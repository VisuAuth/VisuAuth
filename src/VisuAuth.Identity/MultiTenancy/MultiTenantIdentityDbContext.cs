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
    public DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
        => Set<VisuAuthExternalProviderConfig>();

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
        ConfigureVisuAuthExternalProviderConfig(builder);
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

    internal static void ConfigureVisuAuthExternalProviderConfig(ModelBuilder builder)
    {
        builder.Entity<VisuAuthExternalProviderConfig>(entity =>
        {
            entity.ToTable("VisuAuthExternalProviderConfigs");
            // Synthetic GUID PK because EF Core refuses NULL key components
            // and TenantId is nullable for the global-config case. The real
            // uniqueness invariant (one row per (scheme, tenant)) lives in
            // the unique index below.
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.HasIndex(c => new { c.Scheme, c.TenantId }).IsUnique();
            entity.Property(c => c.Scheme).HasMaxLength(64).IsRequired();
            entity.Property(c => c.TenantId).HasMaxLength(64);
            entity.Property(c => c.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(c => c.ClientId).HasMaxLength(256);
            // No length cap on EncryptedClientSecret — DataProtection
            // ciphertext can grow large depending on the protector chain.
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
    public DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
        => Set<VisuAuthExternalProviderConfig>();

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
        MultiTenantIdentityDbContext<TUser>.ConfigureVisuAuthExternalProviderConfig(builder);
    }
}
