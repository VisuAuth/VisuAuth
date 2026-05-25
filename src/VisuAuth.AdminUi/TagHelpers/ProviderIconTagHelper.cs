using Microsoft.AspNetCore.Razor.TagHelpers;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Emits the brand SVG icon for a known external-login provider, or a
/// generic key/person glyph for any other scheme. Used on the
/// "Continue with X" buttons on <c>/visuauth/login</c> + the admin
/// providers list.
/// </summary>
/// <remarks>
/// Usage: <c>&lt;va-provider-icon scheme="Microsoft" /&gt;</c>.
/// The icons are inline SVG (no font dep, no CDN, no extra HTTP requests)
/// and styled via the <c>.va-provider-icon</c> CSS class so a single
/// <c>fill</c>/<c>color</c> change rebrands them all. The catalogue lives
/// in <see cref="ProviderIconSvgs"/>; this helper just dispatches.
/// </remarks>
[HtmlTargetElement("va-provider-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ProviderIconTagHelper : TagHelper
{
    public string? Scheme { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "va-provider-icon");
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Content.SetHtmlContent(GetSvg(Scheme));
    }

    // Switch ordered roughly by popularity so the JIT may benefit from
    // branch prediction on common schemes (Microsoft / Google land first).
    private static string GetSvg(string? scheme) => scheme?.ToLowerInvariant() switch
    {
        "microsoft" => ProviderIconSvgs.Microsoft,
        "google" => ProviderIconSvgs.Google,
        "apple" => ProviderIconSvgs.Apple,
        "github" => ProviderIconSvgs.GitHub,
        "facebook" => ProviderIconSvgs.Facebook,
        "linkedin" => ProviderIconSvgs.LinkedIn,
        "x" or "twitter" => ProviderIconSvgs.X,
        "discord" => ProviderIconSvgs.Discord,
        "slack" => ProviderIconSvgs.Slack,
        "twitch" => ProviderIconSvgs.Twitch,
        "spotify" => ProviderIconSvgs.Spotify,
        "gitlab" => ProviderIconSvgs.GitLab,
        "reddit" => ProviderIconSvgs.Reddit,
        "amazon" => ProviderIconSvgs.Amazon,
        "salesforce" => ProviderIconSvgs.Salesforce,
        "notion" => ProviderIconSvgs.Notion,
        "paypal" => ProviderIconSvgs.PayPal,
        "patreon" => ProviderIconSvgs.Patreon,
        "zoom" => ProviderIconSvgs.Zoom,
        "shopify" => ProviderIconSvgs.Shopify,
        _ => ProviderIconSvgs.Generic,
    };
}
