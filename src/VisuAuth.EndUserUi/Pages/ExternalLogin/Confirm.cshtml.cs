using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.EndUserUi.Pages.ExternalLogin;

/// <summary>
/// Confirmation form for the <c>AutoLinkByEmailOrConfirm</c> and
/// <c>AlwaysConfirm</c> first-time strategies. Lets the user edit the
/// email pre-filled from the provider's claims (or supply one if the
/// provider returned none) before the account is created or linked.
/// </summary>
/// <remarks>
/// Pending claims (provider name, email, display name) are read directly
/// from the external cookie via <see cref="IExternalLoginFlow.GetPendingInfoAsync"/>
/// — TempData was too fragile across the GET → form-submit POST round
/// trip, especially under cross-site cookie rules after an OAuth redirect.
/// The <c>returnUrl</c> rides along as a query string parameter on GET and
/// as a hidden input on POST.
/// </remarks>
public sealed class ConfirmModel(
    IExternalLoginFlow externalLogin,
    ITenantContext tenantContext,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IExternalLoginFlow _externalLogin = externalLogin ?? throw new ArgumentNullException(nameof(externalLogin));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public ConfirmForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    public string? ProviderDisplayName { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var info = await _externalLogin.GetPendingInfoAsync(cancellationToken);
        if (info is null)
        {
            // External cookie expired or never landed — start over.
            return Redirect("/visuauth/login");
        }

        ProviderDisplayName = info.Provider;
        Form.Email = info.Email;
        // Don't pre-fill UserName from the display-name claim: providers
        // typically return "First Last" with spaces, which Identity rejects
        // by default (AllowedUserNameCharacters = letters + digits + -._@+).
        // Empty username = adapter falls back to email, which is always valid.
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var info = await _externalLogin.GetPendingInfoAsync(cancellationToken);
        if (info is null)
        {
            return Redirect("/visuauth/login");
        }
        ProviderDisplayName = info.Provider;

        if (string.IsNullOrWhiteSpace(Form.Email))
        {
            Errors = [_l["ExternalLogin.Confirm.Error.EmailRequired"].Value];
            return Page();
        }

        var tenantId = _tenantContext.IsMultiTenancyEnabled
            ? _tenantContext.CurrentTenantId
            : null;

        var result = await _externalLogin.ConfirmAndCreateAsync(
            Form.Email.Trim(),
            string.IsNullOrWhiteSpace(Form.UserName) ? null : Form.UserName.Trim(),
            tenantId,
            cancellationToken);

        switch (result.Outcome)
        {
            case ExternalSignInOutcome.Success:
                return Redirect(SanitiseLocalReturnUrl(ReturnUrl));

            case ExternalSignInOutcome.NoExternalSession:
                Errors = [_l["ExternalLogin.Error.NoSession"].Value];
                return Page();

            // ExternalSignInOutcome.Failed and any future outcome both
            // surface the generic "we could not finish signing you in".
            default:
                Errors = result.Errors.Count > 0
                    ? result.Errors
                    : [_l["ExternalLogin.Error.SignInFailed"].Value];
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

    public sealed class ConfirmForm
    {
        public string? Email { get; set; }
        public string? UserName { get; set; }
    }
}
