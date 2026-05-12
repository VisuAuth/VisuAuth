using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Drop-in <see cref="IdentityDbContext{TUser}"/> that wires multi-tenancy
/// for the <typeparamref name="TUser"/> entity. The global query filter
/// short-circuits when multi-tenancy is disabled, so single-tenant tests and
/// seeders that bypass the resolver middleware still work.
/// </summary>
/// <typeparam name="TUser">The consumer's user type — must implement
/// <see cref="IMultiTenantEntity"/>.</typeparam>
public abstract class MultiTenantIdentityDbContext<TUser> : IdentityDbContext<TUser>
    where TUser : IdentityUser, IMultiTenantEntity
{
    private readonly ITenantContext _tenantContext;

    protected MultiTenantIdentityDbContext(
        DbContextOptions options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // The filter is evaluated at query-execution time, so reading instance
        // fields here is safe — EF Core re-checks them per query thanks to the
        // closure capturing `this`.
        builder.Entity<TUser>()
            .HasQueryFilter(u =>
                !_tenantContext.IsMultiTenancyEnabled
                || _tenantContext.CurrentTenantId == null
                || u.TenantId == _tenantContext.CurrentTenantId);
    }
}

/// <summary>
/// Variant for consumers that customise the role type. Filters the user
/// entity exactly the same way; roles are global today (per-tenant roles
/// are slated for the multi-tenancy follow-up).
/// </summary>
public abstract class MultiTenantIdentityDbContext<TUser, TRole> : IdentityDbContext<TUser, TRole, string>
    where TUser : IdentityUser, IMultiTenantEntity
    where TRole : IdentityRole
{
    private readonly ITenantContext _tenantContext;

    protected MultiTenantIdentityDbContext(
        DbContextOptions options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.Entity<TUser>()
            .HasQueryFilter(u =>
                !_tenantContext.IsMultiTenancyEnabled
                || _tenantContext.CurrentTenantId == null
                || u.TenantId == _tenantContext.CurrentTenantId);
    }
}
