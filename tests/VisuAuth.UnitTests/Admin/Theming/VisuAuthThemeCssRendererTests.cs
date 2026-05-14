using FluentAssertions;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// Pins the exact CSS output emitted by <see cref="VisuAuthThemeCssRenderer"/>.
/// The block ships inline in every layout, so a regression here would
/// reach every page of the admin UI and end-user pages.
/// </summary>
public sealed class VisuAuthThemeCssRendererTests
{
    [Fact]
    public void Render_WithNullTheme_ReturnsEmptyString()
    {
        VisuAuthThemeCssRenderer.Render(null).Should().BeEmpty();
    }

    [Fact]
    public void Render_WithEmptyTheme_ReturnsEmptyString()
    {
        VisuAuthThemeCssRenderer.Render(new VisuAuthTheme()).Should().BeEmpty(
            "every property is null, so no override should emit");
    }

    [Fact]
    public void Render_WithBlankProperty_TreatsItAsUnsetAndReturnsEmpty()
    {
        var theme = new VisuAuthTheme { Primary = "   " };

        VisuAuthThemeCssRenderer.Render(theme).Should().BeEmpty(
            "whitespace-only values must be skipped like nulls");
    }

    [Fact]
    public void Render_WithSinglePrimary_EmitsRootBlockWithThatVariable()
    {
        var theme = new VisuAuthTheme { Primary = "#7c3aed" };

        var css = VisuAuthThemeCssRenderer.Render(theme);

        css.Should().Be(":root { --visuauth-primary: #7c3aed; }");
    }

    [Fact]
    public void Render_TrimsLeadingAndTrailingWhitespaceFromValues()
    {
        var theme = new VisuAuthTheme { Primary = "  #7c3aed  " };

        VisuAuthThemeCssRenderer.Render(theme)
            .Should().Be(":root { --visuauth-primary: #7c3aed; }");
    }

    [Fact]
    public void Render_WithEveryProperty_EmitsThemInTheDefaultStylesheetOrder()
    {
        var theme = new VisuAuthTheme
        {
            Primary = "#a", PrimaryFg = "#b", Bg = "#c", Fg = "#d", Muted = "#e",
            Border = "#f", Surface = "#g", Danger = "#h", Success = "#i",
            Radius = "0.25rem", Font = "Inter",
        };

        var css = VisuAuthThemeCssRenderer.Render(theme);

        // Order matches the :root block in visuauth.css so a side-by-side
        // diff stays trivial.
        css.Should().Be(
            ":root { "
            + "--visuauth-primary: #a; "
            + "--visuauth-primary-fg: #b; "
            + "--visuauth-bg: #c; "
            + "--visuauth-fg: #d; "
            + "--visuauth-muted: #e; "
            + "--visuauth-border: #f; "
            + "--visuauth-surface: #g; "
            + "--visuauth-danger: #h; "
            + "--visuauth-success: #i; "
            + "--visuauth-radius: 0.25rem; "
            + "--visuauth-font: Inter;"
            + " }");
    }

    [Fact]
    public void Render_WithFontStackContainingQuotesAndCommas_EmitsThemUnchanged()
    {
        var theme = new VisuAuthTheme
        {
            Font = "-apple-system, \"Segoe UI\", Roboto, sans-serif",
        };

        VisuAuthThemeCssRenderer.Render(theme).Should().Contain(
            "--visuauth-font: -apple-system, \"Segoe UI\", Roboto, sans-serif;",
            "real-world font stacks use commas and quotes — both must pass through");
    }

    [Theory]
    [InlineData("</style>")]
    [InlineData("red; background: url(x)")]
    [InlineData("red} body { display: none")]
    [InlineData("\\65 ")]
    public void Render_WithValueContainingForbiddenChar_Throws(string maliciousValue)
    {
        var theme = new VisuAuthTheme { Primary = maliciousValue };

        var act = () => VisuAuthThemeCssRenderer.Render(theme);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Primary*forbidden character*");
    }
}
