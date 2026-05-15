using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.EndUserUi.TwoFactor;

namespace VisuAuth.EndUserUi.Pages.TwoFactor;

/// <summary>
/// Authenticator-app pairing page. Generates (or reuses) the user's shared
/// key, renders it as a QR plus manual-entry string, and asks the user to
/// type a six-digit code from their app to confirm enrollment before flipping
/// two-factor on.
/// </summary>
/// <remarks>
/// Cookie-authenticated only — anonymous users are bounced to <c>/visuauth/login</c>
/// by the <see cref="AuthorizeAttribute"/>. The page is intentionally
/// idempotent: refreshing it does not rotate the key, only an explicit
/// "Generate new key" POST does.
/// </remarks>
[Authorize]
public sealed class SetupModel(
    ITwoFactorFlow twoFactor,
    IQrCodeSvgRenderer qrCodeRenderer,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly ITwoFactorFlow _twoFactor = twoFactor ?? throw new ArgumentNullException(nameof(twoFactor));
    private readonly IQrCodeSvgRenderer _qrCodeRenderer = qrCodeRenderer ?? throw new ArgumentNullException(nameof(qrCodeRenderer));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public string? VerificationCode { get; set; }

    public UserBackendCapabilities Capabilities => _twoFactor.Capabilities;

    public AuthenticatorSetup? Setup { get; private set; }

    public string? QrCodeSvg { get; private set; }

    public bool IsEnabled { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            return await NotSupportedAsync(cancellationToken);
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(VerificationCode))
        {
            ErrorMessage = _l["TwoFactor.Setup.Error.CodeRequired"].Value;
            return await LoadAsync(cancellationToken);
        }

        var result = await _twoFactor.EnableTwoFactorAsync(userId, VerificationCode, cancellationToken);
        if (!result.IsSuccess)
        {
            // Adapter returns an English fallback message; the page owns the
            // localized text. Always prefer the page's own message for the
            // canonical "wrong code" failure mode.
            ErrorMessage = _l["TwoFactor.Setup.Error.CodeInvalid"].Value;
            return await LoadAsync(cancellationToken);
        }

        // Land on recovery codes immediately — generating them is the only
        // safe next step, otherwise a lost authenticator locks the user out.
        return Redirect("/visuauth/two-factor/recovery-codes?generated=true");
    }

    public async Task<IActionResult> OnPostResetKeyAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            return await NotSupportedAsync(cancellationToken);
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        var result = await _twoFactor.ResetAuthenticatorKeyAsync(userId, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error ?? _l["TwoFactor.Setup.Error.ResetFailed"].Value;
        }
        else
        {
            SuccessMessage = _l["TwoFactor.Setup.KeyRotated"].Value;
        }
        return await LoadAsync(cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            return await NotSupportedAsync(cancellationToken);
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        IsEnabled = await _twoFactor.IsTwoFactorEnabledAsync(userId, cancellationToken);
        Setup = await _twoFactor.GetAuthenticatorSetupAsync(userId, cancellationToken);
        if (Setup is not null)
        {
            QrCodeSvg = _qrCodeRenderer.Render(Setup.OtpAuthUri);
        }
        return Page();
    }

    private Task<IActionResult> NotSupportedAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ErrorMessage = _l["TwoFactor.NotSupported"].Value;
        return Task.FromResult<IActionResult>(Page());
    }

    private string? ResolveUserId()
    {
        // SignInManager's cookie identity carries the user id in the
        // NameIdentifier claim by default; falling back to UserManager.GetUserId
        // would require the manager dependency for no real gain.
        return User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}
