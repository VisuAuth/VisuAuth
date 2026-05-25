using Microsoft.AspNetCore.Http;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EndUserUi.Api;

namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Maps non-success <see cref="SignInResult"/> outcomes to JSON
/// <see cref="IResult"/> responses for the minimal-API <c>/api/auth/login</c>
/// endpoint. Centralises the status-code policy (423 / 401 / 403) so the
/// handler stays as orchestration only and adding a new outcome means
/// editing one place.
/// </summary>
/// <remarks>
/// The success branch isn't mapped here because it doesn't return a
/// canned message — it goes through the JWT issuer. Callers handle that
/// path explicitly before delegating to this mapper.
/// </remarks>
public static class SignInApiResponseMapper
{
    /// <summary>
    /// Returns the canonical HTTP response for a non-success outcome.
    /// Pre-condition: caller must NOT call this for
    /// <see cref="SignInOutcome.Success"/> — that's not a failure to map.
    /// </summary>
    public static IResult MapFailure(SignInResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            SignInOutcome.LockedOut =>
                Results.Json(new AuthErrorResponse("Account is locked."), statusCode: StatusCodes.Status423Locked),
            SignInOutcome.RequiresTwoFactor =>
                Results.Json(new AuthErrorResponse("Two-factor authentication is required."), statusCode: StatusCodes.Status401Unauthorized),
            SignInOutcome.NotAllowed =>
                Results.Json(new AuthErrorResponse(result.Error ?? "Sign-in is not allowed for this account."), statusCode: StatusCodes.Status403Forbidden),
            // InvalidCredentials + everything else collapses to a generic
            // 401 so we never leak whether the email exists.
            _ =>
                Results.Json(new AuthErrorResponse("Email or password is incorrect."), statusCode: StatusCodes.Status401Unauthorized),
        };
    }
}
