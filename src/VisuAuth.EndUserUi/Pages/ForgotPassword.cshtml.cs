using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Forgot-password request page. Always responds with the same generic
/// "we'll email you instructions" message so an attacker cannot probe
/// whether an email is registered. Development mode also surfaces the
/// reset URL inline for the sample app's manual test flow.
/// </summary>
public sealed class ForgotPasswordModel(
    IAuthenticationFlow authentication,
    IOptions<EndUserUiOptions> options) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly EndUserUiOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    [BindProperty]
    public ForgotPasswordForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _authentication.Capabilities;

    public bool Submitted { get; private set; }

    /// <summary>Reset URL surfaced inline in development mode. Null otherwise.</summary>
    public string? ResetLink { get; private set; }

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsPasswordReset)
        {
            ModelState.AddModelError(string.Empty, "This backend does not support password reset.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.Email))
        {
            ModelState.AddModelError(string.Empty, "Email is required.");
            return Page();
        }

        var email = Form.Email.Trim();
        var result = await _authentication.RequestPasswordResetAsync(email, cancellationToken);

        Submitted = true;

        // Surface the link only in dev mode, and only when the store
        // actually returned a token — unknown emails get `IsSuccess = true`
        // but no token (anti-enumeration).
        if (_options.DevelopmentMode &&
            result.IsSuccess &&
            result.Metadata.TryGetValue("resetToken", out var token) &&
            !string.IsNullOrEmpty(token))
        {
            var encodedEmail = Uri.EscapeDataString(email);
            var encodedToken = Uri.EscapeDataString(token);
            ResetLink = $"/visuauth/reset-password?email={encodedEmail}&token={encodedToken}";
        }

        return Page();
    }

    public sealed class ForgotPasswordForm
    {
        public string? Email { get; set; }
    }
}
