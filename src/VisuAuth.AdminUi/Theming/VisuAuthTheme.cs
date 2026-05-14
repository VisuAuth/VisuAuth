namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Programmatic theming (CLAUDE.md §8.4, layer 2). One nullable string per
/// CSS custom property declared in <c>visuauth.css</c>. Anything left
/// <c>null</c> or blank falls through to the stylesheet's built-in value.
/// </summary>
/// <remarks>
/// Consumers configure this through the standard options pipeline:
///
/// <code>
/// services.AddVisuAuth&lt;ApplicationUser&gt;();
/// services.Configure&lt;VisuAuthTheme&gt;(theme =&gt;
/// {
///     theme.Primary   = "#7c3aed";
///     theme.PrimaryFg = "#ffffff";
/// });
/// </code>
///
/// At render time <see cref="VisuAuthThemeCssRenderer"/> turns the populated
/// properties into a <c>:root { … }</c> block emitted right after the
/// default stylesheet, so the cascade lets the overrides win without
/// forking <c>visuauth.css</c>.
///
/// View override (layer 3) and per-tenant theme (layer 4) ship in v0.2.
/// </remarks>
public sealed class VisuAuthTheme
{
    /// <summary>Maps to <c>--visuauth-primary</c> (e.g. <c>#7c3aed</c>).</summary>
    public string? Primary { get; set; }

    /// <summary>Maps to <c>--visuauth-primary-fg</c> — the foreground used on top of <see cref="Primary"/>.</summary>
    public string? PrimaryFg { get; set; }

    /// <summary>Maps to <c>--visuauth-bg</c> — page / card background.</summary>
    public string? Bg { get; set; }

    /// <summary>Maps to <c>--visuauth-fg</c> — base text colour.</summary>
    public string? Fg { get; set; }

    /// <summary>Maps to <c>--visuauth-muted</c> — secondary text.</summary>
    public string? Muted { get; set; }

    /// <summary>Maps to <c>--visuauth-border</c>.</summary>
    public string? Border { get; set; }

    /// <summary>Maps to <c>--visuauth-surface</c> — table headers, sidebar, hover rows.</summary>
    public string? Surface { get; set; }

    /// <summary>Maps to <c>--visuauth-danger</c>.</summary>
    public string? Danger { get; set; }

    /// <summary>Maps to <c>--visuauth-success</c>.</summary>
    public string? Success { get; set; }

    /// <summary>Maps to <c>--visuauth-radius</c> — corner radius (e.g. <c>0.5rem</c>).</summary>
    public string? Radius { get; set; }

    /// <summary>Maps to <c>--visuauth-font</c> — the font stack.</summary>
    public string? Font { get; set; }
}
