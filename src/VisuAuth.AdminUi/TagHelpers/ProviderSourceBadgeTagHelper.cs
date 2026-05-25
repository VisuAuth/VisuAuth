using Microsoft.AspNetCore.Razor.TagHelpers;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Tiny badge with an inline SVG glyph (database or code chevrons) used by
/// the external-providers admin page to mark whether a given field is
/// sourced from the dynamic store (DB) or from static config (appsettings,
/// Program.cs lambda, env vars). Both badges may render on the same cell —
/// the partial that calls this helper adds a tooltip explaining that the
/// DB value wins at runtime.
/// </summary>
/// <remarks>
/// Usage: <c>&lt;va-provider-source kind="db" title="..." /&gt;</c> or
/// <c>kind="code"</c>. <c>kind</c> is required; <c>title</c> is rendered
/// verbatim as the HTML <c>title</c> attribute for the tooltip.
/// </remarks>
[HtmlTargetElement("va-provider-source", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ProviderSourceBadgeTagHelper : TagHelper
{
    /// <summary>Either <c>db</c> or <c>code</c>. Anything else falls back to the code glyph.</summary>
    public string? Kind { get; set; }

    /// <summary>Visible label inside the badge (e.g. "from DB", "do banco").</summary>
    public string? Label { get; set; }

    /// <summary>Hover tooltip — usually a sentence explaining who wins / what this means.</summary>
    public string? Title { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var isDb = string.Equals(Kind, "db", StringComparison.OrdinalIgnoreCase);
        var svg = isDb ? ProviderIconSvgs.DatabaseBadge : ProviderIconSvgs.CodeBadge;
        var cssVariant = isDb ? "va-source-badge-db" : "va-source-badge-code";

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"va-source-badge {cssVariant}");
        if (!string.IsNullOrWhiteSpace(Title))
        {
            output.Attributes.SetAttribute("title", Title);
        }
        // Icon-only badge — the SVG carries the meaning; the Label moves to
        // aria-label so screen readers still announce "from DB" / "from code".
        // The hover Title remains for sighted users who want the prose.
        if (!string.IsNullOrWhiteSpace(Label))
        {
            output.Attributes.SetAttribute("aria-label", Label);
        }
        output.Content.SetHtmlContent(svg);
    }
}
