using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Integration tests for <c>/visuauth/two-factor/setup</c>.
/// </summary>
public sealed partial class TwoFactorSetupTests(VisuAuthTestFactory factory) : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = TokenRegexImpl();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex TokenRegexImpl();

    private readonly VisuAuthTestFactory _factory = factory;

    [Fact]
    public async Task GetSetup_AsAnonymousUser_RedirectsToVisuAuthLogin()
    {
        using var client = CreateClient(allowRedirects: false);

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/setup", UriKind.Relative));

        ((int)response.StatusCode).Should().BeInRange(300, 399,
            "the [Authorize] attribute must keep the page private");
        // The cookie LoginPath must point at /visuauth/login, not the
        // ASP.NET Identity "/Account/Login" default — otherwise the user
        // hits a 404. AddVisuAuthEndUserUi PostConfigures the cookie
        // options to enforce this.
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        location.Should().Contain("/visuauth/login",
            "the redirect must land on the VisuAuth login page (cookie middleware emits an absolute URL)");
        location.Should().NotContain("/Account/Login",
            "the cookie must not fall back to Identity's /Account/Login default");
        location.Should().Contain("ReturnUrl=",
            "the original requested URL must round-trip via ReturnUrl");
    }

    [Fact]
    public async Task GetSetup_AsAuthenticatedUser_RendersQrCodeAndSharedKey()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "setup.fresh");

        using var client = CreateClient();
        await SignInAsync(client, user.Email!);

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/setup", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Set up two-factor authentication", "the heading must render");
        body.Should().Contain("class=\"va-twofactor-qr\"", "the QR container must be present");
        body.Should().Contain("<svg", "the inline SVG QR must render");
        body.Should().Contain("class=\"va-temp-password\"", "the manual-entry key must be exposed");
        // viewBox is what makes CSS scaling stay vector-clean — without it
        // browsers fall back to bitmap-style scaling and authenticator camera
        // apps refuse to lock on the resulting sub-pixel module edges.
        body.Should().MatchRegex(@"<svg\b[^>]*?viewBox=""0 0 \d+ \d+""",
            "the QR SVG must carry a viewBox so cameras can scan it after CSS scaling");
        body.Should().Contain("data-otpauth-uri=\"otpauth://totp/",
            "the otpauth URI encoded in the QR must also be exposed for desktop copy / debugging");
    }

    [Fact]
    public async Task PostVerify_WithEmptyCode_ReRendersPageWithError()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "setup.empty");

        using var client = CreateClient();
        await SignInAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/setup");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["VerificationCode"] = "",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/setup?handler=Verify", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue("an empty code must keep the form open, not redirect");
        body.Should().Contain("Enter the six-digit verification code");

        // Two-factor must NOT be enabled.
        await EnsureTwoFactorIsAsync(user.Id, expected: false);
    }

    [Fact]
    public async Task PostVerify_WithInvalidCode_ShowsErrorAndDoesNotEnable()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "setup.bad");

        using var client = CreateClient();
        await SignInAsync(client, user.Email!);

        // First GET so the user gets a real shared key.
        var token = await GetTokenAsync(client, "/visuauth/two-factor/setup");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["VerificationCode"] = "000000",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/setup?handler=Verify", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("verification code is invalid", "wrong code must surface a localized error");

        await EnsureTwoFactorIsAsync(user.Id, expected: false);
    }

    [Fact]
    public async Task PostVerify_WithValidCode_EnablesTwoFactorAndRedirectsToRecoveryCodes()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "setup.ok");

        using var client = CreateClient(allowRedirects: false);
        await SignInAsync(client, user.Email!);

        // Hit GET so the page provisions the shared key in storage.
        var token = await GetTokenAsync(client, "/visuauth/two-factor/setup");

        // Live TOTP code from the same provider the page will verify against.
        var code = await TwoFactorTestHelpers.GetCurrentTotpCodeAsync(_factory, user);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["VerificationCode"] = code,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/setup?handler=Verify", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/two-factor/recovery-codes?generated=true",
            "a successful enable must hand the user off to the recovery-codes page");

        await EnsureTwoFactorIsAsync(user.Id, expected: true);
    }

    [Fact]
    public async Task PostResetKey_RotatesAuthenticatorKeyAndShowsNotice()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "setup.rotate");

        using var client = CreateClient();
        await SignInAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/setup");

        // Capture the original key so we can prove the POST rotated it.
        string? originalKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id);
            originalKey = await um.GetAuthenticatorKeyAsync(fresh!);
        }
        originalKey.Should().NotBeNullOrEmpty();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/setup?handler=ResetKey", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Authenticator key rotated");

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifier.FindByIdAsync(user.Id);
        var newKey = await verifier.GetAuthenticatorKeyAsync(refreshed!);
        newKey.Should().NotBe(originalKey, "ResetKey must produce a new shared secret");
    }

    private HttpClient CreateClient(bool allowRedirects = true) => _factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = allowRedirects,
        });

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.Password"] = TwoFactorTestHelpers.DefaultPassword,
        });
        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        // 302 (redirect-back) when AllowAutoRedirect is off; 200 once the
        // root has been followed. Either way the cookie has landed.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
    }

    private async Task EnsureTwoFactorIsAsync(string userId, bool expected)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um.FindByIdAsync(userId);
        var enabled = await um.GetTwoFactorEnabledAsync(refreshed!);
        enabled.Should().Be(expected);
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} must succeed before posting");
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }

}
