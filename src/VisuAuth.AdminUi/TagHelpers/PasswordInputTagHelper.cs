using Microsoft.AspNetCore.Razor.TagHelpers;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Renders a labelled password input wrapped in <c>.va-password-wrap</c> with
/// the show/hide toggle button. Replaces the ~25-line markup block that was
/// duplicated across every page with a password field.
/// </summary>
/// <remarks>
/// Usage: <c>&lt;va-password name="Form.Password" label="Password" autocomplete="current-password" autofocus /&gt;</c>
///
/// The emitted HTML must stay byte-identical to the inline block so the
/// <c>PasswordToggleTests</c> regression suite keeps passing without changes
/// (it greps for <c>.va-password-wrap</c>, <c>data-va-password-toggle</c>, and
/// the <c>hidden</c> attribute on the eye-off SVG).
/// </remarks>
[HtmlTargetElement("va-password", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PasswordInputTagHelper : TagHelper
{
    /// <summary>
    /// <c>name</c> attribute on the underlying <c>&lt;input&gt;</c>. Required.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Label text rendered above the input. Defaults to "Password".</summary>
    public string Label { get; set; } = "Password";

    /// <summary>
    /// <c>autocomplete</c> attribute. Typical values: <c>current-password</c>
    /// for sign-in, <c>new-password</c> for create / reset flows.
    /// </summary>
    public string? Autocomplete { get; set; }

    /// <summary>Placeholder rendered inside the empty input.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Emits the <c>autofocus</c> attribute when true.</summary>
    public bool Autofocus { get; set; }

    /// <summary>
    /// Emits the <c>required</c> attribute. Defaults to true — most password
    /// fields are mandatory. The admin "new user" form sets this to false
    /// because a blank password autogenerates a temporary one.
    /// </summary>
    public bool Required { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "va-field");

        var attrs = new System.Text.StringBuilder();
        attrs.Append("type=\"password\" name=\"").Append(HtmlEncode(Name)).Append("\" class=\"va-input\"");
        if (!string.IsNullOrEmpty(Autocomplete))
        {
            attrs.Append(" autocomplete=\"").Append(HtmlEncode(Autocomplete)).Append('"');
        }
        if (!string.IsNullOrEmpty(Placeholder))
        {
            attrs.Append(" placeholder=\"").Append(HtmlEncode(Placeholder)).Append('"');
        }
        if (Required)
        {
            attrs.Append(" required");
        }
        if (Autofocus)
        {
            attrs.Append(" autofocus");
        }

        var content =
            $"<span class=\"va-field-label\">{HtmlEncode(Label)}</span>" +
            "<div class=\"va-password-wrap\">" +
            $"<input {attrs} />" +
            "<button type=\"button\" class=\"va-password-toggle\" data-va-password-toggle aria-label=\"Show password\" aria-pressed=\"false\">" +
            EyeSvg +
            EyeOffSvg +
            "</button>" +
            "</div>";

        output.Content.SetHtmlContent(content);
    }

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    // SVGs match the Feather-style icons that were inline in every .cshtml.
    // The eye-off SVG MUST carry the `hidden` attribute on initial render so
    // both icons never paint at once — the click handler in visuauth.js flips
    // it via element.hidden after each toggle.
    private const string EyeSvg =
        "<svg class=\"va-icon-eye\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">" +
        "<path d=\"M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z\" />" +
        "<circle cx=\"12\" cy=\"12\" r=\"3\" />" +
        "</svg>";

    private const string EyeOffSvg =
        "<svg class=\"va-icon-eye-off\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" hidden>" +
        "<path d=\"M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24\" />" +
        "<line x1=\"1\" y1=\"1\" x2=\"23\" y2=\"23\" />" +
        "</svg>";
}
