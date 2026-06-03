using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Tenancy;

/// <summary>
/// Backend-agnostic tenant catalogue. Implementations expose the set of
/// tenants the host knows about and support inline CRUD from the admin UI.
/// </summary>
/// <remarks>
/// Single-tenant deployments do not register an implementation — the
/// catalogue page only renders when multi-tenancy is enabled.
/// </remarks>
public interface ITenantStore
{
    /// <summary>Lists every tenant in the catalogue.</summary>
    Task<IReadOnlyList<TenantSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a single tenant by id, or <c>null</c> when none matches.</summary>
    Task<TenantSummary?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tenant with the given id (used as routing key) and optional
    /// display name. When <paramref name="displayName"/> is null the id is
    /// reused as the label.
    /// </summary>
    Task<StoreResult> CreateAsync(string id, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Changes the display name of the tenant identified by <paramref name="id"/>.</summary>
    Task<StoreResult> RenameAsync(string id, string newDisplayName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tenant identified by <paramref name="id"/>.</summary>
    Task<StoreResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
