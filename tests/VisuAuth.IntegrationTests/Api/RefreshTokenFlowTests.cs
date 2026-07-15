using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace VisuAuth.IntegrationTests.Api;

/// <summary>
/// End-to-end coverage for the opaque refresh-token flow, which the sample app
/// opts into via <c>AddVisuAuthRefreshTokens()</c>: sign-in returns a refresh
/// token, redeeming it rotates it, and replaying a spent one revokes the family.
/// </summary>
public sealed class RefreshTokenFlowTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Uri LoginUri = new("/visuauth/api/auth/login", UriKind.Relative);
    private static readonly Uri RefreshUri = new("/visuauth/api/auth/refresh", UriKind.Relative);

    private const string Email = "joao.kruger@example.com";
    private const string Password = "Pa$$w0rd!";

    private readonly VisuAuthTestFactory _factory;

    public RefreshTokenFlowTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLogin_WithPluginEnabled_ReturnsARefreshToken()
    {
        using var client = _factory.CreateClient();

        var body = await SignInAsync(client);

        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace(
            "the plugin is enabled, so sign-in must hand the client a refresh token");
    }

    [Fact]
    public async Task PostRefresh_WithValidRefreshToken_RotatesItAndReturnsAFreshAccessToken()
    {
        using var client = _factory.CreateClient();
        var login = await SignInAsync(client);
        var refreshToken = login.GetProperty("refreshToken").GetString()!;

        var body = await RedeemAsync(client, refreshToken, HttpStatusCode.OK);

        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("refreshToken").GetString().Should()
            .NotBeNullOrWhiteSpace().And
            .NotBe(refreshToken, "tokens are single-use: redeeming one must return a replacement");
    }

    [Fact]
    public async Task PostRefresh_WithTheSameTokenTwice_RejectsTheReplay()
    {
        using var client = _factory.CreateClient();
        var login = await SignInAsync(client);
        var original = login.GetProperty("refreshToken").GetString()!;

        // First redemption rotates it.
        await RedeemAsync(client, original, HttpStatusCode.OK);

        // Replaying the spent token must fail — it is single-use.
        await RedeemAsync(client, original, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_AfterAReplay_RevokesTheWholeFamily()
    {
        using var client = _factory.CreateClient();
        var login = await SignInAsync(client);
        var original = login.GetProperty("refreshToken").GetString()!;

        var rotated = (await RedeemAsync(client, original, HttpStatusCode.OK))
            .GetProperty("refreshToken").GetString()!;

        // Replaying the spent token is evidence it leaked: we cannot tell the
        // attacker from the legitimate client, so the whole lineage burns —
        // including the rotated token that was still perfectly valid.
        await RedeemAsync(client, original, HttpStatusCode.Unauthorized);

        await RedeemAsync(client, rotated, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithAnUnknownRefreshToken_Returns401()
    {
        using var client = _factory.CreateClient();

        await RedeemAsync(client, "not-a-token-anyone-ever-issued", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithoutARefreshToken_Returns400()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(RefreshUri, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostRefresh_WithPluginEnabled_IgnoresTheLegacyAccessTokenPath()
    {
        using var client = _factory.CreateClient();
        var login = await SignInAsync(client);
        var accessToken = login.GetProperty("accessToken").GetString()!;

        // The legacy path is deliberately closed once the plugin is on —
        // leaving it open would let a leaked access token keep renewing itself.
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "with the plugin on, refresh wants a refreshToken body, not a bearer access token");
    }

    private async Task<JsonElement> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(LoginUri, new { email = Email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    private async Task<JsonElement> RedeemAsync(HttpClient client, string refreshToken, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync(RefreshUri, new { refreshToken });
        response.StatusCode.Should().Be(expected);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
