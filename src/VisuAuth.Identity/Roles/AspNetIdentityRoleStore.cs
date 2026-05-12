using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;

namespace VisuAuth.Identity.Roles;

/// <summary>
/// <see cref="IRoleStore"/> implementation backed by ASP.NET Core Identity's
/// <see cref="RoleManager{TRole}"/> for the role catalogue and
/// <see cref="UserManager{TUser}"/> for user-role membership.
/// </summary>
/// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
/// <typeparam name="TRole">The Identity role type used by the consumer.</typeparam>
public sealed class AspNetIdentityRoleStore<TUser, TRole>(
    RoleManager<TRole> roleManager,
    UserManager<TUser> userManager) : IRoleStore
    where TUser : IdentityUser
    where TRole : IdentityRole, new()
{
    private readonly RoleManager<TRole> _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
    private readonly UserManager<TUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummary>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        // tenantId is ignored until multi-tenancy ships — IdentityRole has no
        // tenant column today. The follow-up multi-tenancy PR will filter here.
        _ = tenantId;

        var roles = await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return roles
            .Select(r => new RoleSummary
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty,
                TenantId = null,
                // MemberCount is wired up by the roles-catalogue PR (`feat/admin-ui-roles-catalogue`).
                // Leaving it at 0 here keeps this PR small — no current screen needs the count.
                MemberCount = 0,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RoleSummary?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        var role = await _roleManager.FindByIdAsync(id);
        return role is null
            ? null
            : new RoleSummary
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                TenantId = null,
                MemberCount = 0,
            };
    }

    /// <inheritdoc />
    public async Task<UserResult> CreateAsync(string name, string? tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return UserResult.Failure("Role name is required.");
        }

        _ = tenantId; // see ListAsync — multi-tenancy aware in a later PR
        cancellationToken.ThrowIfCancellationRequested();

        var role = new TRole { Name = name.Trim() };
        var result = await _roleManager.CreateAsync(role);
        return result.Succeeded
            ? UserResult.Success(role.Id)
            : ToFailure(result, "Failed to create role.");
    }

    /// <inheritdoc />
    public async Task<UserResult> RenameAsync(string id, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return UserResult.Failure("New role name is required.");
        }

        var role = await _roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return UserResult.Failure($"Role '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rename = await _roleManager.SetRoleNameAsync(role, newName.Trim());
        if (!rename.Succeeded)
        {
            return ToFailure(rename, "Failed to rename role.");
        }

        // SetRoleNameAsync only mutates the in-memory entity. Persist via UpdateAsync
        // so the normalised name re-indexes too.
        var update = await _roleManager.UpdateAsync(role);
        return update.Succeeded
            ? UserResult.Success(role.Id)
            : ToFailure(update, "Failed to persist role rename.");
    }

    /// <inheritdoc />
    public async Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var role = await _roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return UserResult.Failure($"Role '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _roleManager.DeleteAsync(role);
        return result.Succeeded
            ? UserResult.Success(role.Id)
            : ToFailure(result, "Failed to delete role.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRolesForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return [];
        }

        var roles = await _userManager.GetRolesAsync(user);
        return roles
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<UserResult> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return UserResult.Failure("Role name is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure($"User '{userId}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            return UserResult.Failure($"Role '{roleName}' does not exist.");
        }

        if (await _userManager.IsInRoleAsync(user, roleName))
        {
            return UserResult.Failure($"User is already in role '{roleName}'.");
        }

        var result = await _userManager.AddToRoleAsync(user, roleName);
        return result.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(result, "Failed to assign role.");
    }

    /// <inheritdoc />
    public async Task<UserResult> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return UserResult.Failure("Role name is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure($"User '{userId}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!await _userManager.IsInRoleAsync(user, roleName))
        {
            return UserResult.Failure($"User is not in role '{roleName}'.");
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        return result.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(result, "Failed to remove role.");
    }

    private static UserResult ToFailure(IdentityResult result, string fallback)
    {
        var messages = result.Errors.Select(e => e.Description).ToList();
        return UserResult.Failure(
            messages.Count == 0 ? fallback : messages[0],
            messages);
    }
}
