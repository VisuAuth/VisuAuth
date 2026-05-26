using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;
using VisuAuth.AdminUi.Pages.Admin;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Direct unit coverage for <see cref="DashboardModel"/> behaviour the
/// integration suite can't exercise without spinning up alternative
/// adapters: the audit-plugin-off branch (zero-filled chart, "not
/// enabled" hint card), the capability-gated KPI skips (a backend with
/// <c>SupportsLockout = false</c> emits a null LockedUsers tile), and
/// the dense-series construction for the chart.
/// </summary>
public sealed class DashboardModelTests
{
    [Fact]
    public async Task OnGet_WhenAuditPluginNotRegistered_DisablesAuditFlagAndZeroFillsChart()
    {
        var services = BuildServices(out var users, out var roles, out var tenants);

        var page = new DashboardModel(users.Object, roles.Object, tenants.Object, services);
        await page.OnGetAsync(CancellationToken.None);

        page.AuditPluginEnabled.Should().BeFalse(
            "IAuditReader is absent from the service provider for this test");
        page.RecentActivity.Should().BeEmpty();
        page.LoginsPerDay.Should().HaveCount(DashboardModel.LoginChartDays,
            "the cshtml relies on a stable bar count even when the plugin is off");
        page.LoginsPerDay.Should().OnlyContain(d => d.Count == 0,
            "zero-fill prevents the chart from collapsing while the plugin is being wired");
        page.LoginChartMax.Should().Be(0);
    }

    [Fact]
    public async Task OnGet_WhenCapabilitiesOff_LeavesGatedKpisNull()
    {
        // Backend with everything off — mirrors what a Microsoft Entra
        // adapter would declare for the v0.2 milestone.
        var caps = new UserBackendCapabilities
        {
            SupportsLockout = false,
            SupportsEmailConfirmation = false,
            SupportsTwoFactor = false,
        };
        var services = BuildServices(out var users, out var roles, out var tenants, caps);

        // Only the unconditional TotalUsers tile should poke the store.
        users.Setup(s => s.ListAsync(
                It.Is<UserFilter>(f => f.IsLockedOut == null && f.EmailConfirmed == null && f.TwoFactorEnabled == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyUserPage(7));

        var page = new DashboardModel(users.Object, roles.Object, tenants.Object, services);
        await page.OnGetAsync(CancellationToken.None);

        page.TotalUsers.Should().Be(7);
        page.LockedUsers.Should().BeNull();
        page.PendingConfirmUsers.Should().BeNull();
        page.TwoFactorUsers.Should().BeNull();

        users.Verify(s => s.ListAsync(
            It.Is<UserFilter>(f => f.IsLockedOut == true),
            It.IsAny<CancellationToken>()),
            Times.Never, "capability off → skip the locked-users count entirely");
    }

    [Fact]
    public async Task OnGet_WhenAuditPluginOn_PopulatesRecentActivityAndDenseChart()
    {
        var caps = new UserBackendCapabilities();
        var (services, audit) = BuildServicesWithAudit(out var users, out var roles, out var tenants, caps);

        var fixedNow = DateTimeOffset.UtcNow;
        // Two days inside the window, one day OUTSIDE — the dense series
        // builder must zero-fill the gap and drop the out-of-range entry
        // (the reader stub already filters by range, this asserts we
        // don't accidentally let an outlier through).
        audit.Setup(a => a.CountByDayAsync(
                AuditActions.LoginSucceeded,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DailyActionCount(DateOnly.FromDateTime(fixedNow.UtcDateTime.AddDays(-1)), 3),
                new DailyActionCount(DateOnly.FromDateTime(fixedNow.UtcDateTime), 5),
            });
        audit.Setup(a => a.ListAsync(It.IsAny<AuditFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<AuditEntryView>
            {
                Items = new[]
                {
                    new AuditEntryView
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = fixedNow,
                        Action = AuditActions.LoginSucceeded,
                        TargetType = AuditTargetTypes.User,
                        TargetLabel = "alice@example.com",
                    },
                },
                Total = 1,
                Page = 1,
                PageSize = DashboardModel.RecentActivityCount,
            });

        var page = new DashboardModel(users.Object, roles.Object, tenants.Object, services);
        await page.OnGetAsync(CancellationToken.None);

        page.AuditPluginEnabled.Should().BeTrue();
        page.RecentActivity.Should().ContainSingle()
            .Which.TargetLabel.Should().Be("alice@example.com");
        page.LoginsPerDay.Should().HaveCount(DashboardModel.LoginChartDays);
        page.LoginChartMax.Should().Be(5, "the densest day was the today bucket");
        page.LoginsPerDay[^1].Count.Should().Be(5, "today is the last bucket in the series");
    }

    [Fact]
    public async Task OnGet_WhenMultiTenancyOff_LeavesTotalTenantsNull()
    {
        var services = BuildServices(out var users, out var roles, out var tenants);
        tenants.SetupGet(t => t.IsMultiTenancyEnabled).Returns(false);

        var page = new DashboardModel(users.Object, roles.Object, tenants.Object, services);
        await page.OnGetAsync(CancellationToken.None);

        page.TotalTenants.Should().BeNull(
            "tenants tile must hide in single-tenant deployments");
    }

#pragma warning disable CA1859 // The dashboard receives IServiceProvider, not ServiceProvider — keep the test's helper signature aligned.
    private static IServiceProvider BuildServices(
        out Mock<IUserStore> users,
        out Mock<IRoleStore> roles,
        out Mock<ITenantContext> tenants,
        UserBackendCapabilities? caps = null)
    {
        users = new Mock<IUserStore>();
        users.SetupGet(s => s.Capabilities).Returns(caps ?? new UserBackendCapabilities
        {
            SupportsLockout = true,
            SupportsEmailConfirmation = true,
            SupportsTwoFactor = true,
        });
        users.Setup(s => s.ListAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyUserPage(0));

        roles = new Mock<IRoleStore>();
        roles.Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleSummary>());

        tenants = new Mock<ITenantContext>();
        tenants.SetupGet(t => t.IsMultiTenancyEnabled).Returns(false);

        // ServiceProvider is just for the optional IAuditReader / ITenantStore
        // lookups — the rest of the dashboard collaborators come in via
        // the page-model constructor.
        return new ServiceCollection().BuildServiceProvider();
    }

    private static (IServiceProvider services, Mock<IAuditReader> audit) BuildServicesWithAudit(
        out Mock<IUserStore> users,
        out Mock<IRoleStore> roles,
        out Mock<ITenantContext> tenants,
        UserBackendCapabilities? caps = null)
    {
        users = new Mock<IUserStore>();
        users.SetupGet(s => s.Capabilities).Returns(caps ?? new UserBackendCapabilities
        {
            SupportsLockout = true,
            SupportsEmailConfirmation = true,
            SupportsTwoFactor = true,
        });
        users.Setup(s => s.ListAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyUserPage(0));

        roles = new Mock<IRoleStore>();
        roles.Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleSummary>());

        tenants = new Mock<ITenantContext>();
        tenants.SetupGet(t => t.IsMultiTenancyEnabled).Returns(false);

        var audit = new Mock<IAuditReader>();
        var sc = new ServiceCollection();
        sc.AddSingleton(audit.Object);
        return (sc.BuildServiceProvider(), audit);
    }

    private static PagedResult<UserSummary> EmptyUserPage(int total) => new()
    {
        Items = Array.Empty<UserSummary>(),
        Total = total,
        Page = 1,
        PageSize = 1,
    };
#pragma warning restore CA1859
}
