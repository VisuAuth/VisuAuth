using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.AdminUi.Pages.Admin.AuditLog;

/// <summary>
/// <c>/visuauth/admin/audit-log</c> — the read surface for the audit
/// plugin. Capability-gates on the optional <see cref="IAuditReader"/>:
/// when the host hasn't called <c>AddVisuAuthAuditLog</c>, the reader
/// isn't registered and the page renders an empty state explaining how to
/// turn the feature on.
/// </summary>
/// <remarks>
/// Filters are GET-bound so the URL is shareable (paste the URL with
/// <c>?action=UserLocked&amp;targetId=abc123</c> and the admin lands on
/// the same filtered view). Pagination follows the same pattern as the
/// Users page.
/// </remarks>
public sealed class IndexModel(IAuditReader? auditReader = null) : PageModel
{
    /// <summary>Cap matches <see cref="EfCoreAuditStore"/>'s upper bound.</summary>
    private const int DefaultPageSize = 50;

    private readonly IAuditReader? _auditReader = auditReader;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? ActorSearch { get; set; }

    [BindProperty(SupportsGet = true, Name = "action")]
    public string? ActionFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "targetId")]
    public string? TargetIdFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "outcome")]
    public string? OutcomeFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public DateTime? To { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public static int PageSize => DefaultPageSize;

    public PagedResult<AuditEntryView> Result { get; private set; } = PagedResult<AuditEntryView>.Empty(pageSize: DefaultPageSize);

    /// <summary>Distinct action codes pulled from the store — feeds the filter dropdown.</summary>
    public IReadOnlyList<string> AvailableActions { get; private set; } = [];

    /// <summary>True only when the consumer wired the audit plugin via <c>AddVisuAuthAuditLog</c>.</summary>
    public bool IsAuditEnabled => _auditReader is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (_auditReader is null)
        {
            // Plugin not opted-in — the page still renders so the admin
            // sees the explainer (i18n key ExternalProviders.AuditLog.NotEnabled).
            return Page();
        }

        var outcome = OutcomeFilter switch
        {
            "success" => (AuditOutcome?)AuditOutcome.Success,
            "failure" => (AuditOutcome?)AuditOutcome.Failure,
            _ => null,
        };

        var filter = new AuditFilter
        {
            ActorSearch = string.IsNullOrWhiteSpace(ActorSearch) ? null : ActorSearch.Trim(),
            Action = string.IsNullOrWhiteSpace(ActionFilter) ? null : ActionFilter.Trim(),
            TargetId = string.IsNullOrWhiteSpace(TargetIdFilter) ? null : TargetIdFilter.Trim(),
            // DateTime → DateTimeOffset assumes UTC because the admin's
            // <input type="date"> doesn't include a timezone. Matches how
            // we store Timestamp via TimeProvider.GetUtcNow().
            From = From is { } from ? new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)) : null,
            To = To is { } to ? new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Utc).AddDays(1).AddTicks(-1)) : null,
            Outcome = outcome,
            Page = PageNumber < 1 ? 1 : PageNumber,
            PageSize = DefaultPageSize,
        };

        Result = await _auditReader.ListAsync(filter, cancellationToken);
        AvailableActions = await _auditReader.ListDistinctActionsAsync(cancellationToken);

        return Page();
    }
}
