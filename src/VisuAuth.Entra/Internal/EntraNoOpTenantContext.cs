using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Entra.Internal;

/// <summary>
/// Fallback <see cref="ITenantContext"/> the Entra adapter registers
/// when the host hasn't wired multi-tenancy. The Identity adapter has
/// its own no-op (in VisuAuth.Identity, internal-ish to that package);
/// CLAUDE.md §2.5 keeps adapters from depending on each other, so the
/// Entra adapter ships its own.
/// </summary>
/// <remarks>
/// Per-user tenancy isn't a concept on the Entra side — the directory
/// itself IS the tenant — so this never gets swapped for anything else.
/// Reports multi-tenancy as disabled so any code that branches on
/// <see cref="ITenantContext.IsMultiTenancyEnabled"/> degrades gracefully.
/// </remarks>
internal sealed class EntraNoOpTenantContext : ITenantContext
{
    public bool IsMultiTenancyEnabled => false;
    public string? CurrentTenantId => null;
    public string? CurrentTenantDisplayName => null;
}
