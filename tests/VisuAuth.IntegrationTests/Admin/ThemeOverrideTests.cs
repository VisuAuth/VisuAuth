using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Programmatic theming end-to-end: the sample app configures a
/// <c>SampleThemes</c> preset in <c>Program.cs</c>; here we assert the
/// inline <c>&lt;style data-visuauth-theme&gt;</c> block ships on admin
/// <em>and</em> end-user pages and carries a <c>--visuauth-primary</c>
/// override. The exact hex isn't pinned so the suite survives any preset
/// swap as long as the preset touches <see cref="VisuAuthTheme.Primary"/>
/// — every <c>SampleThemes</c> preset except <c>Default</c> and <c>Serif</c>
/// does, and those two intentionally skip this contract.
/// </summary>
public sealed class ThemeOverrideTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetUsers_WithSampleAppThemeConfigured_EmitsInlineStyleOverride()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("data-visuauth-theme",
            "the layout must mount <va-theme-style /> after the default stylesheet");
        body.Should().Contain("--visuauth-primary:",
            "the sample preset must override at least the primary colour");
    }

    [Fact]
    public async Task GetUsers_ThemeStyleBlock_AppearsAfterDefaultStylesheet()
    {
        // Source order is load-bearing: the inline override only wins if it
        // ships AFTER the default stylesheet link.
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        var cssLinkIndex = body.IndexOf("visuauth.css", StringComparison.Ordinal);
        var themeBlockIndex = body.IndexOf("data-visuauth-theme", StringComparison.Ordinal);

        cssLinkIndex.Should().BeGreaterThan(-1, "the default stylesheet link must be present");
        themeBlockIndex.Should().BeGreaterThan(cssLinkIndex,
            "the override <style> must come after the default <link> for the cascade to favour it");
    }

    [Fact]
    public async Task GetLogin_WithSampleAppThemeConfigured_EmitsInlineStyleOverrideOnEndUserPagesToo()
    {
        // Admin and end-user pages share the same VisuAuthTheme bag, so
        // any branding the consumer configures applies to the public
        // sign-in pages without extra wiring.
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("data-visuauth-theme");
        body.Should().Contain("--visuauth-primary:");
    }
}
