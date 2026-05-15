using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using SignInResult = VisuAuth.Abstractions.Authentication.SignInResult;

namespace VisuAuth.EndUserUi.Pages.TwoFactor;

/// <summary>
/// Post-password 2FA challenge. The user has already proved their password
/// (Identity stored a partial "two-factor user id" cookie); this page
/// collects either the TOTP code from their authenticator app or one of
/// their recovery codes and finishes the sign-in.
/// </summary>
/// <remarks>
/// Anonymous-accessible by design — the partial cookie is the user's only
/// proof at this stage. <see cref="ITwoFactorFlow"/> guards everything below
/// it; if the cookie is missing the underlying SignInManager call returns
/// an InvalidCredentials outcome and the user is asked to start over.
/// </remarks>
public sealed class VerifyModel(
    ITwoFactorFlow twoFactor,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly ITwoFactorFlow _twoFactor = twoFactor ?? throw new ArgumentNullException(nameof(twoFactor));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public ChallengeForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "rememberMe")]
    public bool RememberMe { get; set; } = true;

    public UserBackendCapabilities Capabilities => _twoFactor.Capabilities;

    /// <summary>Error message rendered above the authenticator code form.</summary>
    public string? AuthenticatorError { get; private set; }

    /// <summary>Error message rendered inside the recovery-code disclosure.</summary>
    public string? RecoveryError { get; private set; }

    /// <summary>
    /// Top-of-page banner used for the rare cross-cutting cases (lockout,
    /// not-allowed, capability-missing) where the failure is not specific to
    /// either form.
    /// </summary>
    public string? GlobalError { get; private set; }

    /// <summary>
    /// True after a failed recovery-code submit so the Razor view re-opens
    /// the &lt;details&gt; disclosure on render — otherwise the user sees
    /// the form they used collapse out from under them.
    /// </summary>
    public bool RecoveryDetailsOpen { get; private set; }

    public IActionResult OnGet()
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            GlobalError = _l["TwoFactor.NotSupported"].Value;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAuthenticatorAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            GlobalError = _l["TwoFactor.NotSupported"].Value;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.Code))
        {
            AuthenticatorError = _l["TwoFactor.Verify.Error.CodeRequired"].Value;
            return Page();
        }

        var result = await _twoFactor.TwoFactorAuthenticatorSignInAsync(
            Form.Code,
            RememberMe,
            Form.RememberMachine,
            cancellationToken);

        return HandleSignInResult(result, isRecovery: false);
    }

    public async Task<IActionResult> OnPostRecoveryAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            GlobalError = _l["TwoFactor.NotSupported"].Value;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.RecoveryCode))
        {
            RecoveryError = _l["TwoFactor.Verify.Error.RecoveryRequired"].Value;
            RecoveryDetailsOpen = true;
            return Page();
        }

        var result = await _twoFactor.TwoFactorRecoveryCodeSignInAsync(Form.RecoveryCode, cancellationToken);
        return HandleSignInResult(result, isRecovery: true);
    }

    private IActionResult HandleSignInResult(SignInResult result, bool isRecovery)
    {
        switch (result.Outcome)
        {
            case SignInOutcome.Success:
                return Redirect(SanitiseLocalReturnUrl(ReturnUrl));
            case SignInOutcome.LockedOut:
                GlobalError = _l["TwoFactor.Verify.Error.Locked"].Value;
                return Page();
            case SignInOutcome.NotAllowed:
                GlobalError = _l["TwoFactor.Verify.Error.NotAllowed"].Value;
                return Page();
            case SignInOutcome.InvalidCredentials:
            default:
                if (isRecovery)
                {
                    RecoveryError = _l["TwoFactor.Verify.Error.RecoveryInvalid"].Value;
                    RecoveryDetailsOpen = true;
                }
                else
                {
                    AuthenticatorError = _l["TwoFactor.Verify.Error.CodeInvalid"].Value;
                }
                return Page();
        }
    }

    private string SanitiseLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }
        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }

    public sealed class ChallengeForm
    {
        public string? Code { get; set; }
        public string? RecoveryCode { get; set; }
        public bool RememberMachine { get; set; }
    }
}
