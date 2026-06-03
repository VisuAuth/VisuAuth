using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Backend-agnostic authentication flow consumed by the end-user UI and the mobile API.
/// </summary>
/// <remarks>
/// Adapters for cloud IAMs (Entra) typically override only the methods that make
/// sense and return a "redirect to external provider" signal for the rest.
/// </remarks>
public interface IAuthenticationFlow
{
    /// <summary>Features this backend supports. Inspected at runtime by the UI.</summary>
    UserBackendCapabilities Capabilities { get; }

    /// <summary>Attempts a password sign-in for the given email or user name.</summary>
    Task<SignInResult> SignInWithPasswordAsync(string emailOrUserName, string password, bool persistent, CancellationToken cancellationToken = default);

    /// <summary>Registers a new self-service user (optionally scoped to a tenant).</summary>
    Task<StoreResult> RegisterAsync(string email, string password, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Starts a password-reset for the given email (e.g. issues a reset token / email).</summary>
    Task<StoreResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Completes a password-reset using the token issued by <see cref="RequestPasswordResetAsync"/>.</summary>
    Task<StoreResult> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Confirms a user's email address using the supplied confirmation token.</summary>
    Task<StoreResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);

    /// <summary>Signs the current user out (clears the auth cookie / session).</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a password sign-in attempt.</summary>
public sealed record SignInResult
{
    /// <summary>What happened during the attempt.</summary>
    public required SignInOutcome Outcome { get; init; }

    /// <summary>Identifier of the user, when the attempt resolved one.</summary>
    public string? UserId { get; init; }

    /// <summary>Failure detail, when relevant (e.g. the reason a sign-in was not allowed).</summary>
    public string? Error { get; init; }

    /// <summary>Creates a successful sign-in result for the given user.</summary>
    public static SignInResult Success(string userId) => new() { Outcome = SignInOutcome.Success, UserId = userId };

    /// <summary>Creates a result indicating the user must complete a two-factor challenge.</summary>
    public static SignInResult RequiresTwoFactor(string userId) => new() { Outcome = SignInOutcome.RequiresTwoFactor, UserId = userId };

    /// <summary>Creates a result indicating the account is locked out.</summary>
    public static SignInResult LockedOut() => new() { Outcome = SignInOutcome.LockedOut };

    /// <summary>Creates a result indicating sign-in is not allowed, with an optional reason.</summary>
    public static SignInResult NotAllowed(string? reason = null) => new() { Outcome = SignInOutcome.NotAllowed, Error = reason };

    /// <summary>Creates a result indicating the credentials were invalid.</summary>
    public static SignInResult InvalidCredentials() => new() { Outcome = SignInOutcome.InvalidCredentials };
}

/// <summary>Outcome of a password sign-in attempt.</summary>
public enum SignInOutcome
{
    /// <summary>The user is fully signed in.</summary>
    Success,

    /// <summary>Credentials were valid but a two-factor challenge is required.</summary>
    RequiresTwoFactor,

    /// <summary>The account is locked out.</summary>
    LockedOut,

    /// <summary>Sign-in is not allowed (e.g. email not confirmed).</summary>
    NotAllowed,

    /// <summary>The email/user name or password was incorrect.</summary>
    InvalidCredentials,

    /// <summary>The backend hosts login externally; the caller should redirect to the provider.</summary>
    RedirectToExternalProvider,
}
