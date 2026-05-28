using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.EntraExternal.Web;

/// <summary>
/// Decorates the CRUD adapter's <see cref="EntraExternalAuthenticationFlow"/>
/// to make <see cref="SignOutAsync"/> actually clear the local session.
/// Every other method delegates straight through to the inner flow — the
/// password-form shims (sign-in → "use Microsoft", register / reset /
/// confirm → graceful failure) are unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a decorator.</b> The inner
/// <see cref="EntraExternalAuthenticationFlow"/> lives in
/// <c>VisuAuth.EntraExternal</c>, which is a backend-agnostic CRUD package
/// with no dependency on ASP.NET authentication or
/// <see cref="HttpContext"/> — so its <c>SignOutAsync</c> can only be a
/// no-op. But the End-user session in External mode is the cookie
/// <c>Microsoft.Identity.Web</c> writes under
/// <see cref="CookieAuthenticationDefaults.AuthenticationScheme"/>, and
/// clearing it requires <see cref="HttpContext.SignOutAsync(string)"/>.
/// That capability only exists in this package (the one that wired OIDC),
/// so the real sign-out belongs here. The canonical
/// <c>/visuauth/logout</c> page calls
/// <see cref="IAuthenticationFlow.SignOutAsync"/>, so wrapping the flow
/// fixes logout without introducing a second, External-specific logout
/// URL.
/// </para>
/// <para>
/// <b>Local sign-out, not federated.</b> This clears the app's own cookie
/// — the user is signed out of the VisuAuth app. It does NOT trigger an
/// OIDC end-session redirect to Microsoft, so the hosted
/// <c>{tenant}.ciamlogin.com</c> session persists (clicking "Sign in with
/// Microsoft" again may re-authenticate silently). Federated sign-out
/// needs the post-logout redirect URI registered on the app registration
/// and is opt-in territory for a later iteration; local cookie sign-out
/// is the right default because it always works without extra portal
/// configuration and matches what most CIAM apps want from a "log out of
/// this app" button.
/// </para>
/// </remarks>
public sealed class EntraExternalWebAuthenticationFlow(
    IAuthenticationFlow inner,
    IHttpContextAccessor httpContextAccessor) : IAuthenticationFlow
{
    private readonly IAuthenticationFlow _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IHttpContextAccessor _http =
        httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities => _inner.Capabilities;

    /// <inheritdoc />
    public Task<SignInResult> SignInWithPasswordAsync(
        string emailOrUserName,
        string password,
        bool persistent,
        CancellationToken cancellationToken = default)
        => _inner.SignInWithPasswordAsync(emailOrUserName, password, persistent, cancellationToken);

    /// <inheritdoc />
    public Task<UserResult> RegisterAsync(
        string email,
        string password,
        string? tenantId,
        CancellationToken cancellationToken = default)
        => _inner.RegisterAsync(email, password, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<UserResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
        => _inner.RequestPasswordResetAsync(email, cancellationToken);

    /// <inheritdoc />
    public Task<UserResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
        => _inner.ResetPasswordAsync(email, token, newPassword, cancellationToken);

    /// <inheritdoc />
    public Task<UserResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
        => _inner.ConfirmEmailAsync(userId, token, cancellationToken);

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        // Keep the inner contract (no-op today, but future-proof if the
        // CRUD flow ever needs to do bookkeeping on sign-out).
        await _inner.SignOutAsync(cancellationToken);

        // The real work: drop the cookie Microsoft.Identity.Web issued.
        // Without this the user stays authenticated even after POSTing to
        // /visuauth/logout, because the inner flow can't reach HttpContext.
        var context = _http.HttpContext;
        if (context is not null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
