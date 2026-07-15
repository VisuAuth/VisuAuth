using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Identity.Auditing;
using VisuAuth.Identity.Authentication;

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
    public DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog
        => Set<VisuAuthAuditLogEntry>();

    /// <inheritdoc />
    public DbSet<VisuAuthAdapterConfig> VisuAuthAdapterConfigs
        => Set<VisuAuthAdapterConfig>();

    /// <inheritdoc />
    public DbSet<VisuAuthRefreshToken> VisuAuthRefreshTokens
        => Set<VisuAuthRefreshToken>();

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
        ConfigureVisuAuthAuditLog(builder);
        ConfigureVisuAuthAdapterConfig(builder);
        ConfigureVisuAuthRefreshToken(builder);
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

    internal static void ConfigureVisuAuthAuditLog(ModelBuilder builder)
    {
        builder.Entity<VisuAuthAuditLogEntry>(entity =>
        {
            entity.ToTable("VisuAuthAuditLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            // Conservative column widths so the table indexes well on
            // SQLite / SQL Server / PostgreSQL without per-provider tuning.
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetId).HasMaxLength(256);
            entity.Property(e => e.TargetLabel).HasMaxLength(256);
            entity.Property(e => e.FailureReason).HasMaxLength(1024);
            entity.Property(e => e.ActorUserId).HasMaxLength(64);
            entity.Property(e => e.ActorEmail).HasMaxLength(256);
            entity.Property(e => e.ActorIpAddress).HasMaxLength(64);
            entity.Property(e => e.ActorUserAgent).HasMaxLength(512);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            // PayloadJson is intentionally uncapped — payloads are small
            // by convention but providers may grow them; let the DB pick.

            // SQLite (and a couple of other providers) refuse to ORDER BY
            // DateTimeOffset. Convert to UTC DateTime on persistence so the
            // admin page's "newest first" sort works across providers. The
            // round-trip is loss-free because the writer always supplies
            // UTC values via TimeProvider.GetUtcNow().
            entity.Property(e => e.Timestamp)
                .HasConversion(
                    v => v.UtcDateTime,
                    v => new DateTimeOffset(v, TimeSpan.Zero));

            // Indexes mirror the admin page's access patterns. Composite
            // (Timestamp DESC, Id) keeps deterministic ordering when many
            // events share a tick.
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.TargetId);
            entity.HasIndex(e => e.Action);
        });
    }

    internal static void ConfigureVisuAuthAdapterConfig(ModelBuilder builder)
    {
        builder.Entity<VisuAuthAdapterConfig>(entity =>
        {
            entity.ToTable("VisuAuthAdapterConfigs");
            // Synthetic GUID PK; the real invariant (one row per (Adapter, Key))
            // is the unique index below.
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.HasIndex(c => new { c.Adapter, c.Key }).IsUnique();
            entity.Property(c => c.Adapter).HasMaxLength(64).IsRequired();
            entity.Property(c => c.Key).HasMaxLength(128).IsRequired();
            // No length cap on Value — DataProtection ciphertext for secret
            // settings can grow large depending on the protector chain.
        });
    }

    internal static void ConfigureVisuAuthRefreshToken(ModelBuilder builder)
    {
        builder.Entity<VisuAuthRefreshToken>(entity =>
        {
            entity.ToTable("VisuAuthRefreshTokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).ValueGeneratedNever();

            // Redemption looks a token up by its hash, so this index carries
            // the hot path. Unique because a hash collision would mean two
            // users sharing a token.
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();

            entity.Property(t => t.FamilyId).HasMaxLength(32).IsRequired();
            // Replay detection revokes a whole family at once.
            entity.HasIndex(t => t.FamilyId);

            entity.Property(t => t.UserId).HasMaxLength(450).IsRequired();
            // "Revoke everything for this user" and retention sweeps.
            entity.HasIndex(t => t.UserId);

            entity.Property(t => t.ReplacedByHash).HasMaxLength(64);
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
    public DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog
        => Set<VisuAuthAuditLogEntry>();

    /// <inheritdoc />
    public DbSet<VisuAuthAdapterConfig> VisuAuthAdapterConfigs
        => Set<VisuAuthAdapterConfig>();

    /// <inheritdoc />
    public DbSet<VisuAuthRefreshToken> VisuAuthRefreshTokens
        => Set<VisuAuthRefreshToken>();

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
        MultiTenantIdentityDbContext<TUser>.ConfigureVisuAuthAuditLog(builder);
        MultiTenantIdentityDbContext<TUser>.ConfigureVisuAuthAdapterConfig(builder);
        MultiTenantIdentityDbContext<TUser>.ConfigureVisuAuthRefreshToken(builder);
    }
}
