namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Default <see cref="ITenantViewOverrideResolver"/> registered by
/// <c>AddVisuAuthAdminUi</c>. Always returns <see langword="null"/> so
/// the expander skips the per-tenant slot entirely — the
/// single-tenant / no-per-tenant-overrides fast path costs nothing.
/// </summary>
internal sealed class NoOpTenantViewOverrideResolver : ITenantViewOverrideResolver
{
    public string? ResolveOverrideRoot(string? tenantId) => null;
}
