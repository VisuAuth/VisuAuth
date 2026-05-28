using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.EntraExternal.Web;

/// <summary>
/// Real <see cref="IExternalLoginFlow"/> for the Entra External adapter,
/// backed by Microsoft.Identity.Web's OIDC + Cookies wiring. Replaces the
/// <c>EntraNoOpExternalLoginFlow</c> stub the EntraCore package registers
/// when the consumer wires only admin CRUD; once
/// <c>AddVisuAuthEntraExternalSignIn(...)</c> fires, this implementation
/// surfaces the "Sign in with Microsoft" button on
/// <c>/visuauth/login</c> and closes the round-trip back to a working
/// local session.
/// </summary>
/// <remarks>
/// <para>
/// <b>How the round-trip works.</b> The user clicks the button →
/// <c>/visuauth/external-login/start</c> POST with the OIDC scheme name
/// → <c>StartModel</c> issues a <see cref="Microsoft.AspNetCore.Mvc.ChallengeResult"/>
/// → Microsoft.Identity.Web redirects to
/// <c>{tenant}.ciamlogin.com/{tenant-id}/v2.0/authorize</c> → user signs
/// in on the hosted page → Microsoft posts back to <c>/signin-oidc</c>
/// → Microsoft.Identity.Web validates the id_token + writes the Cookies
/// scheme cookie + redirects to <c>/visuauth/external-login/callback</c>.
/// At that point <see cref="HttpContext.User"/> is already authenticated;
/// our job in <see cref="CompleteSignInAsync"/> is to verify the user
/// exists in the directory (defence against a stale token that beats
/// directory cleanup) and translate the success into the envelope the
/// existing Callback page expects.
/// </para>
/// <para>
/// <b>Why no SignInManager equivalent.</b> The Identity adapter's flow
/// calls <c>SignInManager.SignInAsync</c> after resolving the user
/// because IdentityFx owns the local cookie. In External mode,
/// Microsoft.Identity.Web's Cookies handler IS the local cookie — by the
/// time we run, the user is signed in already. We don't issue a second
/// session.
/// </para>
/// <para>
/// <b>First-time strategies in External mode.</b> Customers in an
/// External tenant register through Microsoft's hosted signup user flow,
/// not through a VisuAuth confirmation page — so the
/// <see cref="ExternalLoginFirstTimeStrategy.AlwaysConfirm"/> and
/// <see cref="ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm"/>
/// branches are graceful failures with a clear message rather than
/// surfacing the VisuAuth confirm page (which can't create users in the
/// External directory; only Microsoft can). The <c>AutoCreate</c>
/// strategy is the supported happy path: the user already exists in
/// Graph because the hosted signup wrote them there before redirecting
/// back. PR D adds the signup customisation (user flow selection,
/// attribute mapping) that makes the End-user UI ride the hosted signup
/// directly when appropriate.
/// </para>
/// </remarks>
public sealed class EntraExternalLoginFlow : IExternalLoginFlow
{
    /// <summary>
    /// Scheme name surfaced to the login page. Must match the OIDC
    /// scheme Microsoft.Identity.Web registers — see
    /// <see cref="VisuAuth.EntraExternal.Web.DependencyInjection.VisuAuthEntraExternalSignInExtensions"/>
    /// where both halves are pinned to the same value.
    /// </summary>
    public const string ProviderScheme = "MicrosoftEntraExternal";

    /// <summary>Display name on the "Sign in with Microsoft" button.</summary>
    public const string ProviderDisplayName = "Sign in with Microsoft";

    private static readonly ExternalProviderInfo[] Providers =
    [
        new ExternalProviderInfo
        {
            Scheme = ProviderScheme,
            DisplayName = ProviderDisplayName,
        },
    ];

    /// <summary>
    /// Standard OIDC claim type for the immutable per-user identifier
    /// (Microsoft Graph user object id). Used as the primary lookup
    /// key against <see cref="IUserStore"/>.
    /// </summary>
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>Backup claim name some configurations emit instead of the long URI form.</summary>
    private const string ObjectIdClaimShort = "oid";

    /// <summary>Standard OIDC name claim — display name from the hosted login page.</summary>
    private const string NameClaim = "name";

    private readonly IHttpContextAccessor _http;
    private readonly IUserStore _userStore;
    private readonly IEntraExternalProfileSync _profileSync;

    public EntraExternalLoginFlow(
        IHttpContextAccessor httpContextAccessor,
        IUserStore userStore,
        IEntraExternalProfileSync profileSync)
    {
        _http = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
        _profileSync = profileSync ?? throw new ArgumentNullException(nameof(profileSync));
        // Reuses the External adapter's capability set and overlays
        // SupportsExternalProviders = true — once this flow is wired we
        // DO have a provider to surface. The EntraExternalCapabilities
        // singleton stays at false because the CRUD-only adapter has no
        // providers to list; the overlay happens at this layer (the
        // package that actually wires OIDC).
        Capabilities = _userStore.Capabilities with { SupportsExternalProviders = true };
    }

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Single static provider — the user flow selection (PR D) will
        // still surface as one button; flow-specific URLs are negotiated
        // through the OIDC handler's options, not by listing multiple
        // flows here.
        return Task.FromResult<IReadOnlyList<ExternalProviderInfo>>(Providers);
    }

    /// <inheritdoc />
    public async Task<ExternalSignInResult> CompleteSignInAsync(
        ExternalLoginFirstTimeStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var principal = _http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            // The OIDC handler should have signed the user into the
            // Cookies scheme before reaching the Callback page. If we get
            // here without an authenticated principal, the cookie was
            // dropped (CSP / SameSite mishap) or the handler errored —
            // surface the same outcome as a missing external cookie so
            // the Callback page's NoExternalSession branch lights up.
            return ExternalSignInResult.NoExternalSession();
        }

        var objectId = ResolveObjectId(principal);
        if (string.IsNullOrEmpty(objectId))
        {
            return ExternalSignInResult.Failed(
                ["The Microsoft-issued token does not carry an object identifier (oid). Check the OIDC scopes."]);
        }

        // Verify the directory still has this user. Defence against a
        // stale token that beats directory cleanup (admin deleted the
        // customer in the portal while a long-lived session was active).
        var user = await _userStore.GetAsync(objectId, cancellationToken);
        if (user is null)
        {
            return strategy switch
            {
                // AutoCreate: hosted signup populates Graph synchronously;
                // a missing user here means propagation hasn't completed
                // OR the token claim doesn't actually map to a directory
                // object. Either way, treat as failed — we can't safely
                // auto-create against the External directory (Microsoft
                // owns identity creation through user flows).
                ExternalLoginFirstTimeStrategy.AutoCreate
                    => ExternalSignInResult.Failed(
                        ["Signed in user was not found in the Entra External directory. The hosted signup flow may not have completed."]),

                // RequiresConfirmation paths don't apply to External:
                // VisuAuth's confirm page would call IUserStore.CreateAsync
                // which lacks the user-flow context Microsoft uses to wire
                // an OIDC subject to a new directory entry. Surface a
                // graceful failure with an actionable message instead of
                // leading the user into a dead-end form.
                ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm
                or ExternalLoginFirstTimeStrategy.AlwaysConfirm
                    => ExternalSignInResult.Failed(
                        ["Confirmation-based first-time strategies do not apply to Entra External — the hosted signup user flow owns directory creation. Use AutoCreate."]),

                _ => ExternalSignInResult.Failed(
                        [$"Unknown first-time strategy '{strategy}'."]),
            };
        }

        // Happy path. The Cookies scheme cookie was set by
        // Microsoft.Identity.Web before we reached the Callback page, so
        // the user is already authenticated locally.

        // Best-effort: copy any sign-up-flow-collected attributes off the
        // token onto the Graph user profile (opt-in via
        // EntraExternalWebOptions.ProfileSync). No-op when disabled; never
        // throws — a profile-sync failure must not break a sign-in the
        // user already completed.
        await _profileSync.SyncAsync(principal, objectId, cancellationToken);

        // Return Success + the directory id so the audit log + return-url
        // redirect can proceed.
        return ExternalSignInResult.Success(user.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not reachable in normal External flows — see the class-level
    /// remark on first-time strategies. Implemented as a graceful failure
    /// so a stale link to the confirm page (from a copy-pasted URL or a
    /// browser-back navigation) doesn't crash.
    /// </remarks>
    public Task<ExternalSignInResult> ConfirmAndCreateAsync(
        string email,
        string? userName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExternalSignInResult.Failed(
            ["The VisuAuth confirmation page does not apply to Entra External: customer accounts are created by Microsoft's hosted signup user flow. Configure a sign-up user flow in the Entra portal instead."]));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The confirm page reads pending claims via this method when it
    /// renders. In External mode the principal is already authenticated
    /// (not "pending"), but we honour the contract anyway: surfacing the
    /// same shape lets the confirm page render without a null check, and
    /// the page itself produces a graceful failure as soon as the user
    /// posts (via <see cref="ConfirmAndCreateAsync"/>).
    /// </remarks>
    public Task<ExternalPendingInfo?> GetPendingInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var principal = _http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult<ExternalPendingInfo?>(null);
        }

        var objectId = ResolveObjectId(principal);
        if (string.IsNullOrEmpty(objectId))
        {
            return Task.FromResult<ExternalPendingInfo?>(null);
        }

        return Task.FromResult<ExternalPendingInfo?>(new ExternalPendingInfo
        {
            Provider = ProviderScheme,
            ProviderKey = objectId,
            Email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("preferred_username"),
            DisplayName = principal.FindFirstValue(NameClaim)
                          ?? principal.FindFirstValue(ClaimTypes.Name),
        });
    }

    /// <summary>
    /// Reads the Microsoft object id from a claims principal. Tries the
    /// long URI form first (what Microsoft.Identity.Web's default claim
    /// mapper emits), falls back to the short <c>oid</c> claim some
    /// configurations use.
    /// </summary>
    private static string? ResolveObjectId(ClaimsPrincipal principal)
        => principal.FindFirstValue(ObjectIdClaim)
            ?? principal.FindFirstValue(ObjectIdClaimShort);

    /// <summary>
    /// Pinned constant for the cookie auth scheme this package relies on
    /// — surfaced so unit tests and downstream consumers can reference
    /// the same value Microsoft.Identity.Web's default wiring uses
    /// without importing the cookie auth defaults type.
    /// </summary>
    internal const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
}
