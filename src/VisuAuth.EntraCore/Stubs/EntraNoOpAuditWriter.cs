using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.EntraCore.Stubs;

/// <summary>
/// Fallback <see cref="IAuditWriter"/> the Entra adapter family
/// registers when the host hasn't wired a real audit store. Without
/// this, VisuAuth.EndUserUi's <c>SignInAuditEmitter</c> can't be
/// resolved by DI in Entra-only deployments — every login attempt
/// would crash with "Unable to resolve IAuditWriter".
/// </summary>
/// <remarks>
/// Mirrors the no-op writer in VisuAuth.Identity (which lives
/// <c>internal</c> behind a different package, so the Entra adapter
/// family can't reuse it directly — CLAUDE.md §2.5 keeps adapters
/// independent of each other). Registered via <c>TryAdd</c> in each
/// adapter's DI extension so a consumer who wires the audit-log
/// plugin keeps their real writer.
/// </remarks>
public sealed class EntraNoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
