using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Public sign-in page. Authenticates via <see cref="IAuthenticationFlow"/>
/// so the page stays adapter-agnostic — the ASP.NET Identity adapter sets
/// the cookie; future Entra adapter would redirect to Microsoft's hosted
/// login instead.
/// </summary>
public sealed class LoginModel(IAuthenticationFlow authentication) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));

    [BindProperty]
    public LoginForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    public UserBackendCapabilities Capabilities => _authentication.Capabilities;

    /// <summary>Banner shown when the previous submission failed.</summary>
    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (!Capabilities.SupportsLocalLogin)
        {
            // Backends without local sign-in (e.g. Entra) still render the
            // page, but with a redirect-style message. A future PR adds the
            // "sign in with Microsoft" button here.
            ErrorMessage = "This backend does not support local sign-in.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsLocalLogin)
        {
            ErrorMessage = "This backend does not support local sign-in.";
            return Page();
        }

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Form.Email) || string.IsNullOrWhiteSpace(Form.Password))
        {
            ErrorMessage = "Email and password are required.";
            return Page();
        }

        var result = await _authentication.SignInWithPasswordAsync(
            Form.Email.Trim(),
            Form.Password,
            Form.RememberMe,
            cancellationToken);

        switch (result.Outcome)
        {
            case SignInOutcome.Success:
                return Redirect(SanitiseReturnUrl(ReturnUrl));

            case SignInOutcome.RequiresTwoFactor:
                // Two-factor challenge page lands with the register/reset PR.
                // For now surface a message so the admin knows what is happening.
                ErrorMessage = "Two-factor authentication is required. The challenge page ships in a follow-up PR.";
                return Page();

            case SignInOutcome.LockedOut:
                ErrorMessage = "This account is locked. Contact an administrator.";
                return Page();

            case SignInOutcome.NotAllowed:
                ErrorMessage = result.Error ?? "Sign-in is not allowed for this account yet (email may need confirmation).";
                return Page();

            case SignInOutcome.RedirectToExternalProvider:
                // External-provider redirect lands with the providers PR.
                ErrorMessage = "External provider sign-in is required for this backend.";
                return Page();

            case SignInOutcome.InvalidCredentials:
            default:
                // Deliberately generic — do not leak whether the email exists.
                ErrorMessage = "Email or password is incorrect.";
                return Page();
        }
    }

    private string SanitiseReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }
        // Only accept local URLs. Prevents open-redirect via a crafted login link.
        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }

    public sealed class LoginForm
    {
        public string? Email { get; set; }

        public string? Password { get; set; }

        public bool RememberMe { get; set; } = true;
    }
}
