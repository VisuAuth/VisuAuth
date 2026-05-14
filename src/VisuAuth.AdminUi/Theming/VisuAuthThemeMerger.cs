namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Overlay one <see cref="VisuAuthTheme"/> on top of another for theming
/// layer 4 (per-tenant). Non-blank properties from the override win;
/// properties left null in the override fall through to the base; both
/// null means the CSS default in <c>visuauth.css</c> applies.
/// </summary>
/// <remarks>
/// Pulled out of <see cref="VisuAuthThemeCssRenderer"/> so the merge
/// logic is unit-testable on its own and the renderer keeps its single
/// responsibility (theme → CSS string).
/// </remarks>
public static class VisuAuthThemeMerger
{
    /// <summary>
    /// Merges <paramref name="overrides"/> on top of <paramref name="base"/>.
    /// </summary>
    /// <param name="base">Underlying theme (typically <c>IOptions&lt;VisuAuthTheme&gt;.Value</c>).</param>
    /// <param name="overrides">Per-tenant overrides (typically the result of <see cref="ITenantThemeResolver.ResolveAsync"/>).</param>
    /// <returns>
    /// A new <see cref="VisuAuthTheme"/>; never returns the inputs by
    /// reference so callers can't mutate either side accidentally. When
    /// <paramref name="overrides"/> is <see langword="null"/>, returns a
    /// copy of <paramref name="base"/>.
    /// </returns>
    public static VisuAuthTheme Merge(VisuAuthTheme @base, VisuAuthTheme? overrides)
    {
        ArgumentNullException.ThrowIfNull(@base);

        if (overrides is null)
        {
            return Copy(@base);
        }

        return new VisuAuthTheme
        {
            Primary = Pick(overrides.Primary, @base.Primary),
            PrimaryFg = Pick(overrides.PrimaryFg, @base.PrimaryFg),
            Bg = Pick(overrides.Bg, @base.Bg),
            Fg = Pick(overrides.Fg, @base.Fg),
            Muted = Pick(overrides.Muted, @base.Muted),
            Border = Pick(overrides.Border, @base.Border),
            Surface = Pick(overrides.Surface, @base.Surface),
            Danger = Pick(overrides.Danger, @base.Danger),
            Success = Pick(overrides.Success, @base.Success),
            Radius = Pick(overrides.Radius, @base.Radius),
            Font = Pick(overrides.Font, @base.Font),
        };
    }

    private static string? Pick(string? @override, string? fallback) =>
        // Whitespace counts as "unset" — same rule the renderer applies,
        // so a tenant resolver returning Primary = "  " behaves like
        // Primary = null and the global theme keeps showing through.
        string.IsNullOrWhiteSpace(@override) ? fallback : @override;

    private static VisuAuthTheme Copy(VisuAuthTheme source) => new()
    {
        Primary = source.Primary,
        PrimaryFg = source.PrimaryFg,
        Bg = source.Bg,
        Fg = source.Fg,
        Muted = source.Muted,
        Border = source.Border,
        Surface = source.Surface,
        Danger = source.Danger,
        Success = source.Success,
        Radius = source.Radius,
        Font = source.Font,
    };
}
