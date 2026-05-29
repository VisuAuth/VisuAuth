using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Entra.Configuration;

namespace VisuAuth.Entra;

/// <summary>
/// <see cref="IRoleStore"/> backed by Microsoft Graph app roles. The
/// adapter manages the role catalogue + assignments of a single target
/// application — the one whose object id is in
/// <see cref="EntraOptions.AppRoleResourceId"/> (default:
/// <see cref="EntraOptions.ClientId"/>, i.e. VisuAuth's own app).
/// </summary>
/// <remarks>
/// <para>
/// App roles in Entra are <b>declared in the app manifest</b>, not at
/// runtime. The Graph API exposes them read-only on the
/// <see cref="ServicePrincipal.AppRoles"/> collection. Consequently:
/// </para>
/// <list type="bullet">
///   <item><see cref="ListAsync"/> + <see cref="GetAsync"/> work.</item>
///   <item><see cref="GetRolesForUserAsync"/> walks
///     <c>/users/{id}/appRoleAssignments</c>.</item>
///   <item><see cref="AssignRoleAsync"/> / <see cref="RemoveRoleAsync"/>
///     create / delete entries on that collection — the legitimate
///     runtime mutation paths.</item>
///   <item><see cref="CreateAsync"/>, <see cref="RenameAsync"/>,
///     <see cref="DeleteAsync"/> throw <see cref="NotSupportedException"/>
///     — declaring a new role at runtime isn't a Graph operation. Edit
///     the app manifest in the Entra portal instead.</item>
/// </list>
/// <para>
/// The Admin UI's "Roles" page calls only the methods backed by Graph,
/// because the capability-aware sidebar entries already hide the
/// "create role" button when the adapter declares
/// <c>SupportsRoleManagement = true</c> AND
/// <c>SupportsBulkOperations = false</c>. Even so, throwing
/// NotSupported here is the safety net for direct API consumers.
/// </para>
/// </remarks>
public sealed class EntraRoleStore(
    IEntraGraphClient graphClient,
    IOptions<EntraOptions> options) : IRoleStore
{
    private readonly IEntraGraphClient _graphClient =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly EntraOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    // Resolved per call so the store always uses the current client.
    private GraphServiceClient Graph => _graphClient.GetClient();

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummary>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var (servicePrincipalId, appRoles) = await LoadTargetAppAsync(cancellationToken);
        if (servicePrincipalId is null || appRoles.Count == 0)
        {
            return [];
        }

        // Cross-reference: roles + their current assignment count. Member
        // count requires a separate call per role; we do them in parallel
        // bounded to the role count (≤ a few dozen in practice).
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

            var assignments = await Graph.Users[userId].AppRoleAssignments
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

            await Graph.Users[userId].AppRoleAssignments.PostAsync(new AppRoleAssignment
            {
                AppRoleId = role.Id,
                // PrincipalId = the user being assigned the role. Graph
                // mandates this on the body even though the URL already
                // identifies the user (parallel to GroupMembership shape).
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

            var assignments = await Graph.Users[userId].AppRoleAssignments
                .GetAsync(cancellationToken: cancellationToken);

            var target = (assignments?.Value ?? [])
                .FirstOrDefault(a => a.AppRoleId == role.Id);
            if (target?.Id is null)
            {
#pragma warning disable IDE0028 // UserResult.Metadata expects IReadOnlyDictionary — explicit Dictionary<,> here is intentional.
                return UserResult.Success(userId, metadata: new Dictionary<string, string> { ["noop"] = "true" });
#pragma warning restore IDE0028
            }

            await Graph.Users[userId].AppRoleAssignments[target.Id]
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
    /// it differs from the app id (the <see cref="EntraOptions.ClientId"/>
    /// / <see cref="EntraOptions.AppRoleResourceId"/> we configure with).
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
            var principals = await Graph.ServicePrincipals.GetAsync(rc =>
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
        // Pull every assignment on the service principal in one call (no
        // per-role round-trip). Graph returns one row per (principal,
        // role) pair, so the bucket-by-role count is a simple group-by.
        try
        {
            var assignments = await Graph.ServicePrincipals[servicePrincipalId].AppRoleAssignedTo
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
            _ = appRoles; // keep parameter referenced for future per-role fallback
            return [];
        }
    }

    private static AppRole? FindRoleByName(IReadOnlyList<AppRole> roles, string roleName)
        => roles.FirstOrDefault(r =>
            string.Equals(r.DisplayName, roleName, StringComparison.Ordinal)
            || string.Equals(r.Value, roleName, StringComparison.Ordinal)
            || string.Equals(r.Id?.ToString(), roleName, StringComparison.Ordinal));
}
