using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Integration tests for the WebView deep-link flow on
/// <c>/visuauth/login</c>. A successful sign-in with an allowed
/// non-HTTP returnUrl redirects to that URL with the JWT appended.
/// </summary>
public sealed class WebViewCallbackTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public WebViewCallbackTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLogin_WithAllowedDeepLink_AndPreviewModeOn_RendersPreviewWithCallbackUrl()
    {
        using var client = CreateClient(allowRedirects: false);

        var deepLink = "visuauth-sample://auth/callback";
        var token = await GetTokenAsync(client, $"/visuauth/login?returnUrl={Uri.EscapeDataString(deepLink)}");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = deepLink,
            ["Form.Email"] = "joao.kruger@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        // Sample app runs with ShowPreviewPage = true so a desktop developer
        // gets feedback instead of a silent redirect into a scheme the OS
        // does not register.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "preview mode renders the page in-place instead of issuing a 302");
        body.Should().Contain(">Sign-in successful</h1>");
        body.Should().Contain("visuauth-sample://auth/callback#",
            "the callback URL must be visible so the developer can copy / inspect it");
        body.Should().Contain("access_token=");
        body.Should().Contain("expires_at=");
        body.Should().Contain("user_id=");

        // The Continue button must hold the same callback URL so a click
        // fires the real redirect on a device that registers the scheme.
        body.Should().MatchRegex(
            @"<a href=""visuauth-sample://auth/callback#[^""]+"" class=""va-btn va-btn-primary"">Continue \(open the app\)</a>");
    }

    [Fact]
    public async Task PostLogin_WithDisallowedScheme_FallsBackToSafeDefault()
    {
        using var client = CreateClient(allowRedirects: false);

        // 'attacker-app' is not in the sample's allowlist (only 'visuauth-sample' is).
        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "attacker-app://callback",
            ["Form.Email"] = "alice.silva@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/",
            "non-listed schemes must NOT receive the JWT — they're treated like any other non-local URL");
    }

    [Fact]
    public async Task PostLogin_WithHttpReturnUrlEvenIfListed_NeverGetsTokenAppended()
    {
        // The sample app does not list http(s) in AllowedSchemes; even if a
        // future consumer mistakenly adds them, the LoginModel filters them
        // out. We assert the contract end-to-end here: an http URL with a
        // foreign host falls back to the safe default.
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "https://attacker.example/phish",
            ["Form.Email"] = "joao.kruger@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/",
            "http(s) targets are always rejected — open-redirect guard cannot be bypassed via deep-link configuration");
    }

    [Fact]
    public async Task PostLogin_WithMalformedReturnUrl_FallsBackToSafeDefault()
    {
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "not a valid uri at all",
            ["Form.Email"] = "joao.kruger@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task PostLogin_DeepLinkSchemeMatchIsCaseInsensitive()
    {
        using var client = CreateClient(allowRedirects: false);

        // Configured allowlist has 'visuauth-sample'. We send the upper-case
        // variant — must still match (preview page also renders since the
        // sample sets ShowPreviewPage = true).
        var deepLink = "VisuAuth-Sample://callback";
        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = deepLink,
            ["Form.Email"] = "alice.silva@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The Uri class normalises the scheme to lower-case on its output
        // (and may add a trailing '/' to authority-only URIs) — proves the
        // matcher accepted the mixed-case input.
        body.Should().MatchRegex("visuauth-sample://callback/?#",
            "case-insensitive matching must accept VisuAuth-Sample as visuauth-sample");
        body.Should().Contain("access_token=");
    }

    [Fact]
    public async Task PostLogin_WithLocalReturnUrl_StillUsesNormalWebFlow()
    {
        // Regression: the new deep-link branch must not change the existing
        // web behaviour. /visuauth/admin/users must still receive the cookie
        // and a plain redirect.
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login?returnUrl=%2Fvisuauth%2Fadmin%2Fusers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/visuauth/admin/users",
            ["Form.Email"] = "admin@visuauth.dev",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users");

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal),
            "the local-redirect path must still set the cookie — deep-link flow is the exception, not the rule");
    }

    private HttpClient CreateClient(bool allowRedirects = true) => _factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = allowRedirects,
        });

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
