using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.EntraExternal.Configuration;

namespace VisuAuth.EntraExternal;

/// <summary>
/// <see cref="IRoleStore"/> backed by Microsoft Graph app roles for an
/// Entra External tenant. Identical behaviour to the Workforce adapter's
/// store — same Graph endpoints, same NotSupported branches for create /
/// rename / delete (app roles are declared in the application manifest,
/// not at runtime). The only difference is the typed
/// <see cref="EntraExternalOptions"/> dependency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this looks identical to <c>VisuAuth.Entra.EntraRoleStore</c>.</b>
/// Microsoft Graph treats app roles the same way regardless of tenant
/// family — they live on the app's service principal and are assigned to
/// users through <c>/users/{id}/appRoleAssignments</c>. We deliberately
/// duplicate the implementation (rather than refactor a shared base into
/// VisuAuth.EntraCore) for three reasons:
/// </para>
/// <list type="number">
///   <item>
///     The shared abstraction would have to carry a typed options object
///     to satisfy DI in both adapters — meaning a coupling on either
///     <c>EntraOptions</c> or <c>EntraExternalOptions</c> from EntraCore,
///     which would invert the dependency direction.
///   </item>
///   <item>
///     The v0.2 Workforce store's public surface is stable; refactoring
///     it would be a breaking ctor change immediately after a major
///     release.
///   </item>
///   <item>
///     The Graph contract for app roles is unlikely to drift between
///     tenant families — the duplication risk is small. If a third Entra
///     adapter appears (or the abstraction visibly drifts), the refactor
///     should land then in a single follow-up.
///   </item>
/// </list>
/// </remarks>
public sealed class EntraExternalRoleStore(
    GraphServiceClient graphClient,
    IOptions<EntraExternalOptions> options) : IRoleStore
{
    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly EntraExternalOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummary>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var (servicePrincipalId, appRoles) = await LoadTargetAppAsync(cancellationToken);
        if (servicePrincipalId is null || appRoles.Count == 0)
        {
            return [];
        }

        var counts = await CountAssignmentsPerRoleAsync(servicePrincipalId, appRoles, cancellationToken);

        return appRoles
            .Where(r => r.Id.HasValue)
            .Select(r => new RoleSummary
            {
                Id = r.Id!.Value.ToString(),
                Name = r.DisplayName ?? r.Value ?? r.Id!.Value.ToString(),
                TenantId = null,
                MemberCount = counts.TryGetValue(r.Id!.Value, out var c) ? c : 0,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RoleSummary?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Guid.TryParse(id, out var roleGuid))
        {
            return null;
        }

        var (_, appRoles) = await LoadTargetAppAsync(cancellationToken);
        var role = appRoles.FirstOrDefault(r => r.Id == roleGuid);
        if (role is null)
        {
            return null;
        }
        return new RoleSummary
        {
            Id = id,
            Name = role.DisplayName ?? role.Value ?? id,
            TenantId = null,
            MemberCount = 0,
        };
    }

    /// <inheritdoc />
    public Task<UserResult> CreateAsync(string name, string? tenantId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "App roles are declared in the Entra app manifest, not at runtime. "
            + "Open the app registration in the Entra portal and add the role there, "
            + "then refresh this page.");

    /// <inheritdoc />
    public Task<UserResult> RenameAsync(string id, string newName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Renaming app roles is not supported by Microsoft Graph at runtime.");

    /// <inheritdoc />
    public Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Deleting app roles is not supported by Microsoft Graph at runtime.");

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRolesForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        try
        {
            var (_, appRoles) = await LoadTargetAppAsync(cancellationToken);
            var byId = appRoles.Where(r => r.Id.HasValue)
                .ToDictionary(r => r.Id!.Value, r => r.DisplayName ?? r.Value ?? r.Id!.Value.ToString());

            var assignments = await _graph.Users[userId].AppRoleAssignments
                .GetAsync(cancellationToken: cancellationToken);

            return (assignments?.Value ?? [])
                .Where(a => a.AppRoleId.HasValue && byId.ContainsKey(a.AppRoleId!.Value))
                .Select(a => byId[a.AppRoleId!.Value])
                .ToList();
        }
        catch (ODataError)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        try
        {
            var (servicePrincipalId, appRoles) = await LoadTargetAppAsync(cancellationToken);
            if (servicePrincipalId is null)
            {
                return UserResult.Failure("Target app's service principal not found.");
            }

            var role = FindRoleByName(appRoles, roleName);
            if (role?.Id is null)
            {
                return UserResult.Failure($"Role '{roleName}' is not declared on the target app.");
            }

            await _graph.Users[userId].AppRoleAssignments.PostAsync(new AppRoleAssignment
            {
                AppRoleId = role.Id,
                PrincipalId = Guid.Parse(userId),
                ResourceId = Guid.Parse(servicePrincipalId),
            }, cancellationToken: cancellationToken);

            return UserResult.Success(userId);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(ex.Error?.Message ?? "Failed to assign role.");
        }
        catch (FormatException)
        {
            return UserResult.Failure("User id must be a valid GUID for the Entra adapter.");
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        try
        {
            var (_, appRoles) = await LoadTargetAppAsync(cancellationToken);
            var role = FindRoleByName(appRoles, roleName);
            if (role?.Id is null)
            {
                return UserResult.Failure($"Role '{roleName}' is not declared on the target app.");
            }

            var assignments = await _graph.Users[userId].AppRoleAssignments
                .GetAsync(cancellationToken: cancellationToken);

            var target = (assignments?.Value ?? [])
                .FirstOrDefault(a => a.AppRoleId == role.Id);
            if (target?.Id is null)
            {
#pragma warning disable IDE0028 // UserResult.Metadata expects IReadOnlyDictionary — explicit Dictionary<,> here is intentional.
                return UserResult.Success(userId, metadata: new Dictionary<string, string> { ["noop"] = "true" });
#pragma warning restore IDE0028
            }

            await _graph.Users[userId].AppRoleAssignments[target.Id]
                .DeleteAsync(cancellationToken: cancellationToken);

            return UserResult.Success(userId);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(ex.Error?.Message ?? "Failed to remove role.");
        }
    }

    /// <summary>
    /// Loads the service principal for the configured target app and
    /// returns (servicePrincipalId, appRoles). The service principal id
    /// is needed for the appRoleAssignment <c>ResourceId</c> property —
    /// it differs from the app id we configure with (different GUID).
    /// </summary>
    private async Task<(string? servicePrincipalId, IReadOnlyList<AppRole> appRoles)> LoadTargetAppAsync(
        CancellationToken cancellationToken)
    {
        var targetAppId = _options.AppRoleResourceId ?? _options.ClientId;
        if (string.IsNullOrEmpty(targetAppId))
        {
            return (null, []);
        }

        try
        {
            var principals = await _graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"appId eq '{targetAppId}'";
                rc.QueryParameters.Select = ["id", "appRoles"];
                rc.Headers.Add("ConsistencyLevel", "eventual");
            }, cancellationToken);

            var principal = principals?.Value?.FirstOrDefault();
            if (principal is null)
            {
                return (null, []);
            }
            return (principal.Id, principal.AppRoles ?? []);
        }
        catch (ODataError)
        {
            return (null, []);
        }
    }

    private async Task<Dictionary<Guid, int>> CountAssignmentsPerRoleAsync(
        string servicePrincipalId,
        IReadOnlyList<AppRole> appRoles,
        CancellationToken cancellationToken)
    {
        try
        {
            var assignments = await _graph.ServicePrincipals[servicePrincipalId].AppRoleAssignedTo
                .GetAsync(cancellationToken: cancellationToken);

            return (assignments?.Value ?? [])
                .Where(a => a.AppRoleId.HasValue)
                .GroupBy(a => a.AppRoleId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        catch (ODataError)
        {
            // Return zeros — the role catalogue still renders, just without
            // the member-count column populated. Common when the registered
            // app lacks Application.Read.All in a strict tenant.
            _ = appRoles;
            return [];
        }
    }

    private static AppRole? FindRoleByName(IReadOnlyList<AppRole> roles, string roleName)
        => roles.FirstOrDefault(r =>
            string.Equals(r.DisplayName, roleName, StringComparison.Ordinal)
            || string.Equals(r.Value, roleName, StringComparison.Ordinal)
            || string.Equals(r.Id?.ToString(), roleName, StringComparison.Ordinal));
}
