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
public abstract class MultiTenantIdentityDbContext<TUser> : IdentityDbContext<TUser>, IVisuAuthMetadataDbContext
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
    public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // Query filter parameters must be field / property accesses on the
        // DbContext — EF Core re-reads them per query. Method calls inside
        // the filter expression do not translate to SQL.
        builder.Entity<TUser>()
            .HasQueryFilter(u =>
                !_tenantContext.IsMultiTenancyEnabled
                || _tenantContext.CurrentTenantId == null
                || u.TenantId == _tenantContext.CurrentTenantId);

        ConfigureVisuAuthTenant(builder);
    }

    internal static void ConfigureVisuAuthTenant(ModelBuilder builder)
    {
        builder.Entity<VisuAuthTenant>(entity =>
        {
            entity.ToTable("VisuAuthTenants");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasMaxLength(64).IsRequired();
            entity.Property(t => t.DisplayName).HasMaxLength(256).IsRequired();
        });
    }
}

/// <summary>
/// Variant for consumers that customise the role type. Filters the user
/// entity exactly the same way; roles are global today (per-tenant roles
/// are slated for the multi-tenancy follow-up).
/// </summary>
public abstract class MultiTenantIdentityDbContext<TUser, TRole>
    : IdentityDbContext<TUser, TRole, string>, IVisuAuthMetadataDbContext
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
    public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();

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

        MultiTenantIdentityDbContext<TUser>.ConfigureVisuAuthTenant(builder);
    }
}
