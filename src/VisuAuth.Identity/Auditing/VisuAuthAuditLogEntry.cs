using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.Identity.Auditing;

/// <summary>
/// VisuAuth-owned metadata row recording one audited action. Stored in the
/// <c>VisuAuthAuditLog</c> table by <see cref="MultiTenantIdentityDbContext{TUser}"/>.
/// Indexed on (Timestamp DESC) for the admin page's default ordering,
/// (ActorUserId) for the per-user drill-down, and (Action) for the action
/// dropdown filter.
/// </summary>
/// <remarks>
/// CLAUDE.md §2.5 — VisuAuth-owned tables are explicit and documented.
/// The schema is conservative on column widths so the table indexes well
/// on SQLite / SQL Server / PostgreSQL without manual tuning.
/// </remarks>
public sealed class VisuAuthAuditLogEntry
{
    /// <summary>Synthetic primary key. Generated client-side as a Guid so the writer can record without a round-trip.</summary>
    public Guid Id { get; set; }

    /// <summary>UTC time the event was recorded, source = <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Action code (e.g. <c>UserLocked</c>). See <see cref="AuditActions"/> for VisuAuth-emitted codes.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entity type the action targeted (e.g. <c>User</c>, <c>Role</c>).</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Id of the target entity (user id, role name, scheme name) — null when the action is broad (e.g. bulk).</summary>
    public string? TargetId { get; set; }

    /// <summary>Cached display label (email, display name) so the admin page doesn't need to re-fetch deleted entities.</summary>
    public string? TargetLabel { get; set; }

    /// <summary>Success / Failure. Persisted as the enum value (int) for index efficiency.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Free-text reason on failure; null on success.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Actor's user id when available. Null for unauthenticated events (failed login attempts, anonymous self-service).</summary>
    public string? ActorUserId { get; set; }

    /// <summary>Actor's email / username at time of the event (snapshotted so renames don't rewrite history).</summary>
    public string? ActorEmail { get; set; }

    /// <summary>Client IP captured from the request (X-Forwarded-For when set by a trusted proxy, otherwise RemoteIpAddress).</summary>
    public string? ActorIpAddress { get; set; }

    /// <summary>User-Agent header snapshot. Truncated to 512 chars by the writer if longer.</summary>
    public string? ActorUserAgent { get; set; }

    /// <summary>Tenant id when multi-tenancy is enabled. Null in single-tenant deployments and for cross-tenant system events.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// JSON-serialised structured payload (the dictionary passed in via
    /// <see cref="AuditEvent.Payload"/>). Never include secrets — payload
    /// is intentionally human-readable in the admin UI.
    /// </summary>
    public string? PayloadJson { get; set; }
}
