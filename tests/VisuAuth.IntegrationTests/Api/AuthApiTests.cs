using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sample.WebApp.Data;
using VisuAuth.Abstractions.Authentication;
using Xunit;

namespace VisuAuth.IntegrationTests.Api;

/// <summary>
/// Integration tests for the mobile / native JWT REST API at
/// <c>/visuauth/api/auth</c>.
/// </summary>
public sealed class AuthApiTests : IClassFixture<LegacyRefreshTokenFactory>
{
    private static readonly Uri LoginUri = new("/visuauth/api/auth/login", UriKind.Relative);
    private static readonly Uri RegisterUri = new("/visuauth/api/auth/register", UriKind.Relative);
    private static readonly Uri RefreshUri = new("/visuauth/api/auth/refresh", UriKind.Relative);

    private readonly LegacyRefreshTokenFactory _factory;

    public AuthApiTests(LegacyRefreshTokenFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLogin_WithValidCredentials_ReturnsSignedJwt()
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
    public async Task PostLogin_WithTenantUser_AttachesTenantIdClaim()
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
    public async Task PostLogin_WithWrongPassword_Returns401WithoutToken()
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
    public async Task PostLogin_WithUnknownEmail_ReturnsSameGeneric401()
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
    public async Task PostLogin_WithMissingFields_Returns400()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(LoginUri, new { email = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostRegister_WithValidPayload_CreatesUserAndReturnsJwt()
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
    public async Task PostRegister_WithDuplicateEmail_Returns400WithIdentityErrors()
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
    public async Task PostRefresh_WithValidBearer_ReturnsFreshJwt()
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
    public async Task PostRefresh_WithoutBearer_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(RefreshUri, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithMalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", "Bearer not-a-real-jwt");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithForgedSignature_Returns401()
    {
        using var client = _factory.CreateClient();

        // A structurally valid token for an arbitrary subject, with the correct
        // issuer / audience but signed with a key the server does not know.
        // Before the signature fix this minted a real token for any `sub` — a
        // pre-auth account-takeover primitive.
        var forged = BuildToken(
            subject: Guid.NewGuid().ToString(),
            signingKey: "attacker-controlled-key-that-is-at-least-32-bytes!!",
            expires: DateTime.UtcNow.AddHours(1));

        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", $"Bearer {forged}");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithExpiredButAuthenticToken_ReturnsFreshJwt()
    {
        using var client = _factory.CreateClient();

        // Grab a genuine user id so IssueAsync can reload the user.
        var loginBody = await ReadJsonAsync(await client.PostAsJsonAsync(LoginUri, new
        {
            email = "laura.matos@example.com",
            password = "Pa$$w0rd!",
        }));
        var userId = loginBody.GetProperty("userId").GetString()!;

        // Authentic signature and the user's current security stamp, but
        // expired ten minutes ago. Refresh must still accept it — accepting
        // expired (yet un-revoked) tokens is the whole point of refresh.
        var expired = BuildToken(
            userId,
            SampleSigningKey,
            DateTime.UtcNow.AddMinutes(-10),
            stamp: await GetSecurityStampAsync(userId));

        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", $"Bearer {expired}");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostRefresh_WithWrongIssuer_Returns401()
    {
        using var client = _factory.CreateClient();

        // Correct signing key, but a foreign issuer — must be rejected.
        var wrongIssuer = BuildToken(
            subject: Guid.NewGuid().ToString(),
            signingKey: SampleSigningKey,
            expires: DateTime.UtcNow.AddHours(1),
            issuer: "https://evil.example");

        using var refresh = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refresh.Headers.Add("Authorization", $"Bearer {wrongIssuer}");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Signing key / issuer / audience the sample app configures in Program.cs.
    private const string SampleSigningKey = "sample-dev-signing-key-do-not-use-in-production-or-anywhere-else";
    private const string SampleIssuer = "VisuAuth.Sample";

    private async Task<string> GetSecurityStampAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        return await userManager.GetSecurityStampAsync(user!);
    }

    private static string BuildToken(
        string subject,
        string signingKey,
        DateTime expires,
        string issuer = SampleIssuer,
        string? stamp = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (stamp is not null)
        {
            claims.Add(new Claim(VisuAuthClaimTypes.SecurityStamp, stamp));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: SampleIssuer,
            claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
