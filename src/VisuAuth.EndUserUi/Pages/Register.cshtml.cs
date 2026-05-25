using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Self-service registration. Capability-gated: only renders when
/// <see cref="UserBackendCapabilities.SupportsRegistration"/> is true,
/// otherwise shows an "external provider only" message.
/// </summary>
public sealed class RegisterModel(
    IAuthenticationFlow authentication,
    ITenantContext tenantContext,
    IOptions<EndUserUiOptions> options,
    IAuditWriter auditWriter,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly EndUserUiOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public RegisterForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _authentication.Capabilities;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? CreatedUserId { get; private set; }

    /// <summary>
    /// Confirmation link surfaced in development mode after a successful
    /// register. Null in production — consumers wire their own
    /// `IEmailSender` to deliver this.
    /// </summary>
    public string? ConfirmationLink { get; private set; }

    public IActionResult OnGet()
    {
        if (!Capabilities.SupportsRegistration)
        {
            Errors = [_l["Register.Error.NotSupported"].Value];
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsRegistration)
        {
            Errors = [_l["Register.Error.NotSupported"].Value];
            return Page();
        }

        if (!ModelState.IsValid ||
            string.IsNullOrWhiteSpace(Form.Email) ||
            string.IsNullOrWhiteSpace(Form.Password))
        {
            Errors = [_l["Register.Error.EmailPasswordRequired"].Value];
            return Page();
        }

        if (!string.Equals(Form.Password, Form.ConfirmPassword, StringComparison.Ordinal))
        {
            Errors = [_l["Register.Error.PasswordMismatch"].Value];
            return Page();
        }

        // Auto-attach to the current tenant scope when multi-tenancy is on.
        // The interceptor also handles this for any code path that flows
        // through SaveChanges, but we set it explicitly here so the value
        // sticks even when the request is somehow scope-less.
        var tenantId = _tenantContext.IsMultiTenancyEnabled
            ? _tenantContext.CurrentTenantId
            : null;

        var attemptedEmail = Form.Email.Trim();
        var result = await _authentication.RegisterAsync(
            attemptedEmail,
            Form.Password,
            tenantId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Register.Error.RegisterFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.UserRegistered,
                TargetType = AuditTargetTypes.User,
                TargetLabel = attemptedEmail,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            return Page();
        }

        CreatedUserId = result.UserId;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserRegistered,
            TargetType = AuditTargetTypes.User,
            TargetId = result.UserId,
            TargetLabel = attemptedEmail,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

        // Development mode surfaces a clickable confirmation link so the
        // sample app (and any local dev setup without an email sender) can
        // exercise the flow end to end. Production deployments leave dev
        // mode off and rely on a real `IEmailSender` to deliver this link
        // out-of-band — never inline.
        if (_options.DevelopmentMode &&
            Capabilities.SupportsEmailConfirmation &&
            CreatedUserId is { Length: > 0 } userId &&
            result.Metadata.TryGetValue("emailConfirmationToken", out var confirmationToken) &&
            !string.IsNullOrEmpty(confirmationToken))
        {
            var encodedUserId = Uri.EscapeDataString(userId);
            var encodedToken = Uri.EscapeDataString(confirmationToken);
            ConfirmationLink = $"/visuauth/confirm-email?userId={encodedUserId}&token={encodedToken}";
        }

        // Clear the form so the success view does not re-populate it.
        Form = new RegisterForm();
        return Page();
    }

    public sealed class RegisterForm
    {
        public string? Email { get; set; }

        public string? Password { get; set; }

        public string? ConfirmPassword { get; set; }
    }
}
