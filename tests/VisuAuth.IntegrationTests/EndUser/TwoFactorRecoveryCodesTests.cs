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
/// Integration tests for <c>/visuauth/two-factor/recovery-codes</c> — the
/// post-enable code-management page (also reachable directly to disable 2FA).
/// </summary>
public sealed class TwoFactorRecoveryCodesTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    // Identity's default recovery shape is two groups of alphanumeric
    // characters separated by a hyphen, mixed case + digits. The list page
    // must render each one inside its own copy widget.
    private static readonly Regex RecoveryCodeRegex = new(
        @"<code class=""va-temp-password"" data-va-copy-source>([A-Za-z0-9]{4,})-([A-Za-z0-9]{4,})</code>",
        RegexOptions.Compiled);

    private readonly VisuAuthTestFactory _factory;

    public TwoFactorRecoveryCodesTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRecoveryCodes_AsAnonymousUser_RedirectsToVisuAuthLogin()
    {
        using var client = CreateClient(allowRedirects: false);

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/recovery-codes", UriKind.Relative));

        ((int)response.StatusCode).Should().BeInRange(300, 399,
            "the [Authorize] attribute must keep the page private");
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        location.Should().Contain("/visuauth/login",
            "the cookie LoginPath must point at the VisuAuth login page");
        location.Should().NotContain("/Account/Login",
            "the cookie must not fall back to Identity's /Account/Login default");
    }

    [Fact]
    public async Task GetRecoveryCodes_WithoutTwoFactorEnabled_PromptsToSetup()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "recovery.nope");

        using var client = CreateClient();
        // No 2FA — a plain password sign-in produces the full cookie.
        await SignInWithoutTwoFactorAsync(client, user.Email!);

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/recovery-codes", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Two-factor authentication is not enabled yet");
        body.Should().Contain("/visuauth/two-factor/setup",
            "the page must offer a link back to setup when 2FA is off");
    }

    [Fact]
    public async Task GetRecoveryCodes_WithGeneratedQuery_GeneratesAndDisplaysCodes()
    {
        var user = await EnableTwoFactorOnAdHocUserAsync("recovery.firstvisit");

        using var client = CreateClient();
        await TwoFactorTestHelpers.SignInThroughTwoFactorAsync(_factory, client, user);

        var response = await client.GetAsync(new Uri(
            "/visuauth/two-factor/recovery-codes?generated=true",
            UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Save these codes now",
            "the one-time warning must render alongside the freshly generated codes");
        var matches = RecoveryCodeRegex.Matches(body);
        matches.Count.Should().Be(10, "the page must render exactly the 10 generated codes");
    }

    [Fact]
    public async Task PostGenerate_RegeneratesAndInvalidatesPreviousBatch()
    {
        var user = await EnableTwoFactorOnAdHocUserAsync("recovery.regen");

        // Pre-seed an initial batch so we can prove the regenerate POST
        // invalidates it.
        string firstBatchSample;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var managed = await um.FindByIdAsync(user.Id);
            var initial = await um.GenerateNewTwoFactorRecoveryCodesAsync(managed!, 10);
            initial.Should().NotBeNull();
            firstBatchSample = initial!.First();
        }

        using var client = CreateClient();
        await TwoFactorTestHelpers.SignInThroughTwoFactorAsync(_factory, client, user);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/recovery-codes");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/recovery-codes?handler=Generate", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        var matches = RecoveryCodeRegex.Matches(body);
        matches.Count.Should().Be(10, "the regenerated batch must be exactly ten codes");
        body.Should().NotContain(firstBatchSample,
            "the previous batch must be invalidated and absent from the new render");

        // The previous code must also be unredeemable through Identity.
        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifier.FindByIdAsync(user.Id);
        var redeemOld = await verifier.RedeemTwoFactorRecoveryCodeAsync(refreshed!, firstBatchSample);
        redeemOld.Succeeded.Should().BeFalse("regenerate must invalidate the previous batch end-to-end");
    }

    [Fact]
    public async Task PostDisable_AfterEnabled_ClearsTwoFactorAndRedirectsToSetup()
    {
        var user = await EnableTwoFactorOnAdHocUserAsync("recovery.disable");

        using var client = CreateClient(allowRedirects: false);
        await TwoFactorTestHelpers.SignInThroughTwoFactorAsync(_factory, client, user);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/recovery-codes");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/recovery-codes?handler=Disable", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/two-factor/setup",
            "disable must hand the user back to setup so re-enrolment is one click away");

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifier.FindByIdAsync(user.Id);
        (await verifier.GetTwoFactorEnabledAsync(refreshed!)).Should().BeFalse();
        var key = await verifier.GetAuthenticatorKeyAsync(refreshed!);
        // Disable must wipe the shared key so a stolen QR cannot validate
        // after the user disables.
        key.Should().BeNull();
    }

    private async Task<ApplicationUser> EnableTwoFactorOnAdHocUserAsync(string prefix)
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, prefix);
        await TwoFactorTestHelpers.EnableTotpAsync(_factory, user);
        return user;
    }

    private static async Task SignInWithoutTwoFactorAsync(HttpClient client, string email)
    {
        var loginToken = await GetTokenAsync(client, "/visuauth/login");
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["Form.Email"] = email,
            ["Form.Password"] = TwoFactorTestHelpers.DefaultPassword,
        });
        var loginResponse = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), loginForm);
        loginResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
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
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} must succeed before posting (status {response.StatusCode})");
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
