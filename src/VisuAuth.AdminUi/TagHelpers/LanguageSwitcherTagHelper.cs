using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.AdminUi.Localization;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Renders a compact flag-based language switcher: a <c>&lt;details&gt;</c>
/// whose summary is a flag-only button and whose popover lists every
/// configured culture as a <c>POST /visuauth/culture</c> submit button
/// showing the flag plus the language's native name (and a check on the
/// active one).
/// </summary>
/// <remarks>
/// Available to both <c>VisuAuth.AdminUi</c> and <c>VisuAuth.EndUserUi</c>
/// pages because <c>_ViewImports.cshtml</c> in both projects already
/// imports tag helpers from this assembly.
///
/// Flags come from <see cref="CultureFlags"/> (extensible per culture);
/// native names come from the culture itself, so the switcher needs no
/// localized strings of its own. Uses a native <c>&lt;details&gt;</c> so it
/// toggles with zero JS; visuauth.js adds outside-click / Esc close.
/// </remarks>
[HtmlTargetElement("va-language-switcher", TagStructure = TagStructure.WithoutEndTag)]
public sealed class LanguageSwitcherTagHelper(
    IHttpContextAccessor httpContextAccessor,
    IAntiforgery antiforgery,
    IOptions<RequestLocalizationOptions> requestOptions,
    IOptions<VisuAuthLocalizationOptions> vauOptions,
    IStringLocalizer<AdminSharedResources> localizer) : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IAntiforgery _antiforgery = antiforgery;
    private readonly IOptions<RequestLocalizationOptions> _requestOptions = requestOptions;
    private readonly IOptions<VisuAuthLocalizationOptions> _vauOptions = vauOptions;
    private readonly IStringLocalizer<AdminSharedResources> _localizer = localizer;

    /// <summary>
    /// Optional CSS class appended to the rendered <c>&lt;form&gt;</c>.
    /// Layouts use this to apply layout-specific padding / placement
    /// (e.g. the end-user card uses a tighter footer style).
    /// </summary>
    public string? Class { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            output.SuppressOutput();
            return;
        }

        var supported = _requestOptions.Value.SupportedUICultures ?? [];
        if (supported.Count <= 1)
        {
            // Only one culture available → no point rendering the switcher.
            output.SuppressOutput();
            return;
        }

        var settings = _vauOptions.Value;
        var current = httpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture
                      ?? CultureInfo.CurrentUICulture;

        var tokens = _antiforgery.GetAndStoreTokens(httpContext);
        var returnUrl = httpContext.Request.Path + httpContext.Request.QueryString;
        var label = HtmlEncode(_localizer["Common.LanguageLabel"]);

        // Host element becomes a <details> so the popover toggles with no JS.
        output.TagName = "details";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class",
            string.IsNullOrEmpty(Class) ? "va-lang-switcher va-lang" : $"va-lang-switcher va-lang {Class}");

        var sb = new StringBuilder();
        const string closeSpan = "</span>";

        // Collapsed trigger — the current culture's flag only.
        sb.Append("<summary class=\"va-btn va-btn-ghost va-lang-btn\" aria-label=\"").Append(label).Append("\">")
            .Append("<span class=\"va-flag\">").Append(CultureFlags.ForCulture(current)).Append(closeSpan)
            .Append("</summary>");

        // Popover — one submit button per culture: flag + native name (+ check).
        sb.Append("<form class=\"va-lang-menu\" method=\"post\" action=\"").Append(CultureSwitchEndpoint.Route).Append("\">");
        sb.Append("<input type=\"hidden\" name=\"")
            .Append(HtmlEncode(tokens.FormFieldName))
            .Append("\" value=\"")
            .Append(HtmlEncode(tokens.RequestToken ?? string.Empty))
            .Append("\" />");
        sb.Append("<input type=\"hidden\" name=\"returnUrl\" value=\"")
            .Append(HtmlEncode(returnUrl))
            .Append("\" />");
        foreach (var culture in supported)
        {
            var isSelected = string.Equals(culture.Name, current.Name, StringComparison.OrdinalIgnoreCase);
            sb.Append("<button type=\"submit\" name=\"")
                .Append(HtmlEncode(settings.FormFieldName))
                .Append("\" value=\"")
                .Append(HtmlEncode(culture.Name))
                .Append("\" class=\"va-lang-item")
                .Append(isSelected ? " va-active" : string.Empty)
                .Append("\">")
                .Append("<span class=\"va-flag\">").Append(CultureFlags.ForCulture(culture)).Append(closeSpan)
                .Append("<span>").Append(HtmlEncode(culture.NativeName)).Append(closeSpan);
            if (isSelected)
            {
                sb.Append("<span class=\"va-lang-check\">").Append(CheckSvg).Append(closeSpan);
            }
            sb.Append("</button>");
        }
        sb.Append("</form>");

        output.Content.SetHtmlContent(sb.ToString());
    }

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private const string CheckSvg =
        "<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" "
        + "stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
        + "<polyline points=\"20 6 9 17 4 12\"/></svg>";
}
