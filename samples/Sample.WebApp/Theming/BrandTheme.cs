using VisuAuth.AdminUi.Theming;

namespace Sample.WebApp.Theming;

/// <summary>
/// VisuAuth Layer-2 brand preset.
///
/// Wire it once in Program.cs, after AddVisuAuth(...):
///
/// <code>
/// builder.Services.Configure&lt;VisuAuthTheme&gt;(BrandTheme.Apply);
/// </code>
///
/// These eleven properties are the only ones the package renders inline
/// (via the &lt;va-theme-style /&gt; tag helper) right after visuauth.css, so
/// they take effect on the very first server render with NO flash and NO
/// extra HTTP request. They MUST stay in sync with the light-mode values in
/// <c>wwwroot/css/visuauth-brand.css</c> — the CSS file is what adds dark
/// mode, the dashboard "extended" tokens, and the component refinements on
/// top of this baseline.
///
/// If you only need a palette change and don't want the CSS file / layout
/// overrides at all, this preset alone is enough to rebrand the whole admin
/// + end-user UI.
/// </summary>
public static class BrandTheme
{
    public static void Apply(VisuAuthTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // Brand
        theme.Primary   = "#6366f1";   // ← keep identical to --visuauth-primary in visuauth-brand.css
        theme.PrimaryFg = "#ffffff";

        // Neutrals
        theme.Bg      = "#ffffff";
        theme.Fg      = "#0f172a";
        theme.Muted   = "#64748b";
        theme.Border  = "#e2e8f0";
        theme.Surface = "#f8fafc";

        // Status
        theme.Danger  = "#dc2626";
        theme.Success = "#16a34a";

        // Shape & type
        theme.Radius = "0.5rem";
        theme.Font   = "-apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, "
                     + "\"Helvetica Neue\", Arial, sans-serif";
    }
}
