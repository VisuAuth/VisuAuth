using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages.TwoFactor;

/// <summary>
/// Recovery-code management. Lets the user generate / regenerate the
/// 10-code batch (shown once and never persisted in the UI), and offers a
/// "Disable 2FA" button so they can roll the whole feature back from one
/// place.
/// </summary>
/// <remarks>
/// Hits this page right after a successful enrollment via
/// <c>?generated=true</c>; that branch eagerly generates the first batch
/// so the user never leaves without something to fall back on.
/// </remarks>
[Authorize]
public sealed class RecoveryCodesModel(
    ITwoFactorFlow twoFactor,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    /// <summary>How many recovery codes the page produces per generation.</summary>
    public const int CodeCount = 10;

    private readonly ITwoFactorFlow _twoFactor = twoFactor ?? throw new ArgumentNullException(nameof(twoFactor));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    public UserBackendCapabilities Capabilities => _twoFactor.Capabilities;

    public bool IsEnabled { get; private set; }

    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "generated")] bool generated,
        CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            ErrorMessage = _l["TwoFactor.NotSupported"].Value;
            return Page();
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        IsEnabled = await _twoFactor.IsTwoFactorEnabledAsync(userId, cancellationToken);

        // First-visit-after-enable: produce the initial batch so the user
        // never has to think about discovering the "Generate" button.
        if (generated && IsEnabled)
        {
            await PopulateRecoveryCodesAsync(userId, cancellationToken);
            if (RecoveryCodes.Count > 0)
            {
                SuccessMessage = _l["TwoFactor.Recovery.GeneratedNotice"].Value;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            ErrorMessage = _l["TwoFactor.NotSupported"].Value;
            return Page();
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        IsEnabled = await _twoFactor.IsTwoFactorEnabledAsync(userId, cancellationToken);
        if (!IsEnabled)
        {
            ErrorMessage = _l["TwoFactor.Recovery.Error.NotEnabled"].Value;
            return Page();
        }

        await PopulateRecoveryCodesAsync(userId, cancellationToken);
        if (RecoveryCodes.Count == 0 && ErrorMessage is null)
        {
            ErrorMessage = _l["TwoFactor.Recovery.Error.GenerateFailed"].Value;
        }
        else if (RecoveryCodes.Count > 0)
        {
            SuccessMessage = _l["TwoFactor.Recovery.RegeneratedNotice"].Value;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsTwoFactor)
        {
            ErrorMessage = _l["TwoFactor.NotSupported"].Value;
            return Page();
        }

        var userId = ResolveUserId();
        if (userId is null)
        {
            return Challenge();
        }

        var result = await _twoFactor.DisableTwoFactorAsync(userId, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error ?? _l["TwoFactor.Recovery.Error.DisableFailed"].Value;
            IsEnabled = await _twoFactor.IsTwoFactorEnabledAsync(userId, cancellationToken);
            return Page();
        }

        // Land back on /setup so the user sees the QR-pairing flow again
        // and is gently prompted to re-enroll if they ever want it back.
        return Redirect("/visuauth/two-factor/setup");
    }

    private async Task PopulateRecoveryCodesAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await _twoFactor.GenerateRecoveryCodesAsync(userId, CodeCount, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error ?? _l["TwoFactor.Recovery.Error.GenerateFailed"].Value;
            return;
        }

        if (result.Metadata.TryGetValue("recoveryCodes", out var joined) && !string.IsNullOrWhiteSpace(joined))
        {
            RecoveryCodes = joined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    private string? ResolveUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
