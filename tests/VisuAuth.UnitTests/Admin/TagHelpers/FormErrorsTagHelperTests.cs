using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using VisuAuth.AdminUi.TagHelpers;
using Xunit;

namespace VisuAuth.UnitTests.Admin.TagHelpers;

public sealed class FormErrorsTagHelperTests
{
    [Fact]
    public void Process_NullErrors_SuppressesOutput()
    {
        // Goal: call sites no longer need the outer `@if (Errors.Count > 0)`,
        // so the helper must render nothing when there is nothing to show —
        // not even an empty `<div class="va-alert-danger">`.
        var html = RenderHtml(new FormErrorsTagHelper { Errors = null });

        html.Should().BeEmpty();
    }

    [Fact]
    public void Process_EmptyErrors_SuppressesOutput()
    {
        var html = RenderHtml(new FormErrorsTagHelper { Errors = Array.Empty<string>() });

        html.Should().BeEmpty();
    }

    [Fact]
    public void Process_WithErrors_EmitsDangerAlertWithBulletList()
    {
        var html = RenderHtml(new FormErrorsTagHelper
        {
            Errors = ["Email is required", "Password is too short"],
        });

        html.Should().Contain("class=\"va-alert va-alert-danger\"");
        html.Should().Contain("role=\"alert\"");
        html.Should().Contain("<ul>");
        html.Should().Contain("<li>Email is required</li>");
        html.Should().Contain("<li>Password is too short</li>");
    }

    [Fact]
    public void Process_WithLead_EmitsLeadAsBoldBeforeList()
    {
        var html = RenderHtml(new FormErrorsTagHelper
        {
            Errors = ["x"],
            Lead = "We could not create your account.",
        });

        html.Should().Contain("<strong>We could not create your account.</strong>");
        // Order matters — lead must precede the bullet list, otherwise screen
        // readers announce the errors before their summary.
        var leadIndex = html.IndexOf("<strong>", StringComparison.Ordinal);
        var ulIndex = html.IndexOf("<ul>", StringComparison.Ordinal);
        leadIndex.Should().BeLessThan(ulIndex);
    }

    [Fact]
    public void Process_WithoutLead_OmitsStrongTag()
    {
        var html = RenderHtml(new FormErrorsTagHelper { Errors = ["x"] });

        html.Should().NotContain("<strong>");
    }

    [Fact]
    public void Process_ErrorWithHtmlSpecialCharacters_EscapesContent()
    {
        var html = RenderHtml(new FormErrorsTagHelper
        {
            Errors = ["<script>alert('xss')</script>"],
        });

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Process_LeadWithHtmlSpecialCharacters_EscapesContent()
    {
        var html = RenderHtml(new FormErrorsTagHelper
        {
            Errors = ["x"],
            Lead = "Oops <bad>",
        });

        html.Should().NotContain("<bad>");
        html.Should().Contain("Oops &lt;bad&gt;");
    }

    private static TagHelperOutput Render(FormErrorsTagHelper tagHelper)
    {
        var context = new TagHelperContext(
            tagName: "va-form-errors",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "va-form-errors",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);
        return output;
    }

    private static string RenderHtml(FormErrorsTagHelper tagHelper)
    {
        var output = Render(tagHelper);
        if (output.TagName is null)
        {
            return string.Empty;
        }

        using var writer = new StringWriter();
        writer.Write('<');
        writer.Write(output.TagName);
        foreach (var attr in output.Attributes)
        {
            writer.Write(' ');
            writer.Write(attr.Name);
            writer.Write("=\"");
            writer.Write(attr.Value);
            writer.Write('"');
        }
        writer.Write('>');
        output.Content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        writer.Write("</");
        writer.Write(output.TagName);
        writer.Write('>');
        return writer.ToString();
    }
}
