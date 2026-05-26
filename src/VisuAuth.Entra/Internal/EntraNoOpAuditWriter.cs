using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.Entra.Internal;

/// <summary>
/// Fallback <see cref="IAuditWriter"/> the Entra adapter registers when the
/// host hasn't wired a real audit store. Mirrors the no-op writer in
/// VisuAuth.Identity (which lives <c>internal</c> behind a different
/// package, so the Entra adapter can't reuse it directly — CLAUDE.md §2.5
/// keeps adapters independent of each other).
/// </summary>
/// <remarks>
/// Without this fallback, <see cref="VisuAuth.EndUserUi"/>'s
/// <c>SignInAuditEmitter</c> can't be resolved by DI in Entra-only
/// deployments — every login attempt would crash. Registered via
/// <c>TryAdd</c> in <see cref="DependencyInjection.VisuAuthEntraExtensions"/>
/// so a consumer who wires the audit-log plugin keeps their real writer.
/// </remarks>
internal sealed class EntraNoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
