using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraCore.Infrastructure;
using VisuAuth.EntraCore.Security;
using VisuAuth.EntraExternal.Configuration;
using VisuAuth.EntraExternal.Internal;
using VisuAuth.EntraExternal.Mapping;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.EntraExternal;

/// <summary>
/// <see cref="IUserStore"/> against the Microsoft Graph API for an Entra
/// External ID tenant. Acts on the directory configured in
/// <see cref="EntraExternalOptions"/> using the app-only (client
/// credentials) flow established by <c>AddVisuAuthEntraExternal</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>How this differs from <c>VisuAuth.Entra.EntraUserStore</c>.</b>
/// The Graph endpoint shape is the same; the user payload differs.
/// External ID users carry an <c>identities[]</c> array (the local
/// email-account credential + any federated providers) instead of a
/// human-meaningful <c>userPrincipalName</c>. <see cref="CreateAsync"/>
/// here builds that identities entry from
/// <see cref="EntraExternalOptions.TenantDomain"/>; the list / detail /
/// update / disable / reset paths reuse Graph the same way the Workforce
/// adapter does but project the result through
/// <see cref="EntraExternalUserMapper"/>, which prefers the
/// customer-typed email from identities[] over the auto-generated UPN.
/// </para>
/// <para>
/// Capability surface is fixed in <see cref="EntraExternalCapabilities"/>.
/// Methods the capability set declares as unsupported throw
/// <see cref="NotSupportedException"/> per the IUserStore contract.
/// </para>
/// <para>
/// <b>Pagination:</b> same as the Workforce adapter — Graph's
/// <c>@odata.nextLink</c> continuation maps onto the cursor-based
/// <see cref="PagedResult{T}"/>. The first call applies
/// <see cref="UserFilter.PageSize"/> as <c>$top</c>; the returned
/// <see cref="PagedResult{T}.NextCursor"/> wraps Graph's (origin-validated)
/// continuation link, and <see cref="PagedResult{T}.TotalCount"/> stays
/// <see langword="null"/> because Graph returns no count alongside a page.
/// </para>
/// <para>
/// <b>Failure mapping:</b> Graph errors arrive as
/// <see cref="ODataError"/>. They're converted to
/// <see cref="UserResult.Failure"/> with the Graph error message so the
/// admin UI sees something actionable. Unknown exceptions are logged and
/// re-wrapped as a generic failure rather than propagated — the auditing
/// handlers above us treat a returned failure as "user-facing", and an
/// uncaught exception as a 500.
/// </para>
/// </remarks>
public sealed class EntraExternalUserStore(
    GraphServiceClient graphClient,
    IOptions<EntraExternalOptions> options,
    ILogger<EntraExternalUserStore> logger) : IUserStore
{
    /// <summary>
    /// Key under which <see cref="UserResult.Metadata"/> carries the
    /// one-time temporary password from <see cref="CreateAsync"/> and
    /// <see cref="ResetPasswordAsync"/>. Same convention as the Identity
    /// and Workforce-Entra adapters so the admin UI's click-to-copy
    /// widget keeps working unchanged regardless of backend.
    /// </summary>
    public const string TemporaryPasswordMetadataKey = "temporaryPassword";

    /// <summary>
    /// Single-source UserResult.Failure text for the "Graph returned 404"
    /// branch — hoisted so Sonar's no-magic-strings rule (S1192) is
    /// satisfied AND the message stays consistent across methods.
    /// </summary>
    private const string UserNotFoundMessage = "User not found.";

    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly EntraExternalOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<EntraExternalUserStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private UserBackendCapabilities? _capabilitiesCache;

    /// <inheritdoc />
    /// <remarks>
    /// Overlays <see cref="EntraExternalOptions.DefaultEmailDomain"/> onto
    /// the static <see cref="EntraExternalCapabilities.Value"/> so the
    /// admin Create-User form picks up the locked-suffix UX without the
    /// consumer having to touch capabilities directly. Cached after first
    /// read because both inputs are immutable for the lifetime of the
    /// store.
    /// </remarks>
    public UserBackendCapabilities Capabilities => _capabilitiesCache ??=
        EntraExternalCapabilities.Value with
        {
            EmailDomainSuffix = string.IsNullOrWhiteSpace(_options.DefaultEmailDomain)
                ? null
                : "@" + _options.DefaultEmailDomain.TrimStart('@'),
        };

    /// <inheritdoc />
    public async Task<UserSummary?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            var user = await _graph.Users[id].GetAsync(rc =>
            {
                rc.QueryParameters.Select = EntraExternalUserMapper.SummarySelect.Split(',');
            }, cancellationToken);
            return user is null ? null : EntraExternalUserMapper.ToSummary(user);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UserDetail?> GetDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            var user = await _graph.Users[id].GetAsync(rc =>
            {
                rc.QueryParameters.Select = EntraExternalUserMapper.DetailSelect.Split(',');
            }, cancellationToken);

            if (user is null)
            {
                return null;
            }

            var roles = await ResolveRolesAsync(id, cancellationToken);
            return EntraExternalUserMapper.ToDetail(user, roles);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var pageSize = Math.Clamp(filter.PageSize, 1, 999);

        try
        {
            UserCollectionResponse? response;
            if (GraphPageCursor.TryDecode(filter.Cursor, _options.GraphBaseUrl, "users", out var nextLink))
            {
                // Continuation: the skiptoken URL already carries the original
                // $top / $filter / $select. Replay it and re-assert the
                // consistency level (a header, not part of the URL) since the
                // External search clause is identities-aware.
                response = await _graph.Users
                    .WithUrl(nextLink)
                    .GetAsync(rc => rc.Headers.Add("ConsistencyLevel", "eventual"), cancellationToken);
            }
            else
            {
                response = await _graph.Users.GetAsync(rc =>
                {
                    rc.QueryParameters.Top = pageSize;
                    rc.QueryParameters.Select = EntraExternalUserMapper.SummarySelect.Split(',');
                    var graphFilter = EntraExternalUserMapper.BuildGraphFilter(filter);
                    if (graphFilter is not null)
                    {
                        rc.QueryParameters.Filter = graphFilter;
                        // Advanced query capabilities (filter, count, search) AND
                        // any predicate against identities/* require the
                        // ConsistencyLevel: eventual header. The mapper's search
                        // clause is identities-aware, so the header is always
                        // needed once a filter is present.
                        rc.Headers.Add("ConsistencyLevel", "eventual");
                    }
                }, cancellationToken);
            }

            var items = (response?.Value ?? [])
                .Select(EntraExternalUserMapper.ToSummary)
                .ToList();

            return new PagedResult<UserSummary>
            {
                Items = items,
                // Graph's continuation link becomes our opaque cursor; no
                // cheap total is available, so TotalCount stays null and the
                // UI renders a count-only "showing N" line.
                NextCursor = GraphPageCursor.Encode(response?.OdataNextLink),
                TotalCount = null,
            };
        }
        catch (ODataError ex)
        {
            _logger.GraphListFailed(ex, ex.Error?.Message);
            return PagedResult<UserSummary>.Empty();
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return UserResult.Failure("Email is required.");
        }

        try
        {
            var (graphUser, tempPassword) = EntraExternalUserMapper.ToGraphCreate(
                command,
                _options.TenantDomain,
                EntraTemporaryPassword.Generate);
            var created = await _graph.Users.PostAsync(graphUser, cancellationToken: cancellationToken);
            if (created?.Id is null)
            {
                return UserResult.Failure("Graph returned no user id on create.");
            }

            return UserResult.Success(
                created.Id,
#pragma warning disable IDE0028 // UserResult.Metadata expects IReadOnlyDictionary so target-typed new() can't be used here — the explicit type is the correct shape, not a missed simplification.
                metadata: new Dictionary<string, string> { [TemporaryPasswordMetadataKey] = tempPassword });
#pragma warning restore IDE0028
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to create user."));
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> UpdateAsync(string id, UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            await _graph.Users[id].PatchAsync(EntraExternalUserMapper.ToGraphUpdate(command), cancellationToken: cancellationToken);
            return UserResult.Success(id);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to update user."));
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            await _graph.Users[id].DeleteAsync(cancellationToken: cancellationToken);
            return UserResult.Success(id);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to delete user."));
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            // Entra equivalent of "lock customer": PATCH accountEnabled =
            // false. Setting it back to true unlocks. No separate lockout
            // window — user stays disabled until an admin re-enables them.
            await _graph.Users[id].PatchAsync(new GraphUser { AccountEnabled = enabled }, cancellationToken: cancellationToken);
            return UserResult.Success(id);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex,
                enabled ? "Failed to enable user." : "Failed to disable user."));
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> ResetPasswordAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            // PATCH the user with a new passwordProfile carrying a fresh
            // temporary password and forceChangePassword = true. Same
            // trade-off as the Workforce adapter: this is the legacy
            // admin-reset flow that needs User.ReadWrite.All (a permission
            // already required for CreateAsync), versus
            // UserAuthenticationMethod.ReadWrite.All which the dedicated
            // authentication-methods reset endpoint needs and which many
            // tenants don't grant.
            var tempPassword = EntraTemporaryPassword.Generate();
            await _graph.Users[id].PatchAsync(new GraphUser
            {
                PasswordProfile = new PasswordProfile
                {
                    Password = tempPassword,
                    ForceChangePasswordNextSignIn = true,
                },
            }, cancellationToken: cancellationToken);

            return UserResult.Success(
                id,
#pragma warning disable IDE0028 // UserResult.Metadata expects IReadOnlyDictionary so target-typed new() can't be used here — the explicit type is the correct shape, not a missed simplification.
                metadata: new Dictionary<string, string> { [TemporaryPasswordMetadataKey] = tempPassword });
#pragma warning restore IDE0028
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to reset password."));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deletes every removable authentication method the customer has
    /// registered so they must re-enrol. Shared with the Workforce adapter
    /// through <see cref="EntraTwoFactorReset"/> (the Graph surface is
    /// identical across tenant families). Requires the registered app to
    /// hold <c>UserAuthenticationMethod.ReadWrite.All</c>.
    /// </remarks>
    public async Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            await EntraTwoFactorReset.RemoveAllAsync(_graph, id, cancellationToken);
            return UserResult.Success(id);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to reset two-factor."));
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            // POST /users/{id}/revokeSignInSessions invalidates every
            // refresh token; existing access tokens still work until their
            // (short) lifetime expires. Same shape as the Workforce
            // adapter; the typed PostAsRevokeSignInSessionsPostResponseAsync
            // overload returns a boolean payload we don't surface upward.
            await _graph.Users[id].RevokeSignInSessions
                .PostAsRevokeSignInSessionsPostResponseAsync(cancellationToken: cancellationToken);
            return UserResult.Success(id);
        }
        catch (ODataError ex) when (IsNotFound(ex))
        {
            return UserResult.Failure(UserNotFoundMessage);
        }
        catch (ODataError ex)
        {
            return UserResult.Failure(GraphMessage(ex, "Failed to revoke sessions."));
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRolesAsync(string userId, CancellationToken cancellationToken)
    {
        var resourceId = _options.AppRoleResourceId ?? _options.ClientId;
        if (string.IsNullOrEmpty(resourceId))
        {
            return [];
        }

        try
        {
            // Same shape as Workforce: list the user's app role assignments
            // AND the target app's declared app roles in parallel, then
            // resolve assignment id → role display name. Concurrent dispatch
            // keeps detail-page latency at max(assignment-call, app-call).
            var assignmentsTask = _graph.Users[userId].AppRoleAssignments
                .GetAsync(cancellationToken: cancellationToken);
            var appRolesTask = _graph.ServicePrincipals
                .GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = $"appId eq '{resourceId}'";
                    rc.QueryParameters.Select = ["id", "appRoles"];
                    rc.Headers.Add("ConsistencyLevel", "eventual");
                }, cancellationToken);

            await Task.WhenAll(assignmentsTask, appRolesTask);

            var assignments = (await assignmentsTask)?.Value ?? [];
            var appRoles = (await appRolesTask)?.Value?.FirstOrDefault()?.AppRoles ?? [];

            var byId = appRoles
                .Where(r => r.Id.HasValue)
                .ToDictionary(r => r.Id!.Value, r => r.DisplayName ?? r.Value ?? r.Id!.Value.ToString());

            var result = new List<string>(assignments.Count);
            foreach (var assignment in assignments)
            {
                if (assignment.AppRoleId is { } roleId && byId.TryGetValue(roleId, out var name))
                {
                    result.Add(name);
                }
            }
            return result;
        }
        catch (ODataError ex)
        {
            _logger.GraphRoleResolutionFailed(ex, userId, ex.Error?.Message);
            return [];
        }
    }

    private static bool IsNotFound(ODataError ex)
        => ex.ResponseStatusCode == 404
            || string.Equals(ex.Error?.Code, "Request_ResourceNotFound", StringComparison.Ordinal);

    private static string GraphMessage(ODataError ex, string fallback)
        => ex.Error?.Message is { Length: > 0 } m ? m : fallback;
}
