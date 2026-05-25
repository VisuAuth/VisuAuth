using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Auditing;

/// <summary>
/// Read-side of the audit log used by the admin page. Present in DI only
/// when the audit plugin is wired (the no-op writer path doesn't register
/// a reader because there's nothing to read). Consumers can resolve this
/// to surface audit events in their own reporting flows.
/// </summary>
public interface IAuditReader
{
    /// <summary>
    /// Returns a page of entries matching the filter, ordered by
    /// timestamp DESC (most recent first).
    /// </summary>
    Task<PagedResult<AuditEntryView>> ListAsync(AuditFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct action codes that have ever been recorded, so
    /// the admin page can populate the filter dropdown without hardcoding
    /// the list. Cheap because the index on Action makes this a covering
    /// scan; cached at the page model level if needed.
    /// </summary>
    Task<IReadOnlyList<string>> ListDistinctActionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter shape for <see cref="IAuditReader.ListAsync"/>. All fields are
/// optional and AND-combined; an empty filter returns every entry the
/// caller's tenant scope can see (the store layer applies the tenant
/// filter automatically through the EF Core global filter).
/// </summary>
public sealed record AuditFilter
{
    /// <summary>Substring match against ActorEmail (case-insensitive). Null = no actor filter.</summary>
    public string? ActorSearch { get; init; }

    /// <summary>Exact match against <see cref="AuditEvent.Action"/>. Null = any action.</summary>
    public string? Action { get; init; }

    /// <summary>Exact match against <see cref="AuditEvent.TargetId"/>. Null = any target.</summary>
    public string? TargetId { get; init; }

    /// <summary>Inclusive lower bound. Null = no lower bound.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound. Null = no upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Filter by outcome (success / failure). Null = both.</summary>
    public AuditOutcome? Outcome { get; init; }

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page. Defaults to 50; capped by the store at 200.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Read-shape returned to the admin page. Mirrors the entity columns but
/// projects them through a stable contract — the entity is internal to
/// the EF implementation.
/// </summary>
public sealed record AuditEntryView
{
    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Action { get; init; }
    public required string TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? TargetLabel { get; init; }
    public AuditOutcome Outcome { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>User id of the actor when present; null for anonymous events (failed login attempts before auth).</summary>
    public string? ActorUserId { get; init; }

    /// <summary>Best-effort display label (email or username) for the actor.</summary>
    public string? ActorEmail { get; init; }

    public string? ActorIpAddress { get; init; }
    public string? ActorUserAgent { get; init; }
    public string? TenantId { get; init; }

    /// <summary>Raw JSON of the payload dictionary (or null when no payload was recorded).</summary>
    public string? PayloadJson { get; init; }
}
