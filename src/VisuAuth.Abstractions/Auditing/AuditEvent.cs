namespace VisuAuth.Abstractions.Auditing;

/// <summary>
/// One write-shape carrying everything a caller knows about an action: the
/// semantic <see cref="Action"/> (a stable string code, not a sentence) plus
/// the target and an optional structured <see cref="Payload"/>. Identity
/// fields (actor, tenant, IP, user agent, timestamp) are filled in by the
/// <see cref="IAuditWriter"/> implementation from ambient state — the
/// caller doesn't have to round-trip them through every handler.
/// </summary>
/// <remarks>
/// Action codes are stable strings rather than enums because the audit log
/// is forward-compatible — adding a new event from a future plugin should
/// not require a recompile of the audit page or the store. Convention: a
/// constant in <see cref="AuditActions"/> per known event. Custom callers
/// can pass any string they like.
/// </remarks>
public sealed record AuditEvent
{
    /// <summary>Stable code identifying the action, e.g. <c>UserLocked</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Type of the affected entity, e.g. <c>User</c>, <c>Role</c>, <c>ExternalProvider</c>.</summary>
    public required string TargetType { get; init; }

    /// <summary>Id of the affected entity (user id, role name, scheme name). Null when the action has no specific target (e.g. bulk operation).</summary>
    public string? TargetId { get; init; }

    /// <summary>Display label for the target so the admin UI doesn't have to re-fetch (e.g. user email, role display name).</summary>
    public string? TargetLabel { get; init; }

    /// <summary>Whether the action succeeded or failed. Failures are useful for security review.</summary>
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    /// <summary>Free-text reason for a failure (locked-out account, wrong password, validation error). Null on success.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Arbitrary key/value payload — keep it small and never include secrets.
    /// Examples: <c>{"newRole": "Manager"}</c> for RoleAssigned,
    /// <c>{"reason": "admin-initiated"}</c> for RevokeSessions.
    /// Serialized to JSON by the store.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Payload { get; init; }
}

/// <summary>Whether an audited action ended in success or failure.</summary>
public enum AuditOutcome
{
    Success,
    Failure,
}
