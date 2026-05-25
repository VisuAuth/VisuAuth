using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Maps <see cref="SignInResult"/> outcomes to a small decision-record
/// the Razor login page interprets. Keeps the mapper pure (no
/// <c>PageModel</c> / <c>IActionResult</c> deps) so it's easy to unit
/// test and the page model owns the navigation side-effects.
/// </summary>
/// <remarks>
/// The page calls <see cref="Map"/>, then matches on
/// <see cref="SignInPageOutcome.Decision"/>:
/// <list type="bullet">
///   <item><c>RedirectSuccess</c> — page invokes its own
///     <c>ResolveSuccessRedirectAsync</c> (deep-link vs local).</item>
///   <item><c>RedirectTwoFactor</c> — page builds the
///     <c>/visuauth/two-factor/verify</c> URL with rememberMe + returnUrl.</item>
///   <item><c>RenderPage</c> — page sets
///     <c>ErrorMessage = result.Error ?? Ls[ErrorResourceKey]</c> and
///     returns Page().</item>
/// </list>
/// </remarks>
public static class SignInPageResponseMapper
{
    /// <summary>Decision + localisation hint for the Razor page.</summary>
    public static SignInPageOutcome Map(SignInResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            SignInOutcome.Success => new SignInPageOutcome(SignInPageDecision.RedirectSuccess, ErrorResourceKey: null, RawErrorMessage: null),
            SignInOutcome.RequiresTwoFactor => new SignInPageOutcome(SignInPageDecision.RedirectTwoFactor, ErrorResourceKey: null, RawErrorMessage: null),
            SignInOutcome.LockedOut => new SignInPageOutcome(SignInPageDecision.RenderPage, "Login.Error.Locked", RawErrorMessage: null),
            SignInOutcome.NotAllowed => new SignInPageOutcome(SignInPageDecision.RenderPage, "Login.Error.NotAllowed", RawErrorMessage: result.Error),
            SignInOutcome.RedirectToExternalProvider => new SignInPageOutcome(SignInPageDecision.RenderPage, "Login.Error.ExternalRequired", RawErrorMessage: null),
            // InvalidCredentials + future outcomes → intentionally generic.
            _ => new SignInPageOutcome(SignInPageDecision.RenderPage, "Login.Error.Invalid", RawErrorMessage: null),
        };
    }
}

/// <summary>What the page should do after the sign-in attempt.</summary>
public enum SignInPageDecision
{
    /// <summary>Re-render the login form with an error banner.</summary>
    RenderPage,

    /// <summary>Hand the user off to <c>/visuauth/two-factor/verify</c>.</summary>
    RedirectTwoFactor,

    /// <summary>Resolve the returnUrl (deep-link or local) and redirect.</summary>
    RedirectSuccess,
}

/// <summary>
/// Page-mapper output. When <see cref="RawErrorMessage"/> is non-null it
/// wins over the resource key — surfaces backend-specific text
/// (e.g. NotAllowed reason) that the resource bundle can't predict.
/// </summary>
public sealed record SignInPageOutcome(
    SignInPageDecision Decision,
    string? ErrorResourceKey,
    string? RawErrorMessage);
