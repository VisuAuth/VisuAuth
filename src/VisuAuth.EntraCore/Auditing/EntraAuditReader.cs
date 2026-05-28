using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Common;
using VisuAuth.EntraCore.Internal;

namespace VisuAuth.EntraCore.Auditing;

/// <summary>
/// <see cref="IAuditReader"/> backed by Microsoft Entra's audit logs.
/// Merges two Graph sources onto the VisuAuth admin audit-log page:
/// <c>/auditLogs/signIns</c> (who logged in / failed — also feeds the
/// dashboard "logins per day" chart) and <c>/auditLogs/directoryAudits</c>
/// (the directory-change trail: user created / updated / deleted, role
/// assignments — including VisuAuth's own admin operations as Graph logs
/// them). Shared across both adapter families; opt in via
/// <c>AddVisuAuthEntraAuditLog()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Action-aware source routing.</b> Sign-ins map onto the canonical
/// <see cref="AuditActions.LoginSucceeded"/> / <see cref="AuditActions.LoginFailed"/>
/// codes; directory audits surface their raw Entra <c>activityDisplayName</c>.
/// So when the admin filters by a specific action, only the matching
/// source is queried: a login code → sign-ins only; any other action →
/// directory only; no action → both, merged by timestamp DESC.
/// </para>
/// <para>
/// <b>Page-1 semantics.</b> Graph paginates with skip tokens, not numeric
/// pages (same limitation as the Entra user list). <see cref="ListAsync"/>
/// fetches up to <see cref="AuditFilter.PageSize"/> from each queried
/// source, merges, sorts newest-first, and returns the top page. Actor
/// search is pushed to Graph for sign-ins (top-level
/// <c>userPrincipalName</c>) and applied client-side for directory audits
/// (whose initiator UPN is a nested path Graph won't reliably filter).
/// </para>
/// <para>
/// <b>Requires <c>AuditLog.Read.All</c> + an Entra ID P1 licence.</b> On a
/// 403 (missing permission / licence) the reader logs and degrades to an
/// empty view rather than surfacing a 500.
/// </para>
/// </remarks>
public sealed class EntraAuditReader(
    GraphServiceClient graphClient,
    ILogger<EntraAuditReader> logger) : IAuditReader
{
    /// <summary>Page cap mirrors the EF audit store's upper bound.</summary>
    private const int MaxPageSize = 200;

    /// <summary>
    /// Safety cap on next-link follows in <see cref="CountByDayAsync"/> —
    /// 20 pages × 1000 = 20k sign-ins. Beyond that the chart undercounts;
    /// acceptable for a dashboard window (typically 7 days).
    /// </summary>
    private const int MaxCountPages = 20;

    /// <summary>How many directory audits to scan when listing distinct activity names.</summary>
    private const int DistinctActionScanSize = 200;

    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly ILogger<EntraAuditReader> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<PagedResult<AuditEntryView>> ListAsync(AuditFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var actionIsLogin = IsLoginAction(filter.Action);
        var actionIsSet = !string.IsNullOrWhiteSpace(filter.Action);

        var merged = new List<AuditEntryView>(pageSize * 2);

        // Sign-ins: when no action filter, or the filter IS a login code.
        if (!actionIsSet || actionIsLogin)
        {
            merged.AddRange(await ListSignInsAsync(filter, pageSize, cancellationToken));
        }

        // Directory audits: when no action filter, or the filter is some
        // non-login action (a raw Entra activity name).
        if (!actionIsSet || !actionIsLogin)
        {
            merged.AddRange(await ListDirectoryAuditsAsync(filter, pageSize, cancellationToken));
        }

        var page = merged
            .OrderByDescending(e => e.Timestamp)
            .Take(pageSize)
            .ToList();

        return new PagedResult<AuditEntryView>
        {
            Items = page,
            Total = page.Count,
            Page = 1,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListDistinctActionsAsync(CancellationToken cancellationToken = default)
    {
        // Always offer the two login codes (sign-ins). Augment with the
        // distinct directory activity names from a bounded recent scan so
        // the dropdown reflects what the tenant actually does, without an
        // unbounded enumeration.
        var actions = new SortedSet<string>(StringComparer.Ordinal)
        {
            AuditActions.LoginSucceeded,
            AuditActions.LoginFailed,
        };

        try
        {
            var response = await _graph.AuditLogs.DirectoryAudits.GetAsync(rc =>
            {
                rc.QueryParameters.Top = DistinctActionScanSize;
                rc.QueryParameters.Select = ["activityDisplayName"];
            }, cancellationToken);

            foreach (var audit in response?.Value ?? [])
            {
                if (!string.IsNullOrEmpty(audit.ActivityDisplayName))
                {
                    actions.Add(audit.ActivityDisplayName);
                }
            }
        }
        catch (ODataError ex)
        {
            // Degrade to the login codes only — the dropdown still works.
            _logger.EntraAuditQueryFailed(ex, ex.Error?.Message);
        }

        return actions.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyActionCount>> CountByDayAsync(
        string action,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        // Only the login codes are charted (the dashboard's logins-per-day);
        // they're backed by sign-ins. Anything else has no day-rollup here.
        var graphFilter = EntraSignInAuditMapper.BuildCountFilter(action, fromInclusive, toInclusive);
        if (graphFilter is null)
        {
            return [];
        }

        try
        {
            var counts = new Dictionary<DateOnly, int>();
            var response = await _graph.AuditLogs.SignIns.GetAsync(rc =>
            {
                rc.QueryParameters.Top = EntraSignInAuditMapper.MaxGraphPageSize;
                rc.QueryParameters.Select = ["createdDateTime"];
                rc.QueryParameters.Filter = graphFilter;
            }, cancellationToken);

            var pages = 0;
            while (response is not null)
            {
                foreach (var signIn in response.Value ?? [])
                {
                    if (signIn.CreatedDateTime is { } dt)
                    {
                        var day = DateOnly.FromDateTime(dt.UtcDateTime);
                        counts[day] = counts.GetValueOrDefault(day) + 1;
                    }
                }

                if (string.IsNullOrEmpty(response.OdataNextLink) || ++pages >= MaxCountPages)
                {
                    break;
                }
                response = await _graph.AuditLogs.SignIns
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }

            return counts
                .OrderBy(kv => kv.Key)
                .Select(kv => new DailyActionCount(kv.Key, kv.Value))
                .ToList();
        }
        catch (ODataError ex)
        {
            _logger.EntraAuditQueryFailed(ex, ex.Error?.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<AuditEntryView>> ListSignInsAsync(
        AuditFilter filter, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _graph.AuditLogs.SignIns.GetAsync(rc =>
            {
                rc.QueryParameters.Top = pageSize;
                var graphFilter = EntraSignInAuditMapper.BuildListFilter(filter);
                if (graphFilter is not null)
                {
                    rc.QueryParameters.Filter = graphFilter;
                }
            }, cancellationToken);

            return (response?.Value ?? [])
                .Select(EntraSignInAuditMapper.ToEntryView)
                .ToList();
        }
        catch (ODataError ex)
        {
            _logger.EntraAuditQueryFailed(ex, ex.Error?.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<AuditEntryView>> ListDirectoryAuditsAsync(
        AuditFilter filter, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _graph.AuditLogs.DirectoryAudits.GetAsync(rc =>
            {
                rc.QueryParameters.Top = pageSize;
                var graphFilter = EntraDirectoryAuditMapper.BuildListFilter(filter);
                if (graphFilter is not null)
                {
                    rc.QueryParameters.Filter = graphFilter;
                }
            }, cancellationToken);

            var entries = (response?.Value ?? [])
                .Select(EntraDirectoryAuditMapper.ToEntryView);

            // Actor search is client-side for directory audits (Graph won't
            // reliably filter the nested initiatedBy/user path).
            if (!string.IsNullOrWhiteSpace(filter.ActorSearch))
            {
                var needle = filter.ActorSearch.Trim();
                entries = entries.Where(e =>
                    e.ActorEmail is not null
                    && e.ActorEmail.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            return entries.ToList();
        }
        catch (ODataError ex)
        {
            _logger.EntraAuditQueryFailed(ex, ex.Error?.Message);
            return [];
        }
    }

    private static bool IsLoginAction(string? action)
        => action is AuditActions.LoginSucceeded or AuditActions.LoginFailed;
}
