using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Theming layer 3 (CLAUDE.md §8.4) — end-to-end checks. The sample app
/// ships two demo overrides under <c>samples/Sample.WebApp/Views/VisuAuth/</c>:
/// a structure-preserving <c>_UsersTable.cshtml</c> with a visible banner
/// and a <c>Shared/_EndUserLayout.cshtml</c> that mirrors the package
/// default plus a small banner. These tests assert (a) both banners
/// reach the rendered HTML and (b) the page-demotion convention is
/// registered for every VisuAuth Razor Page so consumer overrides at the
/// same <c>@page</c> route win without an <c>AmbiguousMatchException</c>.
/// </summary>
public sealed class ViewOverrideTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetUsers_WithSampleOverrideUsersTable_RendersConsumerBanner()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("data-va-override=\"sample\"",
            "the sample app's _UsersTable.cshtml override must be picked up by the IViewLocationExpander");
        body.Should().Contain("va-override-banner",
            "the consumer override is the only template that emits the .va-override-banner element");
        // Sanity: the structure-preserving override keeps the htmx swap
        // target id so existing client-side wiring keeps working.
        body.Should().Contain("id=\"va-users-table\"");
    }

    [Fact]
    public async Task GetUsers_HtmxPartialRequest_AlsoResolvesOverride()
    {
        // Partials render through the same view engine + location list, so
        // the htmx swap must hit the override too — otherwise full-page
        // load and partial swap diverge and the banner flickers in/out.
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/visuauth/admin/users");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("<!doctype html>", "htmx partial mode must skip the layout");
        body.Should().Contain("data-va-override=\"sample\"",
            "the partial-only response must also resolve through the override path");
    }

    [Fact]
    public async Task GetLogin_WithSampleOverrideLayout_RendersLayoutBanner()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("data-va-override=\"sample\"",
            "the sample app's _EndUserLayout.cshtml override must take effect on every end-user page");
        // The layout override is structure-preserving, so the package's
        // script + stylesheet references still ship — confirm one of each
        // so we know the override didn't accidentally drop them.
        body.Should().Contain("/_content/VisuAuth.AdminUi/htmx.min.js");
        body.Should().Contain("/_content/VisuAuth.AdminUi/visuauth.css");
    }

    [Fact]
    public void DemoteVisuAuthPagesConvention_IsRegisteredForEveryVisuAuthAssembly()
    {
        // Full-page override leg of theming layer 3: every Razor Page in
        // VisuAuth.AdminUi / VisuAuth.EndUserUi must be demoted so a
        // consumer page declaring the same @page route wins the
        // lower-order-wins route match. We assert the convention sits in
        // the RazorPages options pipeline; the actual order mutation is
        // exercised by ASP.NET routing the moment a consumer ships an
        // override page (which is too invasive to demo inside this test
        // project without a separate Razor SDK fixture).
        using var scope = factory.Services.CreateScope();
        var pageOptions = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>>()
            .Value;
        var conventions = pageOptions.Conventions.OfType<DemoteVisuAuthPagesConvention>().ToList();

        conventions.Should().HaveCount(2,
            "one convention per VisuAuth UI assembly (AdminUi + EndUserUi)");
        DemoteVisuAuthPagesConvention.OverridableOrder.Should().Be(1000,
            "the demoted order is documented as the sentinel for theming layer 3");
    }
}
