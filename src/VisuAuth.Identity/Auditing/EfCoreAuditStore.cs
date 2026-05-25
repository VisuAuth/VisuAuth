using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Auditing;

/// <summary>
/// EF Core implementation of <see cref="IAuditWriter"/> + <see cref="IAuditReader"/>.
/// Stores rows in <see cref="VisuAuthAuditLogEntry"/> through the consumer's
/// metadata DbContext, with ambient state (actor, tenant, IP, user agent,
/// timestamp) enriched from <see cref="IHttpContextAccessor"/> and friends.
/// </summary>
/// <remarks>
/// Writes NEVER throw to the caller — the writer logs and swallows storage
/// failures because dropping an audit entry is acceptable; bringing down
/// the primary action (lock user, save provider, etc.) because the audit
/// table is unavailable is not.
/// </remarks>
public sealed class EfCoreAuditStore(
    IVisuAuthMetadataDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    ILogger<EfCoreAuditStore> logger) : IAuditWriter, IAuditReader
{
    private const int MaxPageSize = 200;

    /// <summary>Cap the User-Agent snapshot — some bots send absurdly long ones.</summary>
    private const int MaxUserAgentLength = 512;

    private readonly IVisuAuthMetadataDbContext _db =
        db ?? throw new ArgumentNullException(nameof(db));
    private readonly IHttpContextAccessor _httpContextAccessor =
        httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    private readonly ITenantContext _tenantContext =
        tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<EfCoreAuditStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        try
        {
            var http = _httpContextAccessor.HttpContext;
            var user = http?.User;

            var entry = new VisuAuthAuditLogEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = _timeProvider.GetUtcNow(),
                Action = auditEvent.Action,
                TargetType = auditEvent.TargetType,
                TargetId = auditEvent.TargetId,
                TargetLabel = auditEvent.TargetLabel,
                Outcome = auditEvent.Outcome,
                FailureReason = auditEvent.FailureReason,
                ActorUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
                ActorEmail = user?.FindFirstValue(ClaimTypes.Email)
                             ?? user?.FindFirstValue(ClaimTypes.Name),
                ActorIpAddress = ResolveIpAddress(http),
                ActorUserAgent = TruncateUserAgent(http?.Request?.Headers?.UserAgent.ToString()),
                TenantId = _tenantContext.IsMultiTenancyEnabled
                    ? _tenantContext.CurrentTenantId
                    : null,
                PayloadJson = SerialisePayload(auditEvent.Payload),
            };

            _db.VisuAuthAuditLog.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Auditing must never break the primary action — log + swallow.
        catch (Exception ex)
        {
            _logger.AuditPersistFailed(ex, auditEvent.Action, auditEvent.TargetType, auditEvent.TargetId);
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditEntryView>> ListAsync(
        AuditFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        // Start from the IQueryable so EF can push every predicate to SQL.
        // The query filter on TenantId is applied implicitly by the
        // DbContext when multi-tenancy is on (see ITenantContext usage).
        var query = _db.VisuAuthAuditLog.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.ActorSearch))
        {
            var needle = filter.ActorSearch.Trim();
            query = query.Where(e =>
                (e.ActorEmail != null && EF.Functions.Like(e.ActorEmail, $"%{needle}%"))
                || (e.ActorUserId != null && EF.Functions.Like(e.ActorUserId, $"%{needle}%")));
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(e => e.Action == filter.Action);
        }
        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        {
            query = query.Where(e => e.TargetId == filter.TargetId);
        }
        if (filter.From is { } from)
        {
            query = query.Where(e => e.Timestamp >= from);
        }
        if (filter.To is { } to)
        {
            query = query.Where(e => e.Timestamp <= to);
        }
        if (filter.Outcome is { } outcome)
        {
            query = query.Where(e => e.Outcome == outcome);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => ToView(e))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryView>
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDistinctActionsAsync(CancellationToken cancellationToken = default)
        => _db.VisuAuthAuditLog
            .AsNoTracking()
            .Select(e => e.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken)
            .ContinueWith(t => (IReadOnlyList<string>)t.Result, cancellationToken,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyActionCount>> CountByDayAsync(
        string action,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        // Pull only the Timestamp column for matching rows. Grouping by
        // .Date inside an IQueryable would translate fine on SQL Server
        // but mis-translates under SQLite (datetime → text), so we let
        // the DB do the filter and group in memory — at dashboard cardinalities
        // (single action, narrow window) the row count is bounded enough
        // for this to be fine.
        var timestamps = await _db.VisuAuthAuditLog
            .AsNoTracking()
            .Where(e => e.Action == action
                && e.Timestamp >= fromInclusive
                && e.Timestamp <= toInclusive)
            .Select(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        return timestamps
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .Select(g => new DailyActionCount(g.Key, g.Count()))
            .OrderBy(d => d.Day)
            .ToList();
    }

    /// <summary>
    /// Best-effort IP resolution: prefer the first X-Forwarded-For entry
    /// when present (proxies / load balancers), else the connection's
    /// RemoteIpAddress. Returns null when neither yields anything useful.
    /// </summary>
    private static string? ResolveIpAddress(HttpContext? http)
    {
        if (http is null)
        {
            return null;
        }
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // First entry in the comma-separated list is the original client.
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }
        return http.Connection.RemoteIpAddress?.ToString();
    }

    private static string? TruncateUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return null;
        }
        return userAgent.Length > MaxUserAgentLength
            ? userAgent[..MaxUserAgentLength]
            : userAgent;
    }

    private static string? SerialisePayload(IReadOnlyDictionary<string, string?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return null;
        }
        // JsonSerializer with the default options gives a compact, stable
        // representation — the admin UI just renders it as a `<pre>` block.
        return JsonSerializer.Serialize(payload);
    }

    private static AuditEntryView ToView(VisuAuthAuditLogEntry entry) => new()
    {
        Id = entry.Id,
        Timestamp = entry.Timestamp,
        Action = entry.Action,
        TargetType = entry.TargetType,
        TargetId = entry.TargetId,
        TargetLabel = entry.TargetLabel,
        Outcome = entry.Outcome,
        FailureReason = entry.FailureReason,
        ActorUserId = entry.ActorUserId,
        ActorEmail = entry.ActorEmail,
        ActorIpAddress = entry.ActorIpAddress,
        ActorUserAgent = entry.ActorUserAgent,
        TenantId = entry.TenantId,
        PayloadJson = entry.PayloadJson,
    };
}
