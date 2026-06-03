using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Roles;

/// <summary>
/// Backend-agnostic role management. Implementations should throw
/// <see cref="NotSupportedException"/> when the backend lacks role support
/// (e.g. some Entra configurations).
/// </summary>
public interface IRoleStore
{
    /// <summary>Lists the roles defined in the given tenant scope (pass <c>null</c> for the global scope).</summary>
    Task<IReadOnlyList<RoleSummary>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Loads a single role by id, or <c>null</c> when none matches.</summary>
    Task<RoleSummary?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates a role with the given name in the optional tenant scope.</summary>
    Task<StoreResult> CreateAsync(string name, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Renames the role identified by <paramref name="id"/>.</summary>
    Task<StoreResult> RenameAsync(string id, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the role identified by <paramref name="id"/>.</summary>
    Task<StoreResult> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns the names of the roles assigned to the given user.</summary>
    Task<IReadOnlyList<string>> GetRolesForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Assigns the named role to the user.</summary>
    Task<StoreResult> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>Removes the named role from the user.</summary>
    Task<StoreResult> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
}

/// <summary>Lightweight projection of a role for catalogue listings.</summary>
public sealed record RoleSummary
{
    /// <summary>Stable backend identifier for the role.</summary>
    public required string Id { get; init; }

    /// <summary>Role name.</summary>
    public required string Name { get; init; }

    /// <summary>Identifier of the tenant the role belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>Number of users currently assigned this role.</summary>
    public int MemberCount { get; init; }
}
