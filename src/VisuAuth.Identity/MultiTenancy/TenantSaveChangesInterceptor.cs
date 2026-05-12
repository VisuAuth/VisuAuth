using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// EF Core interceptor that auto-populates <c>TenantId</c> on every newly
/// inserted <see cref="IMultiTenantEntity"/>. Entities that already carry a
/// tenant id (e.g. seeders writing across tenants) are left alone.
/// </summary>
/// <remarks>
/// Scoped lifetime: one instance per request. Reads
/// <see cref="ITenantContext.CurrentTenantId"/> through DI so it stays in
/// sync with the resolver middleware.
/// </remarks>
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext =
        tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null || !_tenantContext.IsMultiTenancyEnabled)
        {
            return;
        }

        var tenantId = _tenantContext.CurrentTenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity is IMultiTenantEntity multiTenant &&
                string.IsNullOrEmpty(multiTenant.TenantId))
            {
                multiTenant.TenantId = tenantId;
            }
        }
    }
}
