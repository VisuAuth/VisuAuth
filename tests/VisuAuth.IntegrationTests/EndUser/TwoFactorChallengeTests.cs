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
/// Integration tests for the post-password TOTP challenge at
/// <c>/visuauth/two-factor/verify</c>, including the redirect from
/// <c>/visuauth/login</c> when the user has 2FA enabled.
/// </summary>
public sealed partial class TwoFactorChallengeTests(VisuAuthTestFactory factory) : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = TokenRegexImpl();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex TokenRegexImpl();

    private readonly VisuAuthTestFactory _factory = factory;

    [Fact]
    public async Task PostLogin_WithTwoFactorEnabledUser_RedirectsToVerifyPage()
    {
        var (user, _) = await EnableTwoFactorOnAdHocUserAsync();

        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login?returnUrl=%2Fvisuauth%2Fadmin%2Fusers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/visuauth/admin/users",
            ["Form.Email"] = user.Email!,
            ["Form.Password"] = TwoFactorTestHelpers.DefaultPassword,
            ["Form.RememberMe"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/visuauth/two-factor/verify",
            "RequiresTwoFactor must hand the user off to the challenge page");
        location.Should().Contain("returnUrl=%2Fvisuauth%2Fadmin%2Fusers",
            "the original returnUrl must survive the redirect to the challenge");
        location.Should().Contain("rememberMe=true",
            "the remember-me preference must survive the 2FA hop");
    }

    [Fact]
    public async Task GetVerify_DefaultRender_ShowsBothCodeAndRecoveryFields()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/verify", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Two-factor verification");
        body.Should().Contain("name=\"Form.Code\"");
        body.Should().Contain("name=\"Form.RecoveryCode\"");
    }

    [Fact]
    public async Task PostAuthenticator_WithValidCode_CompletesSignInAndRedirects()
    {
        var (user, _) = await EnableTwoFactorOnAdHocUserAsync();

        using var client = CreateClient(allowRedirects: false);

        // Trigger the partial-2FA cookie via /visuauth/login.
        await PostLoginExpectingTwoFactorAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var code = await TwoFactorTestHelpers.GetCurrentTotpCodeAsync(_factory, user);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/visuauth/admin/users",
            ["rememberMe"] = "true",
            ["Form.Code"] = code,
            ["Form.RememberMachine"] = "false",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Authenticator", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users",
            "a successful challenge must redirect to the original returnUrl");

        // The full Identity cookie should now be present.
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostAuthenticator_WithInvalidCode_ShowsErrorAndKeepsForm()
    {
        var (user, _) = await EnableTwoFactorOnAdHocUserAsync();

        using var client = CreateClient();
        await PostLoginExpectingTwoFactorAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Code"] = "000000",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Authenticator", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue("invalid code re-renders, not redirect");
        body.Should().Contain("That code is invalid");
        // Authenticator failures must NOT trigger the recovery-disclosure
        // open state — the user submitted via the visible form, the recovery
        // section stays collapsed.
        body.Should().NotContain("<details class=\"va-twofactor-recovery\" open>",
            "an authenticator failure must leave the recovery disclosure closed");
    }

    [Fact]
    public async Task PostRecovery_WithInvalidCode_ShowsRecoverySpecificErrorAndKeepsDetailsOpen()
    {
        var (user, _) = await EnableTwoFactorOnAdHocUserAsync();

        // Need a generated batch so the user has *some* recovery codes
        // (the redeem path differs from the no-codes branch).
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var managed = await um.FindByIdAsync(user.Id);
            (await um.GenerateNewTwoFactorRecoveryCodesAsync(managed!, 5))
                .Should().NotBeNull();
        }

        using var client = CreateClient();
        await PostLoginExpectingTwoFactorAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.RecoveryCode"] = "totally-not-a-real-code",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Recovery", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue("invalid recovery code re-renders, not redirect");
        body.Should().Contain("That recovery code is invalid",
            "recovery failures must surface the recovery-specific message, not the generic 'try the latest code from your app'");
        body.Should().NotContain("from your app",
            "the authenticator-app phrasing must not leak into recovery failures");
        body.Should().Contain("<details class=\"va-twofactor-recovery\" open>",
            "the recovery disclosure must auto-open on a recovery error so the user sees both the form and the error");
    }

    [Fact]
    public async Task PostRecovery_WithSeededDemoCode_CompletesSignIn()
    {
        // The sample app pre-enrols twofactor.demo@example.com with a known
        // recovery batch so the challenge flow is exercisable from the home
        // page without first running setup. If this regresses (e.g. seeder
        // silently drops the codes on a refactor), the sample's manual-test
        // story breaks — guard it here.
        using var client = CreateClient(allowRedirects: false);
        await PostLoginExpectingTwoFactorAsync(client, UserSeeder.TwoFactorEnabledUserEmail);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.RecoveryCode"] = UserSeeder.TwoFactorEnabledUserRecoveryCodes[0],
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Recovery", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "the seeded recovery code must complete the 2FA challenge");
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task PostRecovery_WithValidCode_ConsumesCodeAndCompletesSignIn()
    {
        var (user, _) = await EnableTwoFactorOnAdHocUserAsync();

        // Generate a recovery batch we can use in the challenge.
        string recoveryCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var managed = await um.FindByIdAsync(user.Id);
            var codes = await um.GenerateNewTwoFactorRecoveryCodesAsync(managed!, 5);
            codes.Should().NotBeNull();
            recoveryCode = codes!.First();
        }

        using var client = CreateClient(allowRedirects: false);
        await PostLoginExpectingTwoFactorAsync(client, user.Email!);

        var token = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.RecoveryCode"] = recoveryCode,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Recovery", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/",
            "a successful recovery-code sign-in lands on the safe local default when no returnUrl was sent");

        // The recovery code is one-shot — the same code must no longer
        // satisfy the next sign-in attempt.
        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifier.FindByIdAsync(user.Id);
        var redeemAgain = await verifier.RedeemTwoFactorRecoveryCodeAsync(refreshed!, recoveryCode);
        redeemAgain.Succeeded.Should().BeFalse(
            "Identity must mark the recovery code as consumed after a successful challenge");
    }

    private async Task<(ApplicationUser User, string Key)> EnableTwoFactorOnAdHocUserAsync()
    {
        var user = await TwoFactorTestHelpers.CreateAdHocUserAsync(_factory, "challenge");
        var key = await TwoFactorTestHelpers.EnableTotpAsync(_factory, user);
        return (user, key);
    }

    private static async Task PostLoginExpectingTwoFactorAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.Password"] = TwoFactorTestHelpers.DefaultPassword,
        });
        // Don't follow the 302 — we just need the partial 2FA cookie to land
        // in the cookie container so the next request looks "mid-challenge".
        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.OK);
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
