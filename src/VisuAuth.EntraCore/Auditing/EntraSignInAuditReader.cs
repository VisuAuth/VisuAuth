using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Common;
using VisuAuth.EntraCore.Internal;

namespace VisuAuth.EntraCore.Auditing;

/// <summary>
/// <see cref="IAuditReader"/> backed by Microsoft Entra's
/// <c>/auditLogs/signIns</c>. Surfaces the directory's sign-in events on
/// the VisuAuth admin audit-log page (and feeds the dashboard "logins per
/// day" chart) when an Entra / Entra External deployment opts in via
/// <c>AddVisuAuthEntraSignInAuditLog()</c>. Shared across both adapter
/// families — the sign-in Graph surface is identical.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sign-ins only.</b> Entra also exposes <c>/auditLogs/directoryAudits</c>
/// (the directory-change trail). This reader surfaces sign-ins because
/// they map cleanly onto one <see cref="AuditEntryView"/> per event and
/// are the most security-relevant signal; directory audits have a
/// different shape (collection target resources, user-or-app initiators)
/// and are a documented follow-up.
/// </para>
/// <para>
/// <b>Requires <c>AuditLog.Read.All</c> + an Entra ID P1 licence.</b> When
/// the app lacks the permission or the tenant lacks the licence, Graph
/// returns 403; the reader logs and degrades to an empty view rather than
/// surfacing a 500 — the admin page then renders its empty state.
/// </para>
/// <para>
/// <b>Pagination.</b> Like the Entra user list, <see cref="ListAsync"/>
/// honours <see cref="AuditFilter.PageSize"/> and treats the result as
/// page 1 (Graph paginates with <c>@odata.nextLink</c> skip tokens, not
/// numeric pages). <see cref="CountByDayAsync"/> DOES follow the next-link
/// (bounded) so the chart's day buckets are accurate across a range.
/// </para>
/// </remarks>
public sealed class EntraSignInAuditReader(
    GraphServiceClient graphClient,
    ILogger<EntraSignInAuditReader> logger) : IAuditReader
{
    /// <summary>Page cap mirrors the EF audit store's upper bound.</summary>
    private const int MaxPageSize = 200;

    /// <summary>
    /// Safety cap on next-link follows in <see cref="CountByDayAsync"/> —
    /// 20 pages × 1000 = 20k sign-ins. Beyond that the chart undercounts;
    /// acceptable for a dashboard window (typically 7 days).
    /// </summary>
    private const int MaxCountPages = 20;

    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly ILogger<EntraSignInAuditReader> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<PagedResult<AuditEntryView>> ListAsync(AuditFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        try
        {
            var response = await _graph.AuditLogs.SignIns.GetAsync(rc =>
            {
                rc.QueryParameters.Top = pageSize;
                // signIns are returned newest-first by default, so no
                // explicit $orderby (which can clash with some $filter
                // combinations).
                var graphFilter = EntraSignInAuditMapper.BuildListFilter(filter);
                if (graphFilter is not null)
                {
                    rc.QueryParameters.Filter = graphFilter;
                }
            }, cancellationToken);

            var items = (response?.Value ?? [])
                .Select(EntraSignInAuditMapper.ToEntryView)
                .ToList();

            return new PagedResult<AuditEntryView>
            {
                Items = items,
                Total = items.Count,
                Page = 1,
                PageSize = pageSize,
            };
        }
        catch (ODataError ex)
        {
            _logger.EntraAuditQueryFailed(ex, ex.Error?.Message);
            return PagedResult<AuditEntryView>.Empty(1, pageSize);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// This reader only emits the two canonical login codes, so the filter
    /// dropdown is a fixed pair — no Graph round-trip needed.
    /// </remarks>
    public Task<IReadOnlyList<string>> ListDistinctActionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> actions = [AuditActions.LoginSucceeded, AuditActions.LoginFailed];
        return Task.FromResult(actions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyActionCount>> CountByDayAsync(
        string action,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var graphFilter = EntraSignInAuditMapper.BuildCountFilter(action, fromInclusive, toInclusive);
        if (graphFilter is null)
        {
            // Not a sign-in-backed action (only the login codes are) —
            // nothing to count.
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
}
