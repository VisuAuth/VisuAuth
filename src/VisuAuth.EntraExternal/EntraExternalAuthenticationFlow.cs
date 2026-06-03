using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.EntraExternal;

/// <summary>
/// <see cref="IAuthenticationFlow"/> shim for the Entra External ID
/// adapter. Mirrors the Workforce shim: every end-user sign-in / register
/// / reset / confirm method returns the "go away, Microsoft owns this"
/// signal because the External flow is hosted by Microsoft (the
/// <c>{tenant}.ciamlogin.com</c> page configured under "User flows").
/// </summary>
/// <remarks>
/// <para>
/// VisuAuth's job here is to render the "Sign in with Microsoft" hint —
/// driven by <see cref="UserBackendCapabilities.SupportsLocalLogin"/> =
/// false on <see cref="EntraExternalCapabilities"/> — and let the
/// consumer's OIDC wiring (typically <c>Microsoft.Identity.Web</c>,
/// landing in v0.3 PR-C) take the customer to the hosted login page.
/// </para>
/// <para>
/// Why this still exists despite the capability flag hiding the form:
/// stale URLs, CLI clients, and direct API callers can still POST to
/// the password endpoint. Returning
/// <see cref="SignInOutcome.RedirectToExternalProvider"/> for sign-in /
/// <see cref="StoreResult.Failure"/> for the others is the safety net —
/// the SignInPageResponseMapper in <c>VisuAuth.EndUserUi</c> turns the
/// redirect outcome into a "use Microsoft sign-in" message, and a
/// failure into a 400 with the same hint.
/// </para>
/// </remarks>
public sealed class EntraExternalAuthenticationFlow : IAuthenticationFlow
{
    private const string NotApplicableMessage =
        "This deployment uses Microsoft Entra External ID. Sign in with the 'Sign in with Microsoft' button on the login page.";

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities => EntraExternalCapabilities.Value;

    /// <inheritdoc />
    public Task<SignInResult> SignInWithPasswordAsync(
        string emailOrUserName,
        string password,
        bool persistent,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SignInResult
        {
            Outcome = SignInOutcome.RedirectToExternalProvider,
            Error = NotApplicableMessage,
        });

    /// <inheritdoc />
    public Task<StoreResult> RegisterAsync(
        string email,
        string password,
        string? tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StoreResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<StoreResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(StoreResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<StoreResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StoreResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<StoreResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
        => Task.FromResult(StoreResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
