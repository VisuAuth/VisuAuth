using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Unit coverage for the EF-backed external-provider config store. Exercises
/// the encryption boundary, the "preserve secret on partial update" rule,
/// and the seed-only-if-missing idempotency contract.
/// </summary>
public sealed class EfCoreExternalProviderConfigStoreTests : IDisposable
{
    private readonly TestMetadataDbContext _db;
    private readonly EfCoreExternalProviderConfigStore _store;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero));

    public EfCoreExternalProviderConfigStoreTests()
    {
        var options = new DbContextOptionsBuilder<TestMetadataDbContext>()
            .UseInMemoryDatabase($"extprov-{Guid.NewGuid():N}")
            .Options;
        _db = new TestMetadataDbContext(options);
        _store = new EfCoreExternalProviderConfigStore(
            _db,
            new EphemeralDataProtectionProvider(),
            _time);
    }

    [Fact]
    public async Task SaveAsync_NewScheme_InsertsRowAndEncryptsSecret()
    {
        var result = await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Microsoft",
            DisplayName = "Microsoft",
            ClientId = "client-123",
            PlainTextClientSecret = "super-secret",
            IsEnabled = true,
        });

        result.IsSuccess.Should().BeTrue();

        var row = await _db.VisuAuthExternalProviderConfigs.SingleAsync();
        row.ClientId.Should().Be("client-123");
        row.EncryptedClientSecret.Should().NotBeNullOrEmpty()
            .And.NotBe("super-secret", "DataProtection must encrypt the secret at rest");
        row.IsEnabled.Should().BeTrue();
        row.UpdatedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task GetClientSecretAsync_AfterSave_RoundTripsThroughDataProtection()
    {
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Google",
            DisplayName = "Google",
            ClientId = "g-client",
            PlainTextClientSecret = "g-secret",
            IsEnabled = true,
        });

        var roundTripped = await _store.GetClientSecretAsync("Google", tenantId: null);

        roundTripped.Should().Be("g-secret",
            "round-trip through Protect/Unprotect must restore the original plaintext");
    }

    [Fact]
    public async Task SaveAsync_UpdateWithoutSecret_PreservesExistingCiphertext()
    {
        // Initial save with secret.
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Apple",
            DisplayName = "Apple",
            ClientId = "apple-1",
            PlainTextClientSecret = "the-real-secret",
            IsEnabled = true,
        });

        // Second save with PlainTextClientSecret = null — admin only edited ClientId.
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Apple",
            DisplayName = "Apple",
            ClientId = "apple-2-renamed",
            PlainTextClientSecret = null,
            IsEnabled = true,
        });

        var secret = await _store.GetClientSecretAsync("Apple", tenantId: null);
        secret.Should().Be("the-real-secret",
            "passing PlainTextClientSecret=null on update must preserve the previously stored ciphertext");

        var view = await _store.GetAsync("Apple", tenantId: null);
        view!.ClientId.Should().Be("apple-2-renamed");
    }

    [Fact]
    public async Task SaveAsync_UpdateWithEmptyStringSecret_ClearsStoredCiphertext()
    {
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "GitHub",
            DisplayName = "GitHub",
            ClientId = "gh-1",
            PlainTextClientSecret = "stored",
            IsEnabled = true,
        });

        // Empty string => admin explicitly cleared the secret.
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "GitHub",
            DisplayName = "GitHub",
            ClientId = "gh-1",
            PlainTextClientSecret = string.Empty,
            IsEnabled = true,
        });

        var view = await _store.GetAsync("GitHub", tenantId: null);
        view!.HasClientSecret.Should().BeFalse(
            "empty plaintext is the documented signal to drop the stored ciphertext");

        var secret = await _store.GetClientSecretAsync("GitHub", tenantId: null);
        secret.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NeverReturnsThePlaintextSecret_OnlyAHasFlag()
    {
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Microsoft",
            DisplayName = "Microsoft",
            ClientId = "x",
            PlainTextClientSecret = "topsecret",
            IsEnabled = true,
        });

        var view = await _store.GetAsync("Microsoft", tenantId: null);

        view!.HasClientSecret.Should().BeTrue();
        // ExternalProviderConfigView has no Plaintext field — proves the
        // shape never leaks the secret to UI callers. Belt-and-suspenders:
        // serialise and confirm.
        var serialized = System.Text.Json.JsonSerializer.Serialize(view);
        serialized.Should().NotContain("topsecret");
    }

    [Fact]
    public async Task EnsureSchemeAsync_FirstCall_InsertsRowWithDefaults()
    {
        var result = await _store.EnsureSchemeAsync(
            "Microsoft", "Microsoft",
            defaultClientId: "from-appsettings",
            defaultIsEnabled: true);

        result.IsSuccess.Should().BeTrue();
        var view = await _store.GetAsync("Microsoft", tenantId: null);
        view!.ClientId.Should().Be("from-appsettings");
        view.IsEnabled.Should().BeTrue();
        view.HasClientSecret.Should().BeFalse("seed never persists secrets — admin owns that path");
    }

    [Fact]
    public async Task EnsureSchemeAsync_WhenRowExists_DoesNotOverwriteAdminEdits()
    {
        // Admin saved real values.
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Microsoft",
            DisplayName = "Microsoft",
            ClientId = "admin-supplied",
            PlainTextClientSecret = "admin-secret",
            IsEnabled = true,
        });

        // Restart calls Ensure again with appsettings defaults — must NOT
        // clobber the admin's edits.
        await _store.EnsureSchemeAsync(
            "Microsoft", "Microsoft",
            defaultClientId: "from-appsettings-DIFFERENT",
            defaultIsEnabled: false);

        var view = await _store.GetAsync("Microsoft", tenantId: null);
        view!.ClientId.Should().Be("admin-supplied",
            "EnsureSchemeAsync is idempotent — never overwrites existing rows");
        view.IsEnabled.Should().BeTrue();
        (await _store.GetClientSecretAsync("Microsoft", tenantId: null))
            .Should().Be("admin-secret");
    }

    [Fact]
    public async Task SetEnabledAsync_FlipsFlagAndUpdatesTimestamp()
    {
        await _store.EnsureSchemeAsync("Microsoft", "Microsoft", defaultIsEnabled: false);
        var before = (await _store.GetAsync("Microsoft", tenantId: null))!;
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await _store.SetEnabledAsync("Microsoft", tenantId: null, isEnabled: true);

        result.IsSuccess.Should().BeTrue();
        var after = (await _store.GetAsync("Microsoft", tenantId: null))!;
        after.IsEnabled.Should().BeTrue();
        after.UpdatedAt.Should().BeAfter(before.UpdatedAt);
    }

    [Fact]
    public async Task SetEnabledAsync_OnUnknownScheme_ReturnsFailure()
    {
        var result = await _store.SetEnabledAsync("NeverRegistered", tenantId: null, true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("NeverRegistered");
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow_AndIsIdempotent()
    {
        await _store.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = "Microsoft",
            DisplayName = "Microsoft",
            ClientId = "id",
            PlainTextClientSecret = "sec",
            IsEnabled = true,
        });
        (await _db.VisuAuthExternalProviderConfigs.CountAsync()).Should().Be(1);

        var first = await _store.DeleteAsync("Microsoft", tenantId: null);
        first.IsSuccess.Should().BeTrue();
        (await _db.VisuAuthExternalProviderConfigs.CountAsync()).Should().Be(0);

        // A second call must not throw — admins double-clicking "Delete" on a
        // ghost row shouldn't get a stack trace; the post-condition (no row)
        // already holds so success is the honest answer.
        var second = await _store.DeleteAsync("Microsoft", tenantId: null);
        second.IsSuccess.Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// In-memory DbContext implementing the metadata contract so the store
    /// works without a full Identity DbContext (which needs Identity table
    /// scaffolding we don't care about for the store under test).
    /// </summary>
    private sealed class TestMetadataDbContext(DbContextOptions<TestMetadataDbContext> options)
        : DbContext(options), IVisuAuthMetadataDbContext
    {
        public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();
        public DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
            => Set<VisuAuthExternalProviderConfig>();
    }
}
