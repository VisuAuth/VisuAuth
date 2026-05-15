using FluentAssertions;
using VisuAuth.EndUserUi.TwoFactor;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.TwoFactor;

/// <summary>
/// Unit coverage for the QR-code SVG renderer used by the TOTP setup page.
/// The most load-bearing assertion here is the viewBox post-process — without
/// it CSS scaling produces sub-pixel artefacts that authenticator camera
/// apps refuse to scan.
/// </summary>
public sealed class QrCodeSvgRendererTests
{
    private readonly QrCodeSvgRenderer _renderer = new();

    [Fact]
    public void Render_WithEmptyContent_ReturnsEmptyString()
    {
        _renderer.Render(string.Empty).Should().BeEmpty();
        _renderer.Render("   ").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithOtpAuthUri_EmitsSvgWithViewBox()
    {
        var svg = _renderer.Render("otpauth://totp/Demo:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Demo");

        svg.Should().StartWith("<svg",
            "the renderer must produce an inline SVG (no XML prologue) for embedding in Razor");
        svg.Should().MatchRegex(@"<svg\b[^>]*?viewBox=""0 0 \d+ \d+""",
            "viewBox is mandatory for clean vector scaling under CSS max-width");
        // Width / height stay so CSS that targets them by attribute keeps
        // working; viewBox is purely additive.
        svg.Should().MatchRegex(@"<svg\b[^>]*?width=""\d+""");
        svg.Should().MatchRegex(@"<svg\b[^>]*?height=""\d+""");
    }

    [Fact]
    public void Render_TwiceForSameInput_DoesNotDoubleAddViewBox()
    {
        var svg = _renderer.Render("otpauth://totp/Demo:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Demo");

        var viewBoxOccurrences = System.Text.RegularExpressions.Regex.Count(svg, "viewBox=");
        viewBoxOccurrences.Should().Be(1, "the post-process must be idempotent");
    }

    [Fact]
    public void Render_NativeSize_LargerThanSetupPageDisplaySize()
    {
        // The setup page caps the SVG at max-width: 16rem (~256 px). The
        // native pixel size must comfortably exceed that so any CSS scale
        // is a downscale (cleaner than upscale) — i.e. PixelsPerModule is
        // tuned high enough.
        var svg = _renderer.Render("otpauth://totp/Demo:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Demo");

        var match = System.Text.RegularExpressions.Regex.Match(svg, @"width=""(\d+)""");
        match.Success.Should().BeTrue();
        var width = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        width.Should().BeGreaterThan(256, "native pixel grid must exceed the page's max-width cap");
    }
}
