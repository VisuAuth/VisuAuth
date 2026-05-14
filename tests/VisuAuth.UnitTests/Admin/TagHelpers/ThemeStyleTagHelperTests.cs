using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.AdminUi.TagHelpers;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.TagHelpers;

/// <summary>
/// Unit-level coverage for the per-tenant overlay path through
/// <see cref="ThemeStyleTagHelper"/>. The integration tests assert
/// the overall behaviour through a live request; these target the
/// branch matrix (resolver returns null vs theme, multi-tenancy on
/// vs off, render vs suppress) without needing a host app.
/// </summary>
public sealed class ThemeStyleTagHelperTests
{
    [Fact]
    public async Task ProcessAsync_WithEmptyGlobalThemeAndNoTenantOverride_SuppressesOutput()
    {
        // Both layers empty → renderer returns "" → tag helper drops the
        // entire <style> block. Single-tenant / default-theme fast path.
        var helper = MakeHelper(global: new VisuAuthTheme(), tenantTheme: null);
        var (context, output) = NewContextAndOutput();

        await helper.ProcessAsync(context, output);

        // SuppressOutput nulls the tag name and empties the content; the
        // null tag name is the primary signal Razor uses to drop the
        // element entirely from the rendered HTML.
        output.TagName.Should().BeNull("SuppressOutput must clear the tag for the empty-theme fast path");
    }

    [Fact]
    public async Task ProcessAsync_WithGlobalThemeOnly_RendersGlobalValues()
    {
        var helper = MakeHelper(
            global: new VisuAuthTheme { Primary = "#blue" },
            tenantTheme: null);
        var (context, output) = NewContextAndOutput();

        await helper.ProcessAsync(context, output);

        output.TagName.Should().Be("style");
        output.Attributes["data-visuauth-theme"].Value.Should().Be("true");
        var html = output.Content.GetContent();
        html.Should().Contain("--visuauth-primary: #blue");
    }

    [Fact]
    public async Task ProcessAsync_WithTenantOverride_OverlaysOnGlobal()
    {
        // Tenant overrides Primary; PrimaryFg falls through to global.
        var helper = MakeHelper(
            global: new VisuAuthTheme { Primary = "#blue", PrimaryFg = "#white" },
            tenantTheme: new VisuAuthTheme { Primary = "#red" });
        var (context, output) = NewContextAndOutput();

        await helper.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        html.Should().Contain("--visuauth-primary: #red", "tenant override wins");
        html.Should().Contain("--visuauth-primary-fg: #white", "global fills the gap");
    }

    [Fact]
    public async Task ProcessAsync_WithMultiTenancyDisabled_PassesNullTenantIdToResolver()
    {
        // Single-tenant deployments must call the resolver with null
        // (not the bogus "current tenant" the context might still carry).
        // This guarantees a resolver that branches on null behaves
        // identically whether the consumer turned multi-tenancy on or off.
        var resolver = new Mock<ITenantThemeResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VisuAuthTheme?)null);

        var helper = new ThemeStyleTagHelper(
            Options.Create(new VisuAuthTheme()),
            resolver.Object,
            FakeTenantContext(isOn: false, currentId: "should-be-ignored"));
        var (context, output) = NewContextAndOutput();

        await helper.ProcessAsync(context, output);

        resolver.Verify(r => r.ResolveAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithMultiTenancyEnabled_PassesCurrentTenantIdToResolver()
    {
        var resolver = new Mock<ITenantThemeResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VisuAuthTheme?)null);

        var helper = new ThemeStyleTagHelper(
            Options.Create(new VisuAuthTheme()),
            resolver.Object,
            FakeTenantContext(isOn: true, currentId: "acme"));
        var (context, output) = NewContextAndOutput();

        await helper.ProcessAsync(context, output);

        resolver.Verify(r => r.ResolveAsync("acme", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithNullOutput_Throws()
    {
        var helper = MakeHelper(new VisuAuthTheme(), tenantTheme: null);
        var (context, _) = NewContextAndOutput();

        var act = async () => await helper.ProcessAsync(context, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static ThemeStyleTagHelper MakeHelper(VisuAuthTheme global, VisuAuthTheme? tenantTheme)
    {
        var resolver = new Mock<ITenantThemeResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantTheme);
        return new ThemeStyleTagHelper(
            Options.Create(global),
            resolver.Object,
            FakeTenantContext(isOn: tenantTheme is not null, currentId: "any"));
    }

    private static ITenantContext FakeTenantContext(bool isOn, string? currentId)
    {
        var mock = new Mock<ITenantContext>();
        mock.SetupGet(c => c.IsMultiTenancyEnabled).Returns(isOn);
        mock.SetupGet(c => c.CurrentTenantId).Returns(currentId);
        return mock.Object;
    }

    private static (TagHelperContext Context, TagHelperOutput Output) NewContextAndOutput()
    {
        var context = new TagHelperContext(
            tagName: "va-theme-style",
            allAttributes: [],
            items: new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            tagName: "va-theme-style",
            attributes: [],
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        return (context, output);
    }
}
