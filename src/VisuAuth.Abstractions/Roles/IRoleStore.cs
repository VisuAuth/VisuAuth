using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Roles;

/// <summary>
/// Backend-agnostic role management. Implementations should throw
/// <see cref="NotSupportedException"/> when the backend lacks role support
/// (e.g. some Entra configurations).
/// </summary>
public interface IRoleStore
{
    Task<IReadOnlyList<RoleSummary>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);

    Task<RoleSummary?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<UserResult> CreateAsync(string name, string? tenantId, CancellationToken cancellationToken = default);

    Task<UserResult> RenameAsync(string id, string newName, CancellationToken cancellationToken = default);

    Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRolesForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<UserResult> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);

    Task<UserResult> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
}

public sealed record RoleSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TenantId { get; init; }
    public int MemberCount { get; init; }
}
