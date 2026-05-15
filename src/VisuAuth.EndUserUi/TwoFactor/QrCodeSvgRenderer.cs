using System.Text.RegularExpressions;
using QRCoder;

namespace VisuAuth.EndUserUi.TwoFactor;

/// <summary>
/// Thin façade over QRCoder's <see cref="SvgQRCode"/> so the rest of the
/// end-user UI never depends on the QRCoder API surface directly. Lets us
/// swap the renderer (or the dependency) without touching the page model.
/// </summary>
public interface IQrCodeSvgRenderer
{
    /// <summary>
    /// Renders <paramref name="content"/> as an SVG string suitable for
    /// inline embedding inside a Razor page (no surrounding <c>&lt;?xml&gt;</c>
    /// declaration). Returns an empty string when the content is empty.
    /// </summary>
    string Render(string content);
}

/// <summary>
/// Default <see cref="IQrCodeSvgRenderer"/> backed by QRCoder. Picked
/// medium error correction (Q) so the centre logo overlay (future) does not
/// corrupt the code; Q gives ~25% damage tolerance and still scans on every
/// authenticator app we've tested with.
/// </summary>
public sealed partial class QrCodeSvgRenderer : IQrCodeSvgRenderer
{
    // 8 pixels per module — large enough that camera apps lock on quickly,
    // small enough that the SVG isn't overweight in HTML.
    private const int PixelsPerModule = 8;

    /// <inheritdoc />
    public string Render(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        // drawQuietZones: true — the 4-module quiet border around the QR
        // is part of the spec; without it many scanners refuse to lock.
        var raw = svg.GetGraphic(
            PixelsPerModule,
            darkColorHex: "#000000",
            lightColorHex: "#FFFFFF",
            drawQuietZones: true);

        // QRCoder emits absolute width / height with no viewBox. When CSS
        // resizes the SVG (max-width on mobile, browser zoom, etc.) the
        // browser falls back to bitmap-style scaling, producing sub-pixel
        // module edges that authenticator camera scanners often refuse to
        // lock on to. Adding a viewBox flips the rendering to true vector
        // scaling — module edges stay aligned at every display size.
        return AddViewBox(raw);
    }

    private static string AddViewBox(string svg)
    {
        var match = SvgRootAttributesRegex().Match(svg);
        if (!match.Success)
        {
            return svg;
        }

        var width = match.Groups["w"].Value;
        var height = match.Groups["h"].Value;
        if (svg.Contains("viewBox=", StringComparison.OrdinalIgnoreCase))
        {
            return svg;
        }

        // Insert viewBox immediately after the height attribute. Width and
        // height stay in place so consumers that style the SVG by attribute
        // (e.g. legacy CSS) keep working; the viewBox just unlocks vector
        // scaling whenever the rendered size differs from the native one.
        var insertion = $" viewBox=\"0 0 {width} {height}\"";
        return svg.Insert(match.Index + match.Length, insertion);
    }

    [GeneratedRegex(@"<svg\b[^>]*?\bwidth=""(?<w>\d+)""[^>]*?\bheight=""(?<h>\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SvgRootAttributesRegex();
}
