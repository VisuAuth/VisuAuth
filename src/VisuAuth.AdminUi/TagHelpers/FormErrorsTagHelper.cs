using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Renders a <c>.va-alert.va-alert-danger</c> block wrapping a bullet list of
/// error messages. Replaces the eight-line <c>@if (Errors.Count &gt; 0)</c>
/// snippet that was duplicated across Register, ResetPassword, and the admin
/// new-user form.
/// </summary>
/// <remarks>
/// Usage: <c>&lt;va-form-errors errors="@Model.Errors" lead="We could not create your account." /&gt;</c>
/// Renders nothing when <paramref name="Errors"/> is null or empty, so call
/// sites no longer need an outer <c>@if</c>.
/// </remarks>
[HtmlTargetElement("va-form-errors", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FormErrorsTagHelper : TagHelper
{
    /// <summary>Error messages to render. Null or empty suppresses the alert entirely.</summary>
    public IEnumerable<string>? Errors { get; set; }

    /// <summary>
    /// Optional bold lead-in sentence rendered before the bullet list
    /// (e.g. "We could not create your account."). Omitted when null.
    /// </summary>
    public string? Lead { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var errors = Errors?.ToArray() ?? Array.Empty<string>();
        if (errors.Length == 0)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "va-alert va-alert-danger");
        output.Attributes.SetAttribute("role", "alert");

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Lead))
        {
            sb.Append("<strong>").Append(System.Net.WebUtility.HtmlEncode(Lead)).Append("</strong>");
        }
        sb.Append("<ul>");
        foreach (var err in errors)
        {
            sb.Append("<li>").Append(System.Net.WebUtility.HtmlEncode(err)).Append("</li>");
        }
        sb.Append("</ul>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}
