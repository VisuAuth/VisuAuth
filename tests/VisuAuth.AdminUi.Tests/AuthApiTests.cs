using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sample.WebApp.Data;

using Xunit;

namespace VisuAuth.AdminUi.Tests;

/// <summary>
/// Integration tests for the mobile / native JWT REST API at
/// <c>/visuauth/api/auth</c>.
/// </summary>
public sealed class AuthApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Uri LoginUri = new("/visuauth/api/auth/login", UriKind.Relative);
    private static readonly Uri RegisterUri = new("/visuauth/api/auth/register", UriKind.Relative);
    private static readonly Uri RefreshUri = new("/visuauth/api/auth/refresh", UriKind.Relative);

    private readonly WebApplicationFactory<Program> _factory;

    public AuthApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_a_signed_jwt()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new
        {
            email = "admin@visuauth.dev",
            password = "Pa$$w0rd!",
        });
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessToken = body.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrWhiteSpace();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Issuer.Should().Be("VisuAuth.Sample");
        jwt.Audiences.Should().Contain("VisuAuth.Sample");
        jwt.Subject.Should().NotBeNullOrEmpty();
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == "admin@visuauth.dev");
    }

    [Fact]
    public async Task Login_attaches_tenant_id_claim_for_a_multi_tenant_user()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new
        {
            email = "alice.silva@example.com", // seeded into tenant 'acme'
            password = "Pa$$w0rd!",
        });
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("tenantId").GetString().Should().Be("acme",
            "the JWT response payload must surface the user's tenant");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("accessToken").GetString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "acme",
            "the encoded JWT must carry the tenant_id claim for downstream API gates");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_and_no_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new
        {
            email = "admin@visuauth.dev",
            password = "wrong",
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("Email or password is incorrect",
            "the API mirrors the web sign-in's anti-enumeration response");
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_the_same_generic_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new
        {
            email = $"unknown.{Guid.NewGuid():N}@example.com",
            password = "Whatever!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_missing_fields_returns_400()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new { email = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_creates_a_user_and_returns_a_jwt()
    {
        using var client = _factory.CreateClient();
        var email = $"api.register.{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(RegisterUri, new
        {
            email,
            password = "Api!Reg1Pass",
        });
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace(
            "register auto-logs the user in by returning a JWT");
        body.GetProperty("email").GetString().Should().Be(email);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await um.FindByEmailAsync(email)).Should().NotBeNull();
    }

    [Fact]
    public async Task Register_with_duplicate_email_returns_400_with_identity_errors()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(RegisterUri, new
        {
            email = "joao.kruger@example.com", // seeded
            password = "Dup!Pass1word",
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().MatchRegex("already taken|already (in )?use|Duplicate");
    }

    [Fact]
    public async Task Refresh_with_a_valid_bearer_returns_a_brand_new_jwt()
    {
        using var client = _factory.CreateClient();

        // Step 1: sign in to get a baseline token.
        var loginResponse = await client.PostAsJsonAsync(LoginUri, new
        {
            email = "laura.matos@example.com",
            password = "Pa$$w0rd!",
        });
        var loginBody = await ReadJsonAsync(loginResponse);
        var originalToken = loginBody.GetProperty("accessToken").GetString()!;

        // Step 2: refresh.
        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", $"Bearer {originalToken}");
        var refreshResponse = await client.SendAsync(refresh);
        var refreshBody = await ReadJsonAsync(refreshResponse);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = refreshBody.GetProperty("accessToken").GetString()!;
        newToken.Should().NotBe(originalToken, "refresh must mint a fresh token (different jti)");

        var newJwt = new JwtSecurityTokenHandler().ReadJwtToken(newToken);
        newJwt.Subject.Should().Be(loginBody.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Refresh_without_bearer_returns_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(RefreshUri, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_malformed_token_returns_401()
    {
        using var client = _factory.CreateClient();

        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", "Bearer not-a-real-jwt");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
