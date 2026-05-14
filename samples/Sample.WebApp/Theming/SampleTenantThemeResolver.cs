using VisuAuth.AdminUi.Theming;

namespace Sample.WebApp.Theming;

/// <summary>
/// Demonstrates theming layer 4 (CLAUDE.md §8.4): map each seeded
/// tenant id to a different brand palette so flipping the sidebar
/// tenant switcher visibly re-skins the dashboard.
/// </summary>
/// <remarks>
/// Production resolvers typically pull from a tenants table (or a
/// per-tenant config blob) — this in-memory mapping keeps the sample
/// self-contained and zero-dependency. The resolver returns
/// <see langword="null"/> for unknown tenants so the global
/// <c>SampleThemes.Purple</c> palette declared in <c>Program.cs</c>
/// shows through unchanged.
/// </remarks>
internal sealed class SampleTenantThemeResolver : ITenantThemeResolver
{
    public Task<VisuAuthTheme?> ResolveAsync(string? tenantId, CancellationToken ct = default)
    {
        VisuAuthTheme? theme = tenantId switch
        {
            "acme"    => Build(SampleThemes.Forest),
            "globex"  => Build(SampleThemes.Orange),
            "initech" => Build(SampleThemes.Midnight),
            _ => null,
        };
        return Task.FromResult(theme);
    }

    /// <summary>
    /// Reuses the existing <see cref="SampleThemes"/> mutators so the
    /// per-tenant overrides stay in sync with the standalone presets
    /// the sample app already ships.
    /// </summary>
    private static VisuAuthTheme Build(Action<VisuAuthTheme> apply)
    {
        var theme = new VisuAuthTheme();
        apply(theme);
        return theme;
    }
}
