using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra.Configuration;
using VisuAuth.Entra.Internal;
using VisuAuth.Entra.Mapping;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.Entra;

/// <summary>
/// <see cref="IUserStore"/> against the Microsoft Graph API. Acts on the
/// directory of the Entra tenant configured in <see cref="EntraOptions"/>
/// using the app-only (client credentials) flow established by
/// <c>AddVisuAuthEntra</c>.
/// </summary>
/// <remarks>
/// <para>
/// Capability surface is fixed in <see cref="EntraCapabilities"/>. Each
/// method that maps cleanly to a Graph endpoint executes the call; methods
/// the capability set declares as unsupported throw
/// <see cref="NotSupportedException"/> per the IUserStore contract.
/// </para>
/// <para>
/// <b>Pagination caveat:</b> Graph's <c>/users</c> endpoint paginates with
/// <c>@odata.nextLink</c> + skip tokens, not numeric page indices. The
/// PagedResult contract VisuAuth ships uses 1-based pages — to keep the
/// admin UI working without rewriting it, this implementation honours
/// <see cref="UserFilter.PageSize"/> exactly and treats every call as
/// "page 1". The admin can refine via search / filter (which DO push down
/// to Graph) or raise <c>PageSize</c>. v0.3 will add cursor-based paging
/// to the abstraction so the UI can render "next" buttons against the
/// real Graph cursor.
/// </para>
/// <para>
/// <b>Failure mapping:</b> Graph errors arrive as
/// <see cref="ODataError"/>. They're converted to
/// <see cref="UserResult.Failure"/> with the Graph error message so the
/// admin UI sees something actionable (e.g. "Insufficient privileges to
/// complete the operation" when a permission is missing). Unknown
/// exceptions are logged and re-wrapped as a generic failure rather than
/// propagated — auditing handlers above us treat a returned failure as
/// "user-facing", and an uncaught exception as a 500.
/// </para>
/// </remarks>
public sealed class EntraUserStore(
    GraphServiceClient graphClient,
    IOptions<EntraOptions> options,
    ILogger<EntraUserStore> logger) : IUserStore
{
    /// <summary>
    /// Key under which <see cref="UserResult.Metadata"/> carries the
    /// one-time temporary password from <see cref="CreateAsync"/> and
    /// <see cref="ResetPasswordAsync"/>. Matches the convention the
    /// ASP.NET Identity adapter uses so the admin UI's click-to-copy
    /// widget keeps working unchanged.
    /// </summary>
    public const string TemporaryPasswordMetadataKey = "temporaryPassword";

    /// <summary>
    /// Single-source UserResult.Failure text for the "Graph returned 404"
    /// branch — hoisted here so Sonar's "no magic strings" rule (S1192)
    /// is satisfied AND the message stays consistent across the half-dozen
    /// methods that surface it.
    /// </summary>
    private const string UserNotFoundMessage = "User not found.";

    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly EntraOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<EntraUserStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private UserBackendCapabilities? _capabilitiesCache;

    /// <inheritdoc />
    /// <remarks>
    /// Overlays <see cref="EntraOptions.DefaultEmailDomain"/> onto the
    /// static <see cref="EntraCapabilities.Value"/> so the admin Create-User
    /// form picks up the locked-suffix UX without the consumer having to
    /// touch capabilities directly. Cached after first read because both
    /// inputs are immutable for the lifetime of the store.
    /// </remarks>
    public UserBackendCapabilities Capabilities => _capabilitiesCache ??=
        EntraCapabilities.Value with
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
                rc.QueryParameters.Select = EntraUserMapper.SummarySelect.Split(',');
            }, cancellationToken);
            return user is null ? null : EntraUserMapper.ToSummary(user);
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
                rc.QueryParameters.Select = EntraUserMapper.DetailSelect.Split(',');
            }, cancellationToken);

            if (user is null)
            {
                return null;
            }

            // Resolve the user's app role assignments against the configured
            // target application. Best-effort — a missing AppRoleResourceId
            // or insufficient Application.Read.All just lands the detail
            // page with an empty roles list instead of erroring out.
            var roles = await ResolveRolesAsync(id, cancellationToken);
            return EntraUserMapper.ToDetail(user, roles);
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
            var response = await _graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Top = pageSize;
                rc.QueryParameters.Select = EntraUserMapper.SummarySelect.Split(',');
                var graphFilter = EntraUserMapper.BuildGraphFilter(filter);
                if (graphFilter is not null)
                {
                    rc.QueryParameters.Filter = graphFilter;
                    // Advanced query capabilities (filter, count, search)
                    // require eventual consistency level on directory
                    // objects. Without this header Graph rejects the call
                    // for anything beyond the trivial $select.
                    rc.Headers.Add("ConsistencyLevel", "eventual");
                }
            }, cancellationToken);

            var items = (response?.Value ?? [])
                .Select(EntraUserMapper.ToSummary)
                .ToList();

            return new PagedResult<UserSummary>
            {
                Items = items,
                // Graph doesn't return an exact total without an extra
                // $count call. Treating it as items.Count is honest for
                // page 1 and keeps the admin UI from rendering a
                // "showing 1-25 of ?" line with a misleading number.
                // PagedResult.HasNext = false when Total <= PageSize,
                // which is the right default for the no-cursor v0.2.
                Total = items.Count,
                Page = 1,
                PageSize = pageSize,
            };
        }
        catch (ODataError ex)
        {
            _logger.GraphListFailed(ex, ex.Error?.Message);
            return PagedResult<UserSummary>.Empty(1, pageSize);
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
            var (graphUser, tempPassword) = EntraUserMapper.ToGraphCreate(command, EntraTemporaryPassword.Generate);
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
            await _graph.Users[id].PatchAsync(EntraUserMapper.ToGraphUpdate(command), cancellationToken: cancellationToken);
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
            // Entra equivalent of "lock user": PATCH accountEnabled = false.
            // Setting it back to true unlocks. No separate lockout window —
            // user stays disabled until an admin re-enables them.
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
            // Approach: PATCH the user with a new passwordProfile carrying a
            // freshly generated temporary password and forceChangePassword
            // = true. This is the legacy admin-reset flow — it requires
            // User.ReadWrite.All (a permission already needed for
            // CreateAsync), versus UserAuthenticationMethod.ReadWrite.All
            // which the dedicated authentication-methods reset endpoint
            // needs and which many tenants don't grant. Tradeoff documented
            // in EntraOptions remarks.
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
    /// Not supported by the v0.2 Entra adapter (see
    /// <see cref="EntraCapabilities"/> — <c>SupportsTwoFactorReset = false</c>).
    /// Per-method DELETE on Graph requires a typed builder per subtype
    /// (microsoftAuthenticatorMethods, fido2Methods, etc.) — out of scope
    /// for this milestone. The admin UI hides the button when the
    /// capability flag is off, so this code path is reached only when a
    /// caller bypasses the UI.
    /// </remarks>
    public Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Reset two-factor is not supported on the v0.2 Microsoft Entra adapter. "
            + "Use the Entra portal's 'Authentication methods' blade for the user.");

    /// <inheritdoc />
    public async Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            // POST /users/{id}/revokeSignInSessions invalidates every
            // refresh token; existing access tokens still work until their
            // (short) lifetime expires. The newer
            // PostAsRevokeSignInSessionsPostResponseAsync overload returns
            // a typed response — the boolean payload is just "did Graph
            // queue the revocation", which we don't surface upward; the
            // operation either succeeds or throws.
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
            // List the user's app role assignments AND the target app's
            // declared app roles in parallel, then resolve assignment id
            // → role display name. Concurrent dispatch keeps the detail
            // page latency at max(assignment-call, app-call) rather than
            // the sum.
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

            // Collection expressions: `?? []` is the modern equivalent
            // of `?? new List<T>()` and is what Sonar / Roslyn's IDE0028
            // expects. The Graph SDK still returns a concrete List<T>,
            // but the expression infers the right type from context.
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
