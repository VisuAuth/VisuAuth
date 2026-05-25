using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.Identity.Auditing;

/// <summary>
/// <see cref="IAuditWriter"/> fallback that accepts every write and
/// discards it silently. Registered by default in
/// <c>AddVisuAuth().UseAspNetIdentity()</c> so caller code can always do
/// <c>await _audit.WriteAsync(...)</c> without checking whether the
/// audit plugin is enabled; the EF-backed writer takes over only when the
/// consumer calls <c>AddVisuAuthAuditLog()</c>.
/// </summary>
/// <remarks>
/// Important contract: implementations of <see cref="IAuditWriter"/> MUST
/// be safe to call from every action handler, opt-in or not. Returning a
/// completed Task and doing nothing else here makes the call effectively
/// free when auditing is off.
/// </remarks>
internal sealed class NoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
