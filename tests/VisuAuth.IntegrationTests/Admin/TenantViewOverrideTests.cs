using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// End-to-end checks for per-tenant view overrides (CLAUDE.md §8.4
/// layers 3+4 composed). The sample app's
/// <c>SampleTenantViewOverrideResolver</c> maps the seeded
/// <c>acme</c> tenant to <c>/Views/VisuAuth/Tenants/acme/</c>, which
/// holds a banner-tagged <c>_UsersTable.cshtml</c>. Switching the
/// sidebar tenant cookie must visibly swap which override file
/// renders for the next request.
/// </summary>
public sealed partial class TenantViewOverrideTests(VisuAuthTestFactory factory)
    : IClassFixture<VisuAuthTestFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    [Fact]
    public async Task GetUsers_AfterSwitchingToAcme_RendersAcmeTenantOverride()
    {
        // Resolver: acme → /Views/VisuAuth/Tenants/acme. The expander
        // must prepend that root ahead of the global override, so the
        // acme-branded template wins for this request.
        using var client = await SwitchTenantAsync("acme");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("data-va-override=\"sample-acme\"",
            "the acme override is the only template that emits this marker");
        body.Should().NotContain("data-va-override=\"sample\"",
            "the global sample override must NOT win when a more specific tenant override exists");
    }

    [Fact]
    public async Task GetUsers_AfterSwitchingToGlobex_FallsBackToGlobalOverride()
    {
        // SampleTenantViewOverrideResolver returns null for globex, so
        // the per-tenant slot is empty and the global /Views/VisuAuth/
        // override (the layer-3 demo banner) takes effect instead.
        using var client = await SwitchTenantAsync("globex");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("data-va-override=\"sample\"",
            "globex has no per-tenant override → the global sample override applies");
        body.Should().NotContain("data-va-override=\"sample-acme\"");
    }

    [Fact]
    public async Task GetUsers_HtmxPartialRequest_AlsoResolvesPerTenantOverride()
    {
        // htmx swaps go through the same view engine pipeline; the
        // per-tenant override must reach partial-only renders too,
        // otherwise the swap would flip back to the global template.
        using var client = await SwitchTenantAsync("acme");
        var request = new HttpRequestMessage(HttpMethod.Get, "/visuauth/admin/users");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("<!doctype html>", "htmx mode skips the layout");
        body.Should().Contain("data-va-override=\"sample-acme\"",
            "the acme override must also win on htmx swaps for visual consistency");
    }

    [Fact]
    public async Task GetUsers_AfterSwitchingBackFromAcmeToUnknownTenant_DoesNotServeStaleAcmeMarkup()
    {
        // Razor caches view-location lookups. The expander stashes the
        // resolved tenant id in the cache key so the cached entry for
        // tenant A is invalidated the moment the request switches to
        // tenant B — otherwise tenant B would render with A's template.
        using var client = await SwitchTenantAsync("acme");

        // First request renders acme's template.
        var first = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        (await first.Content.ReadAsStringAsync())
            .Should().Contain("data-va-override=\"sample-acme\"");

        // Switch to globex, which has no per-tenant override.
        await SwitchTenantAsync("globex", existingClient: client);

        var second = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var secondBody = await second.Content.ReadAsStringAsync();

        secondBody.Should().NotContain("data-va-override=\"sample-acme\"",
            "Razor's view-location cache must be invalidated by tenant change");
        secondBody.Should().Contain("data-va-override=\"sample\"",
            "globex falls through to the global sample override");
    }

    private async Task<HttpClient> SwitchTenantAsync(string tenantId, HttpClient? existingClient = null)
    {
        var client = existingClient ?? factory.CreateClient(new WebApplicationFactoryClientOptions
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
