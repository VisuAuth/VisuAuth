using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.Identity.Auditing;
using VisuAuth.Identity.Configuration;
using VisuAuth.Identity.DependencyInjection;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Configuration;

/// <summary>
/// Unit coverage for the EF-backed adapter-config store: the encryption
/// boundary for secret values, the tri-state save semantics
/// (preserve / clear / set), and the guarantee that a secret's plaintext is
/// never echoed through the admin-facing <see cref="IAdapterConfigStore.ListAsync"/>.
/// </summary>
public sealed class EfCoreAdapterConfigStoreTests : IDisposable
{
    private const string Adapter = "Entra";

    private readonly TestMetadataDbContext _db;
    private readonly EfCoreAdapterConfigStore _store;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero));

    public EfCoreAdapterConfigStoreTests()
    {
        var options = new DbContextOptionsBuilder<TestMetadataDbContext>()
            .UseInMemoryDatabase($"adaptercfg-{Guid.NewGuid():N}")
            .Options;
        _db = new TestMetadataDbContext(options);
        _store = new EfCoreAdapterConfigStore(_db, new EphemeralDataProtectionProvider(), _time);
    }

    [Fact]
    public async Task SaveAsync_WithNoValues_ReturnsSuccessWithoutTouchingTheStore()
    {
        var result = await _store.SaveAsync(new SaveAdapterConfigCommand
        {
            Adapter = Adapter,
            Values = [],
        });

        result.IsSuccess.Should().BeTrue("an empty save is a no-op, not an error");
        (await _db.VisuAuthAdapterConfigs.CountAsync()).Should().Be(0,
            "nothing should be written when there are no values to apply");
    }

    [Fact]
    public async Task SaveAsync_SecretValue_IsEncryptedAtRest()
    {
        await _store.SaveAsync(Set("ClientSecret", "super-secret", isSecret: true));

        var row = await _db.VisuAuthAdapterConfigs.SingleAsync(c => c.Key == "ClientSecret");
        row.IsSecret.Should().BeTrue();
        row.Value.Should().NotBeNullOrEmpty()
            .And.NotBe("super-secret", "DataProtection must encrypt the secret at rest");
        row.UpdatedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task SaveAsync_NonSecretValue_IsStoredAsPlaintext()
    {
        await _store.SaveAsync(Set("TenantId", "tenant-123", isSecret: false));

        var row = await _db.VisuAuthAdapterConfigs.SingleAsync(c => c.Key == "TenantId");
        row.IsSecret.Should().BeFalse();
        row.Value.Should().Be("tenant-123");
    }

    [Fact]
    public async Task GetResolvedAsync_DecryptsSecrets_AndReturnsNonSecretsPlain()
    {
        await _store.SaveAsync(new SaveAdapterConfigCommand
        {
            Adapter = Adapter,
            Values =
            [
                new() { Key = "TenantId", IsSecret = false, Value = "tenant-123" },
                new() { Key = "ClientSecret", IsSecret = true, Value = "super-secret" },
            ],
        });

        var resolved = await _store.GetResolvedAsync(Adapter);

        resolved["TenantId"].Should().Be("tenant-123");
        resolved["ClientSecret"].Should().Be("super-secret", "the overlay needs the decrypted value");
    }

    [Fact]
    public async Task ListAsync_NeverEchoesSecretPlaintext()
    {
        await _store.SaveAsync(new SaveAdapterConfigCommand
        {
            Adapter = Adapter,
            Values =
            [
                new() { Key = "TenantId", IsSecret = false, Value = "tenant-123" },
                new() { Key = "ClientSecret", IsSecret = true, Value = "super-secret" },
            ],
        });

        var list = await _store.ListAsync(Adapter);

        var secret = list.Single(e => e.Key == "ClientSecret");
        secret.IsSecret.Should().BeTrue();
        secret.HasValue.Should().BeTrue("a secret is stored");
        secret.Value.Should().BeNull("the admin surface never receives a secret's plaintext");

        var tenant = list.Single(e => e.Key == "TenantId");
        tenant.Value.Should().Be("tenant-123", "non-secret values are safe to show");
    }

    [Fact]
    public async Task SaveAsync_NullValue_PreservesExisting()
    {
        await _store.SaveAsync(Set("ClientSecret", "original", isSecret: true));

        // null = preserve
        await _store.SaveAsync(Set("ClientSecret", null, isSecret: true));

        (await _store.GetResolvedAsync(Adapter))["ClientSecret"].Should().Be("original");
    }

    [Fact]
    public async Task SaveAsync_EmptyValue_ClearsTheOverride()
    {
        await _store.SaveAsync(Set("TenantId", "tenant-123", isSecret: false));

        // "" = clear
        await _store.SaveAsync(Set("TenantId", string.Empty, isSecret: false));

        (await _store.GetResolvedAsync(Adapter)).Should().NotContainKey("TenantId");
        (await _db.VisuAuthAdapterConfigs.AnyAsync(c => c.Key == "TenantId")).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ExistingKey_UpdatesValueInPlace()
    {
        await _store.SaveAsync(Set("GraphBaseUrl", "https://graph.microsoft.com/v1.0", isSecret: false));
        await _store.SaveAsync(Set("GraphBaseUrl", "https://graph.microsoft.us/v1.0", isSecret: false));

        var rows = await _db.VisuAuthAdapterConfigs.Where(c => c.Key == "GraphBaseUrl").ToListAsync();
        rows.Should().ContainSingle("the unique (Adapter, Key) is updated, not duplicated");
        rows[0].Value.Should().Be("https://graph.microsoft.us/v1.0");
    }

    [Fact]
    public async Task SaveAsync_DuplicateKeysInOneCommand_LastWriteWins_NoDuplicateRow()
    {
        await _store.SaveAsync(new SaveAdapterConfigCommand
        {
            Adapter = Adapter,
            Values =
            [
                new() { Key = "TenantId", IsSecret = false, Value = "first" },
                new() { Key = "TenantId", IsSecret = false, Value = "second" },
            ],
        });

        var rows = await _db.VisuAuthAdapterConfigs.Where(c => c.Key == "TenantId").ToListAsync();
        rows.Should().ContainSingle("a duplicate key must not insert a second row (unique index)");
        rows[0].Value.Should().Be("second", "last write wins");
    }

    [Fact]
    public void AddVisuAuthAdapterConfigStore_RegistersTheEfStore()
    {
        var services = new ServiceCollection();

        services.AddVisuAuthAdapterConfigStore();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IAdapterConfigStore)
            && d.ImplementationType == typeof(EfCoreAdapterConfigStore));
    }

    public void Dispose() => _db.Dispose();

    private static SaveAdapterConfigCommand Set(string key, string? value, bool isSecret) => new()
    {
        Adapter = Adapter,
        Values = [new() { Key = key, IsSecret = isSecret, Value = value }],
    };

    private sealed class TestMetadataDbContext(DbContextOptions<TestMetadataDbContext> options)
        : DbContext(options), IVisuAuthMetadataDbContext
    {
        public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();
        public DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
            => Set<VisuAuthExternalProviderConfig>();
        public DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog => Set<VisuAuthAuditLogEntry>();
        public DbSet<VisuAuthAdapterConfig> VisuAuthAdapterConfigs => Set<VisuAuthAdapterConfig>();
    }
}
