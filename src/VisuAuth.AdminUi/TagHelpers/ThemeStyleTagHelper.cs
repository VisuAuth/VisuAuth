using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.AdminUi.Theming;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Emits the programmatic theme overrides (CLAUDE.md §8.4 layers 2 + 4)
/// as an inline <c>&lt;style&gt;</c> block. Renders nothing when no
/// overrides are configured, so the layout stays clean for the
/// single-tenant / default-theme case.
/// </summary>
/// <remarks>
/// Placed right after the default <c>visuauth.css</c> link so source
/// order makes the overrides win on the cascade. Both layouts
/// (admin + end-user) emit this helper.
///
/// On every render the helper asks <see cref="ITenantThemeResolver"/>
/// for a per-tenant override and merges it on top of the global
/// <see cref="VisuAuthTheme"/>. The default
/// <see cref="NoOpTenantThemeResolver"/> always returns
/// <see langword="null"/>, so single-tenant deployments and consumers
/// who never opt in keep the layer-2 fast path.
///
/// The <c>data-visuauth-theme</c> attribute on the rendered tag is a
/// stable hook for integration tests — they grep for it instead of
/// relying on the exact CSS body.
/// </remarks>
[HtmlTargetElement("va-theme-style", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ThemeStyleTagHelper(
    IOptions<VisuAuthTheme> options,
    ITenantThemeResolver tenantThemeResolver,
    ITenantContext tenantContext)
    : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var tenantId = tenantContext.IsMultiTenancyEnabled ? tenantContext.CurrentTenantId : null;
        var tenantOverrides = await tenantThemeResolver.ResolveAsync(tenantId).ConfigureAwait(false);
        var merged = VisuAuthThemeMerger.Merge(options.Value, tenantOverrides);

        var css = VisuAuthThemeCssRenderer.Render(merged);
        if (string.IsNullOrEmpty(css))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "style";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("data-visuauth-theme", "true");
        // CSS body — Render() already throws on characters that could break
        // out of the surrounding <style> element, so SetHtmlContent is safe.
        output.Content.SetHtmlContent(css);
    }
}
