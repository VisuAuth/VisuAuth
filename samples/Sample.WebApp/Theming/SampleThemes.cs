using VisuAuth.AdminUi.Theming;

namespace Sample.WebApp.Theming;

/// <summary>
/// Preset palettes for the manual-test sample. Each is a plain
/// <c>Action&lt;VisuAuthTheme&gt;</c> so it drops straight into
/// <c>services.Configure&lt;VisuAuthTheme&gt;(SampleThemes.Orange)</c>.
/// Swap a single token in <c>Program.cs</c> to recolor the entire UI
/// without recompiling anything else.
/// </summary>
internal static class SampleThemes
{
    /// <summary>
    /// Stock VisuAuth look (indigo on white). Leaves every property null,
    /// so the inline <c>&lt;style data-visuauth-theme&gt;</c> block is
    /// suppressed and the defaults from <c>visuauth.css</c> stand.
    /// </summary>
    public static void Default(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        // intentionally empty — no overrides
    }

    /// <summary>
    /// Purple primary on the stock surface — the lightest possible
    /// override, useful to confirm the cascade is working without
    /// touching neutrals.
    /// </summary>
    public static void Purple(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Primary = "#7c3aed";
        theme.PrimaryFg = "#ffffff";
    }

    /// <summary>
    /// Warm orange palette: primary plus the surrounding neutrals so the
    /// sidebar and table headers also visibly shift away from slate.
    /// </summary>
    public static void Orange(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Primary = "#ea580c";
        theme.PrimaryFg = "#ffffff";
        theme.Surface = "#fff7ed";
        theme.Border = "#fed7aa";
        theme.Muted = "#9a3412";
    }

    /// <summary>
    /// Forest green — also bumps <see cref="VisuAuthTheme.Success"/> so
    /// the success badges and alerts stay coherent with the new primary.
    /// </summary>
    public static void Forest(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Primary = "#15803d";
        theme.PrimaryFg = "#ffffff";
        theme.Success = "#15803d";
        theme.Surface = "#f0fdf4";
        theme.Border = "#bbf7d0";
    }

    /// <summary>
    /// Deep-indigo brand on a soft tinted page background. The design-system
    /// stylesheet renders cards and popovers on <c>--visuauth-elevated</c> — a
    /// token the Layer-2 theme can't reach — so a full dark flip (dark
    /// <see cref="VisuAuthTheme.Bg"/> + light <see cref="VisuAuthTheme.Fg"/>)
    /// left the white cards carrying unreadable light text. This preset stays
    /// coherent with the light card surfaces: it only tints the page
    /// background and recolours the brand, keeping the default dark text. A
    /// genuine dark UI is available through the built-in light/dark toggle.
    /// </summary>
    public static void Midnight(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Primary = "#4f46e5";
        theme.PrimaryFg = "#ffffff";
        theme.Bg = "#eef2ff";
        theme.Border = "#c7d2fe";
    }

    /// <summary>
    /// Shape-and-type only: leaves every colour alone and changes just
    /// <see cref="VisuAuthTheme.Radius"/> and <see cref="VisuAuthTheme.Font"/>.
    /// Demonstrates that the non-colour tokens flow through the same
    /// pipeline as the palette ones.
    /// </summary>
    public static void Serif(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Radius = "1rem";
        theme.Font = "Georgia, \"Times New Roman\", serif";
    }
}
