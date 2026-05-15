using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Backend-agnostic surface for TOTP-based two-factor authentication.
/// Separate from <see cref="IAuthenticationFlow"/> because not every adapter
/// owns the 2FA store: cloud IAMs (Entra) handle MFA on their hosted login
/// pages and would simply leave <see cref="UserBackendCapabilities.SupportsTwoFactor"/>
/// false rather than implement this surface.
/// </summary>
public interface ITwoFactorFlow
{
    /// <summary>Mirrors the capability bag the rest of VisuAuth consults.</summary>
    UserBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Loads (and lazily generates) the authenticator shared key for
    /// <paramref name="userId"/>. Returns the raw key plus the
    /// <c>otpauth://</c> URI ready to be encoded into a QR code.
    /// </summary>
    Task<AuthenticatorSetup?> GetAuthenticatorSetupAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the current shared key and provisions a fresh one. Used both
    /// by the admin "Reset 2FA" path and by an end user who wants to re-enroll
    /// from scratch.
    /// </summary>
    Task<UserResult> ResetAuthenticatorKeyAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> against the authenticator shared key
    /// and, on success, flips the user's two-factor flag on. Returns failure
    /// (without enabling) when the code is wrong or the user is missing.
    /// </summary>
    Task<UserResult> EnableTwoFactorAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables two-factor authentication for the user and clears the
    /// authenticator shared key so re-enrollment requires a fresh setup.
    /// </summary>
    Task<UserResult> DisableTwoFactorAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the user has the authenticator app enrolled
    /// and two-factor is currently enabled.
    /// </summary>
    Task<bool> IsTwoFactorEnabledAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a fresh batch of recovery codes, replacing any previous set.
    /// The plain-text codes are surfaced once via <see cref="UserResult.Metadata"/>
    /// under the key <c>recoveryCodes</c> (newline-joined) — the caller MUST
    /// show them once and never persist them.
    /// </summary>
    Task<UserResult> GenerateRecoveryCodesAsync(string userId, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a sign-in challenge using a TOTP code. Requires the partial
    /// "two-factor user id" cookie set by the prior password sign-in attempt.
    /// </summary>
    Task<SignInResult> TwoFactorAuthenticatorSignInAsync(
        string code,
        bool persistent,
        bool rememberMachine,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a sign-in challenge using one recovery code. The code is
    /// consumed atomically — a second attempt with the same code must fail.
    /// </summary>
    Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot of the authenticator shared key and matching <c>otpauth://</c>
/// URI for QR-code display.
/// </summary>
public sealed record AuthenticatorSetup
{
    /// <summary>Identifier of the user the key belongs to.</summary>
    public required string UserId { get; init; }

    /// <summary>Account label rendered inside authenticator apps (typically the email).</summary>
    public required string AccountName { get; init; }

    /// <summary>Base32-encoded shared key, formatted in 4-character groups for manual entry.</summary>
    public required string FormattedKey { get; init; }

    /// <summary>Raw shared key as returned by the backend (no separators).</summary>
    public required string RawKey { get; init; }

    /// <summary>
    /// <c>otpauth://totp/...</c> URI ready to be rendered as a QR code by the
    /// authenticator app pairing screen.
    /// </summary>
    public required string OtpAuthUri { get; init; }
}
