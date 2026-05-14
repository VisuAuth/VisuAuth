using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// htmx is bundled inside <c>VisuAuth.AdminUi</c> so air-gapped
/// deployments work out of the box. These tests lock in two things:
/// the asset is reachable through the static-files pipeline at the
/// canonical <c>/_content/VisuAuth.AdminUi/htmx.min.js</c> URL, and
/// no shipped layout still points at the old unpkg CDN.
/// </summary>
public sealed class EmbeddedHtmxAssetTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetEmbeddedHtmx_FromStaticWebAssets_ReturnsJavaScript()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/_content/VisuAuth.AdminUi/htmx.min.js", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            "the embedded htmx asset must be served from VisuAuth.AdminUi/wwwroot");
        // Accept either the modern (RFC 9239) text/javascript or the legacy
        // application/javascript — different ASP.NET Core versions disagree.
        response.Content.Headers.ContentType?.MediaType.Should().BeOneOf(
            "text/javascript",
            "application/javascript");

        // Sanity: ensure we're actually serving htmx, not an empty file.
        body.Should().Contain("htmx",
            "the bundled file must be the real htmx.min.js, not a placeholder");
    }

    [Theory]
    [InlineData("/visuauth/admin/users")]
    [InlineData("/visuauth/login")]
    public async Task GetPageWithLayout_DoesNotReferenceUnpkgCdn(string url)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("unpkg.com",
            $"{url} must load htmx from the embedded static asset, not the public CDN");
        body.Should().MatchRegex(
            @"<script[^>]+src=""/_content/VisuAuth\.AdminUi/htmx\.min\.js",
            $"{url} must reference the embedded htmx asset shipped with VisuAuth.AdminUi");
    }
}
