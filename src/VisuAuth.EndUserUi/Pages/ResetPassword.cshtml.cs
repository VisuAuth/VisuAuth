using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Reset-password page. GET carries the email + token from the link; POST
/// applies the new password.
/// </summary>
public sealed class ResetPasswordModel(IAuthenticationFlow authentication) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));

    [BindProperty(SupportsGet = true, Name = "email")]
    public string? Email { get; set; }

    [BindProperty(SupportsGet = true, Name = "token")]
    public string? Token { get; set; }

    [BindProperty]
    public ResetPasswordForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _authentication.Capabilities;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public bool Succeeded { get; private set; }

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            Errors = ["This link is missing the email or token. Request a fresh one from the forgot-password page."];
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsPasswordReset)
        {
            Errors = ["This backend does not support password reset."];
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            Errors = ["Missing email or token."];
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.NewPassword) ||
            string.IsNullOrWhiteSpace(Form.ConfirmPassword))
        {
            Errors = ["Both password fields are required."];
            return Page();
        }

        if (!string.Equals(Form.NewPassword, Form.ConfirmPassword, StringComparison.Ordinal))
        {
            Errors = ["Passwords do not match."];
            return Page();
        }

        var result = await _authentication.ResetPasswordAsync(
            Email.Trim(),
            Token,
            Form.NewPassword,
            cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to reset password."];
            return Page();
        }

        Succeeded = true;
        // Clear the form so the success view does not echo the password.
        Form = new ResetPasswordForm();
        return Page();
    }

    public sealed class ResetPasswordForm
    {
        public string? NewPassword { get; set; }

        public string? ConfirmPassword { get; set; }
    }
}
