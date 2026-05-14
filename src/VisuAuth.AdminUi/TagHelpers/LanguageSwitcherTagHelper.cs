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
/// Renders a <c>POST /visuauth/culture</c> form with a <c>&lt;select&gt;</c>
/// of the configured cultures. Selecting an option auto-submits the
/// form via vanilla JS — same UX as the tenant switcher.
/// </summary>
/// <remarks>
/// Available to both <c>VisuAuth.AdminUi</c> and <c>VisuAuth.EndUserUi</c>
/// pages because <c>_ViewImports.cshtml</c> in both projects already
/// imports tag helpers from this assembly.
///
/// Strings come from <c>AdminSharedResources</c> so the switcher itself
/// is localized.
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

        output.TagName = "form";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("method", "post");
        output.Attributes.SetAttribute("action", CultureSwitchEndpoint.Route);
        output.Attributes.SetAttribute("class",
            string.IsNullOrEmpty(Class) ? "va-lang-switcher" : $"va-lang-switcher {Class}");

        var label = HtmlEncode(_localizer["Common.LanguageLabel"]);
        var sb = new StringBuilder();
        sb.Append("<span class=\"va-field-label\">").Append(label).Append("</span>");
        sb.Append("<input type=\"hidden\" name=\"")
            .Append(HtmlEncode(tokens.FormFieldName))
            .Append("\" value=\"")
            .Append(HtmlEncode(tokens.RequestToken ?? string.Empty))
            .Append("\" />");
        sb.Append("<input type=\"hidden\" name=\"returnUrl\" value=\"")
            .Append(HtmlEncode(returnUrl))
            .Append("\" />");
        sb.Append("<select name=\"")
            .Append(HtmlEncode(settings.FormFieldName))
            .Append("\" class=\"va-input\" onchange=\"this.form.submit();\" aria-label=\"")
            .Append(HtmlEncode(label))
            .Append("\">");
        foreach (var culture in supported)
        {
            var isSelected = string.Equals(culture.Name, current.Name, StringComparison.OrdinalIgnoreCase);
            sb.Append("<option value=\"")
                .Append(HtmlEncode(culture.Name))
                .Append('"');
            if (isSelected)
            {
                sb.Append(" selected");
            }
            sb.Append('>')
                .Append(HtmlEncode(culture.NativeName))
                .Append("</option>");
        }
        sb.Append("</select>");
        sb.Append("<noscript><button type=\"submit\" class=\"va-btn va-btn-small va-btn-ghost\">")
            .Append(HtmlEncode(_localizer["Sidebar.TenantApply"]))
            .Append("</button></noscript>");

        output.Content.SetHtmlContent(sb.ToString());
    }

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
