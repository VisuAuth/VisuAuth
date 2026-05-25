using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Pure outcome → <see cref="AuditShape"/> mapping. The single source of
/// truth for "which audit action code does this <see cref="SignInOutcome"/>
/// produce?". Every sign-in surface (Razor page, minimal API, future SAML
/// bridge) reads through this so the audit log is consistent regardless
/// of which channel fired.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for outcomes that intentionally don't produce a
/// password-sign-in audit entry — today that's only
/// <see cref="SignInOutcome.RedirectToExternalProvider"/>, which the
/// external-login pages audit themselves with a different action code.
/// </remarks>
public static class SignInAuditMapper
{
    /// <summary>
    /// Triple of (action, outcome, reason) the emitter feeds into the
    /// <see cref="AuditEvent"/>. Reason is null on success.
    /// </summary>
    public sealed record AuditShape(string Action, AuditOutcome Outcome, string? FailureReason);

    /// <summary>Maps a sign-in result to the audit shape, or null to skip.</summary>
    public static AuditShape? FromOutcome(SignInResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            SignInOutcome.Success => new(AuditActions.LoginSucceeded, AuditOutcome.Success, null),
            SignInOutcome.RequiresTwoFactor => new(AuditActions.LoginRequiresTwoFactor, AuditOutcome.Success, null),
            SignInOutcome.LockedOut => new(AuditActions.LoginLockedOut, AuditOutcome.Failure, "Account locked out"),
            SignInOutcome.NotAllowed => new(AuditActions.LoginFailed, AuditOutcome.Failure, result.Error ?? "Sign-in not allowed"),
            // Routed to an external provider — the external-login flow
            // audits its own outcome under a different action code.
            SignInOutcome.RedirectToExternalProvider => null,
            // InvalidCredentials + any future outcome both fall to the
            // generic "we failed the sign-in" branch.
            _ => new(AuditActions.LoginFailed, AuditOutcome.Failure, "InvalidCredentials"),
        };
    }
}
