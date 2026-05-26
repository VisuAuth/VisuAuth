using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.Entra;

/// <summary>
/// <see cref="IAuthenticationFlow"/> shim for the Entra adapter. Every
/// end-user sign-in / register / reset / confirm method returns the
/// "go away, Microsoft owns this" signal because the Entra workforce
/// flow is entirely hosted by Microsoft: VisuAuth's job is to render
/// the "Sign in with Microsoft" button (driven by
/// <see cref="UserBackendCapabilities.SupportsLocalLogin"/> = false on
/// our <see cref="EntraCapabilities"/>) and let the existing external
/// login pipeline take the user to Microsoft's hosted login screen.
/// </summary>
/// <remarks>
/// <para>
/// The shape parallels the existing Identity adapter's flow — the same
/// <see cref="SignInResult"/> / <see cref="UserResult"/> surface — so
/// every end-user page (Login, Register, ForgotPassword, etc.) can
/// resolve <see cref="IAuthenticationFlow"/> from DI without knowing
/// which backend is wired. The pages already branch on capabilities to
/// hide the form entirely; this implementation is the safety net for
/// the rare case where a stale URL or a CLI client still POSTs to the
/// password endpoint.
/// </para>
/// <para>
/// <see cref="SignInOutcome.RedirectToExternalProvider"/> is the signal
/// the login page interprets to swap "wrong password" messaging for a
/// "you need to use Microsoft sign-in" hint (see
/// <see cref="VisuAuth.EndUserUi"/>'s SignInPageResponseMapper). Same
/// reason RegisterAsync / RequestPasswordResetAsync return that error
/// — the operation isn't "wrong", it just doesn't exist in this
/// backend.
/// </para>
/// </remarks>
public sealed class EntraAuthenticationFlow : IAuthenticationFlow
{
    private const string NotApplicableMessage =
        "This deployment uses Microsoft Entra ID. Sign in with the 'Sign in with Microsoft' button on the login page.";

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities => EntraCapabilities.Value;

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
    public Task<UserResult> RegisterAsync(
        string email,
        string password,
        string? tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UserResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<UserResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(UserResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<UserResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UserResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task<UserResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
        => Task.FromResult(UserResult.Failure(NotApplicableMessage));

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
