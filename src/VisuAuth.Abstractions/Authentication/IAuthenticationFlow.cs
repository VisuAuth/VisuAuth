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
    UserBackendCapabilities Capabilities { get; }

    Task<SignInResult> SignInWithPasswordAsync(string emailOrUserName, string password, bool persistent, CancellationToken cancellationToken = default);

    Task<UserResult> RegisterAsync(string email, string password, string? tenantId, CancellationToken cancellationToken = default);

    Task<UserResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    Task<UserResult> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    Task<UserResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}

public sealed record SignInResult
{
    public required SignInOutcome Outcome { get; init; }
    public string? UserId { get; init; }
    public string? Error { get; init; }

    public static SignInResult Success(string userId) => new() { Outcome = SignInOutcome.Success, UserId = userId };
    public static SignInResult RequiresTwoFactor(string userId) => new() { Outcome = SignInOutcome.RequiresTwoFactor, UserId = userId };
    public static SignInResult LockedOut() => new() { Outcome = SignInOutcome.LockedOut };
    public static SignInResult NotAllowed(string? reason = null) => new() { Outcome = SignInOutcome.NotAllowed, Error = reason };
    public static SignInResult InvalidCredentials() => new() { Outcome = SignInOutcome.InvalidCredentials };
}

public enum SignInOutcome
{
    Success,
    RequiresTwoFactor,
    LockedOut,
    NotAllowed,
    InvalidCredentials,
    RedirectToExternalProvider,
}
