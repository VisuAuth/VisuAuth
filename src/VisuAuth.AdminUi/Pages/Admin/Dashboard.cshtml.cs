using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin;

/// <summary>
/// Backing model for <c>/visuauth/admin</c> — the landing dashboard.
/// Renders KPI cards, a 7-day login chart, recent activity, and system
/// health. Drives every count off <see cref="IUserStore.ListAsync"/>
/// with <c>PageSize=1</c> so it works against any adapter without
/// requiring a new <c>CountAsync</c> on the public surface (see
/// CLAUDE.md §6 — the abstraction stays minimal).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAuditReader"/> is optional in DI — the audit plugin is
/// opt-in (CLAUDE.md §2.5). When absent the dashboard renders an
/// "audit plugin not enabled" hint card in place of the recent-activity
/// and login-chart sections. Same pattern as <c>AuditLog/Index</c>.
/// </para>
/// <para>
/// Counts that depend on capability flags
/// (<see cref="UserBackendCapabilities.SupportsTwoFactor"/>,
/// <see cref="UserBackendCapabilities.SupportsEmailConfirmation"/>,
/// <see cref="UserBackendCapabilities.SupportsLockout"/>) are skipped
/// when the adapter reports they're off, so a future Entra adapter
/// won't render meaningless "0 locked" tiles.
/// </para>
/// </remarks>
public sealed class DashboardModel(
    IUserStore userStore,
    IRoleStore roleStore,
    ITenantContext tenantContext,
    IServiceProvider services) : PageModel
{
    /// <summary>How many recent audit events the activity feed pulls.</summary>
    public const int RecentActivityCount = 10;

    /// <summary>How many days the login chart spans (today + 6 prior).</summary>
    public const int LoginChartDays = 7;

    private readonly IUserStore _users = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roles = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
    private readonly ITenantContext _tenants = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public int TotalUsers { get; private set; }
    public int? LockedUsers { get; private set; }
    public int? PendingConfirmUsers { get; private set; }
    public int? TwoFactorUsers { get; private set; }
    public int TotalRoles { get; private set; }
    public int? TotalTenants { get; private set; }

    /// <summary>Most recent audit events, newest first. Empty when the plugin is off.</summary>
    public IReadOnlyList<AuditEntryView> RecentActivity { get; private set; } = [];

    /// <summary>
    /// Dense 7-day series. Every day in the window is present (zero-fill
    /// for days without successful logins) so the bar chart always renders
    /// <see cref="LoginChartDays"/> bars regardless of activity.
    /// </summary>
    public IReadOnlyList<DailyActionCount> LoginsPerDay { get; private set; } = [];

    /// <summary>Peak bar value — used to scale heights in the cshtml.</summary>
    public int LoginChartMax { get; private set; }

    /// <summary>True when <see cref="IAuditReader"/> is registered in DI.</summary>
    public bool AuditPluginEnabled { get; private set; }

    /// <summary>
    /// Read-only system snapshot for the "System health" card. Resolved at
    /// request time so the assembly version reflects the actually-loaded
    /// VisuAuth, not whatever was baked at compile time of the consumer.
    /// </summary>
    public SystemHealthInfo Health { get; private set; } = SystemHealthInfo.Empty;

    public bool MultiTenancyEnabled => _tenants.IsMultiTenancyEnabled;

    public UserBackendCapabilities Capabilities => _users.Capabilities;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Counts: one ListAsync per KPI with PageSize=1. Reuses the
        // store's existing query path so multi-tenant filtering applies
        // automatically — no new abstraction to teach every adapter.
        TotalUsers = await CountAsync(new UserFilter { PageSize = 1 }, cancellationToken);

        if (Capabilities.SupportsLockout)
        {
            LockedUsers = await CountAsync(
                new UserFilter { IsLockedOut = true, PageSize = 1 },
                cancellationToken);
        }
        if (Capabilities.SupportsEmailConfirmation)
        {
            PendingConfirmUsers = await CountAsync(
                new UserFilter { EmailConfirmed = false, PageSize = 1 },
                cancellationToken);
        }
        if (Capabilities.SupportsTwoFactor)
        {
            TwoFactorUsers = await CountAsync(
                new UserFilter { TwoFactorEnabled = true, PageSize = 1 },
                cancellationToken);
        }

        var roleTenant = _tenants.IsMultiTenancyEnabled ? _tenants.CurrentTenantId : null;
        TotalRoles = (await _roles.ListAsync(roleTenant, cancellationToken)).Count;

        if (_tenants.IsMultiTenancyEnabled
            && _services.GetService(typeof(ITenantStore)) is ITenantStore tenantStore)
        {
            TotalTenants = (await tenantStore.ListAsync(cancellationToken)).Count;
        }

        await LoadAuditSectionsAsync(cancellationToken);
        Health = SystemHealthInfo.Capture();
    }

    private async Task<int> CountAsync(UserFilter filter, CancellationToken cancellationToken)
    {
        var page = await _users.ListAsync(filter, cancellationToken);
        // EF stores supply a real total; cursor-only backends (Graph) leave it
        // null, so fall back to the (tiny, PageSize=1) page count — the same
        // best-effort number those backends surfaced before.
        return page.TotalCount ?? page.Items.Count;
    }

    private async Task LoadAuditSectionsAsync(CancellationToken cancellationToken)
    {
        if (_services.GetService(typeof(IAuditReader)) is not IAuditReader reader)
        {
            AuditPluginEnabled = false;
            LoginsPerDay = BuildEmptySeries();
            LoginChartMax = 0;
            return;
        }

        AuditPluginEnabled = true;

        var recent = await reader.ListAsync(
            new AuditFilter { PageSize = RecentActivityCount },
            cancellationToken);
        RecentActivity = recent.Items;

        // 7-day window: from = today-(N-1) at 00:00 UTC, to = now.
        // The reader filters Timestamp >= from && <= to inclusively.
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var from = new DateTimeOffset(today.AddDays(-(LoginChartDays - 1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var raw = await reader.CountByDayAsync(
            AuditActions.LoginSucceeded,
            from,
            nowUtc,
            cancellationToken);

        LoginsPerDay = DenseSeries(raw, today, LoginChartDays);
        LoginChartMax = LoginsPerDay.Count == 0 ? 0 : LoginsPerDay.Max(d => d.Count);
    }

    /// <summary>
    /// Fills missing days with zero counts so the bar chart renders a
    /// continuous N-bar strip. The reader only returns days that had
    /// activity (CLAUDE.md says the caller fills the gap) — this is that.
    /// </summary>
    private static List<DailyActionCount> DenseSeries(
        IReadOnlyList<DailyActionCount> sparse,
        DateOnly today,
        int dayCount)
    {
        var byDay = sparse.ToDictionary(d => d.Day, d => d.Count);
        var result = new List<DailyActionCount>(dayCount);
        for (var i = dayCount - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            result.Add(new DailyActionCount(day, byDay.TryGetValue(day, out var c) ? c : 0));
        }
        return result;
    }

    /// <summary>
    /// Zero-fill series shown when the audit plugin is off — keeps the
    /// chart's vertical rhythm so swapping the plugin on later doesn't
    /// shift the surrounding layout.
    /// </summary>
    private static List<DailyActionCount> BuildEmptySeries()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var result = new List<DailyActionCount>(LoginChartDays);
        for (var i = LoginChartDays - 1; i >= 0; i--)
        {
            result.Add(new DailyActionCount(today.AddDays(-i), 0));
        }
        return result;
    }

    /// <summary>
    /// Snapshot rendered by the "System health" card. Captured at request
    /// time so version info reflects the running assembly, not a value
    /// frozen at compile time.
    /// </summary>
    public sealed record SystemHealthInfo(
        string VisuAuthVersion,
        string RuntimeVersion,
        string FrameworkDescription)
    {
        public static SystemHealthInfo Empty { get; } = new("", "", "");

        public static SystemHealthInfo Capture()
        {
            // InformationalVersion carries the full semver (incl. -alpha.N
            // pre-release suffix from CI); AssemblyVersion only carries
            // major.minor.build. Prefer the informational one.
            var asm = typeof(DashboardModel).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = info ?? asm.GetName().Version?.ToString() ?? "unknown";

            return new SystemHealthInfo(
                version,
                Environment.Version.ToString(),
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        }
    }
}
