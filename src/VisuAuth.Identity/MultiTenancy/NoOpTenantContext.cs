using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Default <see cref="ITenantContext"/> when the consumer has not called
/// <c>EnableVisuAuthTenancy</c>. Reports multi-tenancy as disabled and returns
/// <c>null</c> for everything else — the query filter on
/// <c>MultiTenantIdentityDbContext</c> short-circuits when this context is in
/// play, so single-tenant deployments are unaffected.
/// </summary>
public sealed class NoOpTenantContext : ITenantContext
{
    public bool IsMultiTenancyEnabled => false;

    public string? CurrentTenantId => null;

    public string? CurrentTenantDisplayName => null;
}
