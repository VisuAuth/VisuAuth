namespace VisuAuth.Abstractions.Auditing;

/// <summary>
/// Records an <see cref="AuditEvent"/> for later inspection through the
/// admin audit log page. Always present in DI — when the consumer hasn't
/// opted into the audit plugin, the no-op implementation accepts every
/// write and discards it, so caller code never has to check whether
/// auditing is enabled.
/// </summary>
/// <remarks>
/// Implementations enrich the incoming event with ambient state:
/// <list type="bullet">
///   <item><c>ActorUserId</c> / <c>ActorEmail</c> from the current
///     <c>HttpContext.User</c>.</item>
///   <item><c>ActorIpAddress</c> / <c>ActorUserAgent</c> from request
///     headers.</item>
///   <item><c>TenantId</c> from the resolved <c>ITenantContext</c>.</item>
///   <item><c>Timestamp</c> from <see cref="TimeProvider"/>.</item>
/// </list>
/// Failures inside the writer must NEVER bubble up — auditing a side action
/// should not break the primary action. Real implementations log and swallow.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Records the event. Never throws to the caller, even when the
    /// underlying store is unreachable. Returns once persistence completes
    /// (or has been intentionally skipped).
    /// </summary>
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
