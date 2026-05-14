namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Default <see cref="ITenantThemeResolver"/> registered by
/// <c>AddVisuAuthAdminUi</c>. Always returns <see langword="null"/> so
/// the global <c>IOptions&lt;VisuAuthTheme&gt;</c> applies unmodified —
/// the single-tenant / no-per-tenant-branding fast path costs nothing.
/// </summary>
internal sealed class NoOpTenantThemeResolver : ITenantThemeResolver
{
    public Task<VisuAuthTheme?> ResolveAsync(string? tenantId, CancellationToken ct = default) =>
        Task.FromResult<VisuAuthTheme?>(null);
}
