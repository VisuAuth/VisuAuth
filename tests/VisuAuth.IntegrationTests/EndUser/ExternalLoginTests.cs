using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sample.WebApp.Data;
using VisuAuth.Abstractions.Authentication;
using Xunit;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Integration tests for the external-login pipeline:
/// <c>/visuauth/login</c> button rendering, <c>/external-login/start</c>
/// challenge dispatch, and <c>/external-login/callback</c> + <c>/confirm</c>
/// across the three first-time strategies.
/// </summary>
public sealed partial class ExternalLoginTests(ExternalLoginTestFactory factory) : IClassFixture<ExternalLoginTestFactory>
{
    private static readonly Regex TokenRegex = TokenRegexImpl();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex TokenRegexImpl();

    private readonly ExternalLoginTestFactory _factory = factory;

    [Fact]
    public async Task GetLogin_WithRegisteredProvider_RendersContinueWithButton()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"GET /login must succeed. Status={response.StatusCode}, Body head:\n{body.Substring(0, Math.Min(body.Length, 800))}");
        body.Should().Contain($"value=\"{ExternalLoginTestFactory.TestProviderScheme}\"",
            "the hidden scheme input must carry the registered provider name");
        body.Should().Contain("Continue with Test Provider",
            "the button label must render via the localized 'Continue with {0}' template");
        body.Should().Contain("class=\"va-divider\"",
            "the 'or' divider must render between the password form and the providers");
    }

    [Fact]
    public async Task PostExternalLoginStart_WithRegisteredScheme_Returns302ToProvider()
    {
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["scheme"] = ExternalLoginTestFactory.TestProviderScheme,
            ["returnUrl"] = "/visuauth/admin/users",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/external-login/start", UriKind.Relative),
            form);

        // The fake NoOp handler returns 401 from its default Challenge —
        // a real OAuth handler would return 302 to the provider. Either
        // way, the assertion is the same: the start handler called Challenge,
        // it did NOT bounce back to /login (which would mean the scheme was
        // rejected), and it did NOT render a Razor page body.
        var status = (int)response.StatusCode;
        status.Should().Match(s => s == 401 || (s >= 300 && s <= 399),
            "Challenge must hand off to the registered handler — either redirect (real OAuth) or 401 (no-op test handler)");
        response.Headers.Location?.OriginalString.Should().NotStartWith("/visuauth/login",
            "the start handler must NOT bounce back to /login when the scheme is registered");
    }

    [Fact]
    public async Task PostExternalLoginStart_WithUnknownScheme_RedirectsBackToLogin()
    {
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["scheme"] = "NonExistentProvider",
            ["returnUrl"] = "/visuauth/admin/users",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/external-login/start", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/visuauth/login",
            "an unknown scheme must NOT trigger a Challenge — the user is bounced back to /login");
    }

    [Fact]
    public async Task Callback_AutoCreate_WithFreshExternalIdentity_CreatesUserAndSignsIn()
    {
        SetStrategy(ExternalLoginFirstTimeStrategy.AutoCreate);

        var providerKey = $"key-{Guid.NewGuid():N}";
        var email = $"autocreate.{Guid.NewGuid():N}@example.com";

        using var client = CreateClient();

        // The fake test endpoint stamps the external cookie + redirects to
        // the callback in one shot — same effect a real OAuth provider has
        // at the end of the dance. Crucially, name="Thiago Lugarini" includes
        // a SPACE — Identity's default AllowedUserNameCharacters rejects
        // spaces, so the adapter MUST fall back to email as the UserName
        // instead of the display-name claim. This is a regression guard for
        // the real-world "Username 'thiago lugarini' is invalid, can only
        // contain letters or digits" failure that surfaced with Microsoft.
        var response = await client.GetAsync(new Uri(
            $"/test/external-signin?providerKey={providerKey}&email={Uri.EscapeDataString(email)}&name={Uri.EscapeDataString("Thiago Lugarini")}",
            UriKind.Relative));

        // Callback redirects to "/" (no returnUrl) on success.
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/",
            "AutoCreate must finish the sign-in and land on the safe local default");

        // Verify the local user was created and the external login linked.
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await um.FindByEmailAsync(email);
        created.Should().NotBeNull("AutoCreate must provision a local user");
        created!.EmailConfirmed.Should().BeTrue("the provider already verified the email");

        var logins = await um.GetLoginsAsync(created);
        logins.Should().ContainSingle(l =>
            l.LoginProvider == ExternalLoginTestFactory.TestProviderScheme && l.ProviderKey == providerKey,
            "the external login must be linked to the new user");
        // Regression: the UserName must be the email, NOT the display-name
        // claim that contains a space.
        created.UserName.Should().Be(email,
            "AutoCreate must use the email as UserName so display names with spaces never trip Identity's char allowlist");
    }

    [Fact]
    public async Task Callback_AutoLinkByEmailOrConfirm_WithMatchingEmail_LinksToExistingUserAndSignsIn()
    {
        SetStrategy(ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm);

        // Pre-create the local user the external identity should auto-link to.
        var email = $"link.{Guid.NewGuid():N}@example.com";
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existing = new ApplicationUser
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
                TenantId = "acme",
            };
            (await um.CreateAsync(existing, "Pa$$w0rd!")).Succeeded.Should().BeTrue();
            userId = existing.Id;
        }

        var providerKey = $"key-{Guid.NewGuid():N}";
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(
            $"/test/external-signin?providerKey={providerKey}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/",
            "auto-link must complete sign-in and skip the confirm page");

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifier.FindByIdAsync(userId);
        var logins = await verifier.GetLoginsAsync(refreshed!);
        logins.Should().ContainSingle(l =>
            l.LoginProvider == ExternalLoginTestFactory.TestProviderScheme && l.ProviderKey == providerKey,
            "the external login must attach to the pre-existing user, not create a new one");
    }

    [Fact]
    public async Task Callback_AutoLinkByEmailOrConfirm_WithUnknownEmail_RedirectsToConfirmPage()
    {
        SetStrategy(ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm);

        var providerKey = $"key-{Guid.NewGuid():N}";
        var email = $"unknown.{Guid.NewGuid():N}@example.com";

        using var client = CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/test/external-signin?providerKey={providerKey}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/visuauth/external-login/confirm",
            "an unknown email under AutoLink must fall through to the confirm page");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(email, "the confirm form must pre-fill the email from the provider claim");
    }

    [Fact]
    public async Task Callback_AlwaysConfirm_EvenWithMatchingEmail_RedirectsToConfirmPage()
    {
        SetStrategy(ExternalLoginFirstTimeStrategy.AlwaysConfirm);

        // Pre-create a user with the same email — under AlwaysConfirm we
        // must STILL go through the confirm step.
        var email = $"always.{Guid.NewGuid():N}@example.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existing = new ApplicationUser
            {
                Email = email, UserName = email, EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow, TenantId = "acme",
            };
            (await um.CreateAsync(existing, "Pa$$w0rd!")).Succeeded.Should().BeTrue();
        }

        using var client = CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/test/external-signin?providerKey={Guid.NewGuid():N}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/visuauth/external-login/confirm",
            "AlwaysConfirm must show the confirm page regardless of email match");
    }

    [Fact]
    public async Task Callback_WithRemoteError_RedirectsToLoginWithErrorMessage()
    {
        // Simulate the OAuth provider sending us back with ?remoteError=...
        // — same shape ASP.NET Identity uses. The callback page must NOT
        // attempt to read the external cookie in that branch.
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(
            "/visuauth/external-login/callback?remoteError=AccessDenied",
            UriKind.Relative));

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/visuauth/login",
            "a provider-side error must bounce the user back to /login, not into the strategy");
    }

    [Fact]
    public async Task Callback_WithoutExternalCookie_RedirectsToLoginWithError()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(
            "/visuauth/external-login/callback",
            UriKind.Relative));

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/visuauth/login",
            "no external cookie means the OAuth dance never completed — bounce to /login");
    }

    private void SetStrategy(ExternalLoginFirstTimeStrategy strategy)
    {
        // The factory boots once per fixture, so the options are shared
        // across tests. Mutating the options instance directly is fine —
        // tests run serially (DisableTestParallelization).
        var options = _factory.Services.GetRequiredService<IOptions<ExternalLoginOptions>>();
        options.Value.FirstTimeStrategy = strategy;
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
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} must succeed");
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
