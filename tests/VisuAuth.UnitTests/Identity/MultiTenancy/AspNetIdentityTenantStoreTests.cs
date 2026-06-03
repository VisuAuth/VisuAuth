using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using VisuAuth.Identity.Auditing;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.MultiTenancy;

/// <summary>
/// Unit coverage for the validation / not-found branches of
/// <see cref="AspNetIdentityTenantStore{TUser}"/>, backed by an EF Core
/// in-memory metadata context. The cross-cutting happy paths run in the
/// integration suite; these guard clauses are pinned here.
/// </summary>
public sealed class AspNetIdentityTenantStoreTests : IDisposable
{
    private readonly TestMetadataDbContext _db;
    private readonly AspNetIdentityTenantStore<TestUser> _store;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero));

    public AspNetIdentityTenantStoreTests()
    {
        var options = new DbContextOptionsBuilder<TestMetadataDbContext>()
            .UseInMemoryDatabase($"tenants-{Guid.NewGuid():N}")
            .Options;
        _db = new TestMetadataDbContext(options);
        _store = new AspNetIdentityTenantStore<TestUser>(_db, MockUserManager().Object, _time);
    }

    [Fact]
    public async Task CreateAsync_WithBlankId_ReturnsFailure()
    {
        var result = await _store.CreateAsync("   ", displayName: "Acme");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Tenant id is required.");
    }

    [Fact]
    public async Task CreateAsync_WhenTenantAlreadyExists_ReturnsFailure()
    {
        await SeedTenantAsync("acme", "Acme");

        var result = await _store.CreateAsync("acme", displayName: "Acme Again");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task RenameAsync_WithBlankDisplayName_ReturnsFailure()
    {
        var result = await _store.RenameAsync("acme", "   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Display name is required.");
    }

    [Fact]
    public async Task RenameAsync_WhenTenantNotFound_ReturnsFailure()
    {
        var result = await _store.RenameAsync("missing", "New Name");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("missing").And.Contain("not found");
    }

    [Fact]
    public async Task RenameAsync_OnExistingTenant_PersistsNewDisplayName()
    {
        await SeedTenantAsync("acme", "Acme");

        var result = await _store.RenameAsync("acme", "  Acme Corp  ");

        result.IsSuccess.Should().BeTrue();
        var stored = await _db.VisuAuthTenants.SingleAsync(t => t.Id == "acme");
        stored.DisplayName.Should().Be("Acme Corp", "the new label is trimmed and persisted");
    }

    [Fact]
    public async Task DeleteAsync_WhenTenantNotFound_ReturnsFailure()
    {
        var result = await _store.DeleteAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedTenantAsync(string id, string displayName)
    {
        _db.VisuAuthTenants.Add(new VisuAuthTenant
        {
            Id = id,
            DisplayName = displayName,
            CreatedAt = _time.GetUtcNow(),
        });
        await _db.SaveChangesAsync();
    }

    private static Mock<UserManager<TestUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<TestUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.SetupGet(o => o.Value).Returns(new IdentityOptions());
        return new Mock<UserManager<TestUser>>(
            store.Object,
            optionsAccessor.Object,
            new PasswordHasher<TestUser>(),
            Array.Empty<IUserValidator<TestUser>>(),
            Array.Empty<IPasswordValidator<TestUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new Mock<Microsoft.Extensions.Logging.ILogger<UserManager<TestUser>>>().Object)
        {
            CallBase = false,
        };
    }

    public sealed class TestUser : IdentityUser, IMultiTenantEntity
    {
        public string? TenantId { get; set; }
    }

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
