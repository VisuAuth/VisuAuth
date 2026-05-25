using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Auditing;
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
    IOptions<EndUserUiOptions> options,
    IAuditWriter auditWriter,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly EndUserUiOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

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
            ModelState.AddModelError(string.Empty, _l["Forgot.Error.NotSupported"].Value);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.Email))
        {
            ModelState.AddModelError(string.Empty, _l["Forgot.Error.EmailRequired"].Value);
            return Page();
        }

        var email = Form.Email.Trim();
        var result = await _authentication.RequestPasswordResetAsync(email, cancellationToken);

        Submitted = true;

        // Audit even when result.IsSuccess is unset for unknown emails —
        // a failed reset request still signals a user trying to recover
        // access; it's useful in the audit log even with no target id.
        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.PasswordResetRequested,
            TargetType = AuditTargetTypes.User,
            TargetLabel = email,
            Outcome = result.IsSuccess ? AuditOutcome.Success : AuditOutcome.Failure,
            FailureReason = result.IsSuccess ? null : result.Error,
        }, cancellationToken);

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
