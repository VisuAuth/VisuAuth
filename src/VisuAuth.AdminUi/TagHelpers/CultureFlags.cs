using System.Globalization;

namespace VisuAuth.AdminUi.TagHelpers;

/// <summary>
/// Maps a UI culture to a small inline SVG flag for the language switcher.
/// </summary>
/// <remarks>
/// Extensible by design: to support a language added to the app later, add one
/// entry to <see cref="Flags"/> keyed by either the full culture name
/// (e.g. <c>"pt-BR"</c>) or the two-letter language code (e.g. <c>"en"</c>),
/// with an inline SVG drawn on a square <c>0 0 24 24</c> viewBox (the
/// <c>.va-flag</c> container clips it to a circle). Cultures without an entry
/// fall back to a neutral globe, so the switcher always renders.
///
/// The SVGs are intentionally simplified — they are shown at ~20px inside a
/// circular mask, where fine detail (stars, banners, offset saltires) is not
/// legible anyway.
/// </remarks>
internal static class CultureFlags
{
    // Lookup is name-first (pt-BR), then language-only (pt) — see ForCulture.
    private static readonly Dictionary<string, string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = GbFlag,
        ["en-GB"] = GbFlag,
        ["pt-BR"] = BrFlag,
        ["pt"] = BrFlag,
    };

    /// <summary>
    /// Returns the inline SVG markup for <paramref name="culture"/>: an exact
    /// culture-name match wins, then the two-letter language code, otherwise a
    /// neutral globe fallback.
    /// </summary>
    public static string ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (Flags.TryGetValue(culture.Name, out var byName))
        {
            return byName;
        }
        if (Flags.TryGetValue(culture.TwoLetterISOLanguageName, out var byLang))
        {
            return byLang;
        }
        return GlobeFallback;
    }

    // United Kingdom (English): blue field, white + red saltire, white + red cross.
    private const string GbFlag =
        "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\">"
        + "<rect width=\"24\" height=\"24\" fill=\"#012169\"/>"
        + "<path d=\"M0 0L24 24M24 0L0 24\" stroke=\"#ffffff\" stroke-width=\"5\" fill=\"none\"/>"
        + "<path d=\"M0 0L24 24M24 0L0 24\" stroke=\"#c8102e\" stroke-width=\"2\" fill=\"none\"/>"
        + "<path d=\"M12 0V24M0 12H24\" stroke=\"#ffffff\" stroke-width=\"7\" fill=\"none\"/>"
        + "<path d=\"M12 0V24M0 12H24\" stroke=\"#c8102e\" stroke-width=\"4\" fill=\"none\"/>"
        + "</svg>";

    // Brazil (pt-BR): green field, yellow rhombus, blue disc.
    private const string BrFlag =
        "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\">"
        + "<rect width=\"24\" height=\"24\" fill=\"#009b3a\"/>"
        + "<polygon points=\"12,3 21,12 12,21 3,12\" fill=\"#fedf00\"/>"
        + "<circle cx=\"12\" cy=\"12\" r=\"4.2\" fill=\"#002776\"/>"
        + "</svg>";

    // Neutral globe for any culture without a mapped flag.
    private const string GlobeFallback =
        "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\">"
        + "<rect width=\"24\" height=\"24\" fill=\"#cbd5e1\"/>"
        + "<circle cx=\"12\" cy=\"12\" r=\"6.5\" fill=\"none\" stroke=\"#475569\" stroke-width=\"1.5\"/>"
        + "<path d=\"M5.5 12H18.5\" stroke=\"#475569\" stroke-width=\"1.5\" fill=\"none\"/>"
        + "<path d=\"M12 5.5C8.5 8.5 8.5 15.5 12 18.5C15.5 15.5 15.5 8.5 12 5.5Z\" stroke=\"#475569\" stroke-width=\"1.5\" fill=\"none\"/>"
        + "</svg>";
}
