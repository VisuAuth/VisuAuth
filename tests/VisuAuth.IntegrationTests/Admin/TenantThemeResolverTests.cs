using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// End-to-end checks for theming layer 4 (CLAUDE.md §8.4). The sample
/// app's <c>SampleTenantThemeResolver</c> maps the seeded tenant ids to
/// distinct presets — flipping the sidebar tenant switcher cookie must
/// re-skin the dashboard on the very next request.
/// </summary>
public sealed partial class TenantThemeResolverTests(VisuAuthTestFactory factory)
    : IClassFixture<VisuAuthTestFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    [Fact]
    public async Task GetUsers_WithoutTenantCookie_FallsBackToGlobalThemePrimary()
    {
        // No cookie → SampleTenantThemeResolver returns null → the global
        // theme wins. The sample configures BrandTheme (the brand/dark-mode
        // kit's Layer-2 baseline) in Program.cs; pin its primary.
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("--visuauth-primary: #6366f1",
            "the sample app configures BrandTheme as the global theme — that must apply when no tenant override matches");
    }

    [Fact]
    public async Task GetUsers_AfterSwitchingToAcme_RendersForestPreset()
    {
        // acme → SampleThemes.Forest in SampleTenantThemeResolver.
        // After switching the cookie the next render must emit Forest's
        // green primary AND the props the override didn't touch (PrimaryFg)
        // must still come through from the global BrandTheme.
        using var client = await SwitchTenantAsync("acme");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("--visuauth-primary: #15803d",
            "acme is mapped to Forest, whose primary is #15803d");
        body.Should().Contain("--visuauth-primary-fg: #ffffff",
            "Forest sets PrimaryFg explicitly — it must reach the rendered CSS");
    }

    [Fact]
    public async Task GetUsers_AfterSwitchingToInitech_RendersMidnightPreset()
    {
        // initech → SampleThemes.Midnight (the dark theme). Pin the
        // dark background to make sure the per-tenant override reaches
        // the end-user pages too.
        using var client = await SwitchTenantAsync("initech");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("--visuauth-primary: #818cf8",
            "initech → Midnight has indigo primary on dark surfaces");
        body.Should().Contain("--visuauth-bg: #0f172a",
            "Midnight flips Bg — the strongest signal that the per-tenant override took effect");
    }

    [Fact]
    public async Task GetLogin_WithTenantCookie_AlsoSeesPerTenantPalette()
    {
        // End-user pages share the same <va-theme-style /> tag helper, so
        // a tenant cookie set during admin work must carry through to the
        // public sign-in pages on the next request from the same client.
        using var client = await SwitchTenantAsync("globex");

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("--visuauth-primary: #ea580c",
            "globex → Orange must apply on the public login page too");
    }

    [Fact]
    public async Task GetUsers_AfterSwitchingToUnknownTenant_FallsBackToGlobalTheme()
    {
        // Unknown tenant → SampleTenantThemeResolver returns null →
        // global theme keeps showing through. The fallback is the
        // crucial signal that the merger doesn't break when the
        // resolver has nothing to offer.
        using var client = await SwitchTenantAsync("not-a-real-tenant");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("--visuauth-primary: #6366f1",
            "an unknown tenant must fall through to the global BrandTheme");
    }

    private async Task<HttpClient> SwitchTenantAsync(string tenantId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var pageResponse = await client.GetAsync(new Uri("/visuauth/admin/tenants", UriKind.Relative));
        var pageBody = await pageResponse.Content.ReadAsStringAsync();
        var token = TokenRegex().Match(pageBody).Groups[1].Value;
        token.Should().NotBeNullOrEmpty();

        var switchResponse = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Switch", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["tenantId"] = tenantId,
                ["returnUrl"] = "/visuauth/admin/users",
            }));
        switchResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        return client;
    }
}
