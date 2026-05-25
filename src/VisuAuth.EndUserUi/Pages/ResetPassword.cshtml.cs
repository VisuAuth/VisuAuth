using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Reset-password page. GET carries the email + token from the link; POST
/// applies the new password.
/// </summary>
public sealed class ResetPasswordModel(
    IAuthenticationFlow authentication,
    IAuditWriter auditWriter,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

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
            Errors = [_l["Reset.Error.MissingLink"].Value];
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsPasswordReset)
        {
            Errors = [_l["Reset.Error.NotSupported"].Value];
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            Errors = [_l["Reset.Error.MissingEmailToken"].Value];
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.NewPassword) ||
            string.IsNullOrWhiteSpace(Form.ConfirmPassword))
        {
            Errors = [_l["Reset.Error.BothRequired"].Value];
            return Page();
        }

        if (!string.Equals(Form.NewPassword, Form.ConfirmPassword, StringComparison.Ordinal))
        {
            Errors = [_l["Reset.Error.PasswordMismatch"].Value];
            return Page();
        }

        var email = Email.Trim();
        var result = await _authentication.ResetPasswordAsync(
            email,
            Token,
            Form.NewPassword,
            cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Reset.Error.ResetFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.PasswordResetCompleted,
                TargetType = AuditTargetTypes.User,
                TargetLabel = email,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            return Page();
        }

        Succeeded = true;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.PasswordResetCompleted,
            TargetType = AuditTargetTypes.User,
            TargetLabel = email,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

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
