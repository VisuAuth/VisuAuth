using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Identity.Auditing;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Auditing;

/// <summary>
/// Coverage for the EF-backed audit store. Exercises the enrichment
/// pipeline (actor / IP / UA / tenant / timestamp pulled from ambient
/// state), the never-throws contract on Write, and the filter+pagination
/// surface that the admin page hits.
/// </summary>
public sealed class EfCoreAuditStoreTests : IDisposable
{
    private readonly TestMetadataDbContext _db;
    private readonly EfCoreAuditStore _store;
    // Start in 2025 so the per-test SetUtcNow calls (which only move forward)
    // never trip FakeTimeProvider's "Cannot go back in time" guard.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly DefaultHttpContext _http = new();
    private readonly StubHttpContextAccessor _httpAccessor;
    private readonly StubTenantContext _tenantContext = new();

    public EfCoreAuditStoreTests()
    {
        var options = new DbContextOptionsBuilder<TestMetadataDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid():N}")
            .Options;
        _db = new TestMetadataDbContext(options);
        _httpAccessor = new StubHttpContextAccessor(_http);
        _store = new EfCoreAuditStore(_db, _httpAccessor, _tenantContext, _time, NullLogger<EfCoreAuditStore>.Instance);
    }

    [Fact]
    public async Task WriteAsync_EnrichesEvent_WithActorIpTenantTimestamp()
    {
        // Stuff the ambient http context the writer reads through.
        _http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-7"),
            new Claim(ClaimTypes.Email, "alice@example.com"),
        ], "test"));
        _http.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 11.0.0.2";
        _http.Request.Headers.UserAgent = "Mozilla/5.0";
        _tenantContext.SetTenant("acme");

        await _store.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserLocked,
            TargetType = AuditTargetTypes.User,
            TargetId = "user-99",
            TargetLabel = "victim@example.com",
        });

        var row = await _db.VisuAuthAuditLog.SingleAsync();
        row.Action.Should().Be(AuditActions.UserLocked);
        row.TargetId.Should().Be("user-99");
        row.TargetLabel.Should().Be("victim@example.com");
        row.ActorUserId.Should().Be("user-7");
        row.ActorEmail.Should().Be("alice@example.com");
        row.ActorIpAddress.Should().Be("10.0.0.1", "first X-Forwarded-For entry is the original client");
        row.ActorUserAgent.Should().Be("Mozilla/5.0");
        row.TenantId.Should().Be("acme");
        row.Timestamp.Should().Be(_time.GetUtcNow());
        row.Outcome.Should().Be(AuditOutcome.Success);
    }

    [Fact]
    public async Task WriteAsync_NeverThrowsToCaller_EvenWhenStoreBlowsUp()
    {
        // Dispose the DbContext under the writer to simulate a "store is
        // unavailable" failure. The write must swallow and the test must
        // reach the assertion line below.
        _db.Dispose();

        var act = async () => await _store.WriteAsync(new AuditEvent
        {
            Action = "Something",
            TargetType = AuditTargetTypes.System,
        });

        await act.Should().NotThrowAsync(
            "auditing must never break the primary action — the contract is to log and swallow");
    }

    [Fact]
    public async Task WriteAsync_SerialisesPayloadAsJson()
    {
        await _store.WriteAsync(new AuditEvent
        {
            Action = AuditActions.RoleAssignedToUser,
            TargetType = AuditTargetTypes.User,
            TargetId = "u",
            Payload = new Dictionary<string, string?> { ["role"] = "Admin", ["scope"] = "tenant" },
        });

        var row = await _db.VisuAuthAuditLog.SingleAsync();
        row.PayloadJson.Should().NotBeNull();
        row.PayloadJson!.Should().Contain("\"role\":\"Admin\"")
            .And.Contain("\"scope\":\"tenant\"");
    }

    [Fact]
    public async Task ListAsync_FiltersAndPaginatesByTimestampDesc()
    {
        // Seed 7 entries spread across two actions, two timestamps.
        for (var i = 0; i < 4; i++)
        {
            _time.SetUtcNow(new DateTimeOffset(2026, 5, 1 + i, 12, 0, 0, TimeSpan.Zero));
            await _store.WriteAsync(new AuditEvent
            {
                Action = AuditActions.LoginSucceeded,
                TargetType = AuditTargetTypes.User,
                TargetId = $"u-{i}",
            });
        }
        for (var i = 0; i < 3; i++)
        {
            _time.SetUtcNow(new DateTimeOffset(2026, 5, 10 + i, 12, 0, 0, TimeSpan.Zero));
            await _store.WriteAsync(new AuditEvent
            {
                Action = AuditActions.LoginFailed,
                TargetType = AuditTargetTypes.User,
                TargetId = $"f-{i}",
                Outcome = AuditOutcome.Failure,
                FailureReason = "InvalidCredentials",
            });
        }

        // Filter by action — only the 3 failures come back.
        var failures = await _store.ListAsync(new AuditFilter { Action = AuditActions.LoginFailed });
        failures.TotalCount.Should().Be(3);
        failures.Items.Should().OnlyContain(e => e.Action == AuditActions.LoginFailed);

        // Cursor pagination — first page of 5 (of 7 total) hands back a cursor.
        var page1 = await _store.ListAsync(new AuditFilter { PageSize = 5 });
        page1.Items.Should().HaveCount(5);
        page1.TotalCount.Should().Be(7);
        page1.NextCursor.Should().NotBeNull("7 entries at a page size of 5 leaves a second page");
        // Newest first → first item is the LATEST failure entry.
        page1.Items[0].TargetId.Should().Be("f-2");

        // Following the cursor returns the remaining 2 (oldest) entries and
        // exhausts the set — no further cursor.
        var page2 = await _store.ListAsync(new AuditFilter { PageSize = 5, Cursor = page1.NextCursor });
        page2.Items.Should().HaveCount(2);
        page2.NextCursor.Should().BeNull("the second page consumes the last of the 7 entries");
    }

    public void Dispose() => _db.Dispose();

    private sealed class TestMetadataDbContext(DbContextOptions<TestMetadataDbContext> options)
        : DbContext(options), IVisuAuthMetadataDbContext
    {
        public DbSet<VisuAuthTenant> VisuAuthTenants => Set<VisuAuthTenant>();
        public DbSet<VisuAuth.Identity.MultiTenancy.VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs
            => Set<VisuAuth.Identity.MultiTenancy.VisuAuthExternalProviderConfig>();
        public DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog => Set<VisuAuthAuditLogEntry>();
        public DbSet<VisuAuth.Identity.MultiTenancy.VisuAuthAdapterConfig> VisuAuthAdapterConfigs
            => Set<VisuAuth.Identity.MultiTenancy.VisuAuthAdapterConfig>();
    }

    private sealed class StubHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public bool IsMultiTenancyEnabled { get; private set; } = true;
        public string? CurrentTenantId { get; private set; }
        public string? CurrentTenantDisplayName { get; private set; }
        public void SetTenant(string id, string? displayName = null)
        {
            CurrentTenantId = id;
            CurrentTenantDisplayName = displayName;
        }
    }
}
