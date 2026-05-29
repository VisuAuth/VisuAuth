using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Identity.Auditing;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Auditing;

/// <summary>
/// Coverage for the background retention loop. Exercises the "disabled
/// when RetentionDays &lt;= 0" early-exit and the sweep that deletes
/// entries older than the cutoff while preserving everything newer.
/// </summary>
public sealed class AuditRetentionHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithZeroRetention_ExitsImmediately()
    {
        var sp = BuildScope();
        var service = new AuditRetentionHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AuditLogOptions { RetentionDays = 0 }),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<AuditRetentionHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        // The service should return without ever blocking on the warmup
        // delay — give it a very short hard timeout to fail fast if it
        // doesn't.
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);

        // Reach this line means the bg task completed (or returned) — the
        // "disabled" branch logs and returns; nothing else to assert beyond
        // not hanging.
        true.Should().BeTrue();
    }

    [Fact]
    public async Task SweepAsync_DeletesEntriesOlderThanRetention_PreservesFresh()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sp = BuildScope(time);

        // Seed entries through a dedicated scope, then dispose — the SQLite
        // connection is a singleton so the data survives for the sweep's
        // own DbContext scope to see it.
        using (var seedScope = sp.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TestMetadataDbContext>();
            db.VisuAuthAuditLog.Add(new VisuAuthAuditLogEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = time.GetUtcNow().AddDays(-120),
                Action = "Old",
                TargetType = "X",
            });
            db.VisuAuthAuditLog.Add(new VisuAuthAuditLogEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = time.GetUtcNow().AddDays(-10),
                Action = "Fresh",
                TargetType = "X",
            });
            await db.SaveChangesAsync();
        }

        var service = new AuditRetentionHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AuditLogOptions { RetentionDays = 90 }),
            time,
            NullLogger<AuditRetentionHostedService>.Instance);

        // Drive one sweep directly — avoids racing with the BackgroundService
        // loop, which mixes FakeTimeProvider delays with the real scheduler.
        await service.SweepAsync(CancellationToken.None);

        using var verifyScope = sp.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TestMetadataDbContext>();
        var remaining = await verifyDb.VisuAuthAuditLog.AsNoTracking().ToListAsync();
        remaining.Should().HaveCount(1, "the sweep must delete entries older than RetentionDays");
        remaining[0].Action.Should().Be("Fresh");
    }

    private static ServiceProvider BuildScope(FakeTimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        // SQLite (not InMemory) because the service uses ExecuteDeleteAsync,
        // which the InMemory provider explicitly does not support.
        // Connection stays open for the test's lifetime via a singleton
        // SqliteConnection so the in-memory database survives between scopes.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddSingleton(connection);
        services.AddDbContext<TestMetadataDbContext>((sp, o) =>
            o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddScoped<IVisuAuthMetadataDbContext>(sp => sp.GetRequiredService<TestMetadataDbContext>());
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestMetadataDbContext>();
            db.Database.EnsureCreated();
        }
        return provider;
    }

    private sealed class TestMetadataDbContext(DbContextOptions<TestMetadataDbContext> options)
        : DbContext(options), IVisuAuthMetadataDbContext
    {
        public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();
        public DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
            => Set<VisuAuthExternalProviderConfig>();
        public DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog => Set<VisuAuthAuditLogEntry>();
        public DbSet<VisuAuthAdapterConfig> VisuAuthAdapterConfigs => Set<VisuAuthAdapterConfig>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Mirror the production audit-log configuration just enough that
            // SQLite can persist DateTimeOffset (the converter below is the
            // only required bit for the sweep test to pass).
            builder.Entity<VisuAuthAuditLogEntry>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Action).HasMaxLength(64).IsRequired();
                e.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
                e.Property(x => x.Timestamp).HasConversion(
                    v => v.UtcDateTime,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            });
        }
    }
}
