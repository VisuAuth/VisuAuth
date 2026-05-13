using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using VisuAuth.AdminUi.TagHelpers;
using Xunit;

namespace VisuAuth.UnitTests.Admin.TagHelpers;

/// <summary>
/// Pins the markup emitted by <see cref="PasswordInputTagHelper"/>. The
/// integration-level <c>PasswordToggleTests</c> already greps for the same
/// hooks via HTTP responses, but unit tests here document each individual
/// attribute toggle (autofocus, required, placeholder) without needing
/// a host app — and run in milliseconds.
/// </summary>
public sealed class PasswordInputTagHelperTests
{
    [Fact]
    public void Process_WithDefaults_EmitsLabelWrappingPasswordWrapAndToggleButton()
    {
        var html = Render(new PasswordInputTagHelper { Name = "Form.Password" });

        html.Should().Contain("class=\"va-field\"");
        html.Should().Contain("<span class=\"va-field-label\">Password</span>");
        html.Should().Contain("<div class=\"va-password-wrap\">");
        html.Should().Contain("name=\"Form.Password\"");
        html.Should().Contain("type=\"password\"");
        html.Should().Contain("class=\"va-input\"");
        html.Should().Contain("data-va-password-toggle");
        html.Should().Contain("aria-label=\"Show password\"");
        html.Should().Contain("aria-pressed=\"false\"");
    }

    [Fact]
    public void Process_WithDefaults_MarksEyeOffSvgHiddenOnInitialRender()
    {
        // Critical invariant: only one of the two eye icons must paint at first.
        // The visuauth.js click handler flips `element.hidden` between them; if
        // we forget to ship the eye-off SVG hidden out of the gate, the user
        // sees two icons stacked until they click once.
        var html = Render(new PasswordInputTagHelper { Name = "Form.Password" });

        // `\shidden\b` matches the bare HTML `hidden` attribute (whitespace-preceded),
        // and NOT the substring `hidden` inside `aria-hidden="true"` (preceded by `-`).
        html.Should().MatchRegex(@"<svg class=""va-icon-eye-off""[^>]*\shidden\b");
        html.Should().NotMatchRegex(@"<svg class=""va-icon-eye""[^>]*\shidden\b");
    }

    [Fact]
    public void Process_RequiredTrue_EmitsRequiredAttribute()
    {
        var html = Render(new PasswordInputTagHelper { Name = "x", Required = true });

        html.Should().Contain(" required");
    }

    [Fact]
    public void Process_RequiredFalse_OmitsRequiredAttribute()
    {
        // The admin "new user" form sets required=false because a blank
        // password auto-generates a temporary one server-side.
        var html = Render(new PasswordInputTagHelper { Name = "x", Required = false });

        html.Should().NotContain(" required");
    }

    [Fact]
    public void Process_AutofocusTrue_EmitsAutofocusAttribute()
    {
        var html = Render(new PasswordInputTagHelper { Name = "x", Autofocus = true });

        html.Should().Contain(" autofocus");
    }

    [Fact]
    public void Process_AutofocusFalse_OmitsAutofocusAttribute()
    {
        var html = Render(new PasswordInputTagHelper { Name = "x", Autofocus = false });

        html.Should().NotContain(" autofocus");
    }

    [Fact]
    public void Process_WithPlaceholder_EmitsPlaceholderAttribute()
    {
        var html = Render(new PasswordInputTagHelper
        {
            Name = "x",
            Placeholder = "Leave blank to autogenerate",
        });

        html.Should().Contain("placeholder=\"Leave blank to autogenerate\"");
    }

    [Fact]
    public void Process_WithAutocomplete_EmitsAutocompleteAttribute()
    {
        var html = Render(new PasswordInputTagHelper { Name = "x", Autocomplete = "current-password" });

        html.Should().Contain("autocomplete=\"current-password\"");
    }

    [Fact]
    public void Process_WithCustomLabel_EmitsCustomLabelText()
    {
        var html = Render(new PasswordInputTagHelper { Name = "x", Label = "Confirm new password" });

        html.Should().Contain("<span class=\"va-field-label\">Confirm new password</span>");
    }

    [Fact]
    public void Process_NameWithHtmlSpecialCharacters_EscapesAttributeValue()
    {
        // Property binders never produce a name with quotes, but defence in
        // depth: a future caller that templates the name from user input
        // should not be able to break out of the attribute.
        var html = Render(new PasswordInputTagHelper { Name = "evil\"><script>" });

        html.Should().NotContain("<script>");
        html.Should().Contain("&quot;");
    }

    private static string Render(PasswordInputTagHelper tagHelper)
    {
        var context = new TagHelperContext(
            tagName: "va-password",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "va-password",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        using var writer = new StringWriter();
        // Emit the wrapping element (with TagName + Attributes) followed by Content.
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
