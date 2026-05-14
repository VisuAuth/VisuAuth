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
    /// Full dark theme. Flips <see cref="VisuAuthTheme.Bg"/>,
    /// <see cref="VisuAuthTheme.Fg"/>, <see cref="VisuAuthTheme.Surface"/>,
    /// and <see cref="VisuAuthTheme.Border"/> so the entire layout
    /// (sidebar, cards, tables, inputs) inverts in one go. Useful to
    /// stress-test that every component honours the variables instead
    /// of hard-coding colours.
    /// </summary>
    public static void Midnight(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Primary = "#818cf8";
        theme.PrimaryFg = "#0f172a";
        theme.Bg = "#0f172a";
        theme.Fg = "#e2e8f0";
        theme.Muted = "#94a3b8";
        theme.Surface = "#1e293b";
        theme.Border = "#334155";
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
