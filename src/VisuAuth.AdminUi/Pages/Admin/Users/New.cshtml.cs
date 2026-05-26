using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

/// <summary>
/// Create-user form. On success the admin lands on the new user's detail page
/// so they can immediately follow up with role assignment, lockout, etc.
/// </summary>
public sealed class NewModel(
    IUserStore userStore,
    IRoleStore roleStore,
    IAuditWriter auditWriter,
    IStringLocalizer<AdminSharedResources> localizer) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<AdminSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public CreateUserForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    /// <summary>All roles known to the backend, used to populate the role checkbox list.</summary>
    public IReadOnlyList<RoleSummary> AvailableRoles { get; private set; } = [];

    /// <summary>Validation / business errors from the most recent submission.</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>Temporary password surfaced when the admin leaves the password field blank.</summary>
    public string? GeneratedPassword { get; private set; }

    /// <summary>Newly created user id. Drives the post-create banner with the link to detail.</summary>
    public string? CreatedUserId { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Surfacing "this backend does not support registration" early lets the
        // admin see why the form is unavailable without first attempting a POST.
        if (!Capabilities.SupportsRegistration)
        {
            Errors = [_l["Users.Error.RegistrationNotSupported"].Value];
        }

        await LoadRolesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsRegistration)
        {
            Errors = [_l["Users.Error.RegistrationNotSupported"].Value];
            await LoadRolesAsync(cancellationToken);
            return Page();
        }

        if (!ModelState.IsValid)
        {
            Errors = ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
                .ToList();
            await LoadRolesAsync(cancellationToken);
            return Page();
        }

        // When the backend declares an EmailDomainSuffix the form renders a
        // locked-suffix input (only the local part is editable). The browser
        // posts back just the local part, so we re-append the suffix here
        // before handing the command to the store. A power-user / API
        // caller who passes a full email already containing @ bypasses
        // this branch — useful for the rare multi-domain tenant where the
        // operator wants a different verified domain than the default.
        var rawEmail = Form.Email?.Trim() ?? string.Empty;
        var resolvedEmail = Capabilities.EmailDomainSuffix is { Length: > 0 } suffix && !rawEmail.Contains('@', StringComparison.Ordinal)
            ? rawEmail + suffix
            : rawEmail;

        var command = new CreateUserCommand
        {
            Email = resolvedEmail,
            UserName = string.IsNullOrWhiteSpace(Form.UserName) ? null : Form.UserName.Trim(),
            Password = string.IsNullOrEmpty(Form.Password) ? null : Form.Password,
            PhoneNumber = string.IsNullOrWhiteSpace(Form.PhoneNumber) ? null : Form.PhoneNumber.Trim(),
            EmailConfirmed = Form.EmailConfirmed,
        };

        var result = await _userStore.CreateAsync(command, cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Users.Error.CreateFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.UserCreated,
                TargetType = AuditTargetTypes.User,
                TargetLabel = command.Email,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            await LoadRolesAsync(cancellationToken);
            return Page();
        }

        CreatedUserId = result.UserId;
        if (result.Metadata.TryGetValue("temporaryPassword", out var temp))
        {
            GeneratedPassword = temp;
        }

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserCreated,
            TargetType = AuditTargetTypes.User,
            TargetId = result.UserId,
            TargetLabel = command.Email,
            Outcome = AuditOutcome.Success,
            Payload = new Dictionary<string, string?>
            {
                ["emailConfirmed"] = command.EmailConfirmed ? "true" : "false",
                ["temporaryPasswordGenerated"] = GeneratedPassword is not null ? "true" : "false",
            },
        }, cancellationToken);

        // Assign roles after the user lands. A failure here leaves the user
        // in place — better to surface a partial-success message than to roll
        // back, since the admin can fix roles from the detail page.
        if (CreatedUserId is { Length: > 0 } id && Form.SelectedRoles.Count > 0 && Capabilities.SupportsRoleManagement)
        {
            var roleErrors = new List<string>();
            foreach (var role in Form.SelectedRoles)
            {
                if (string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }
                var assign = await _roleStore.AssignRoleAsync(id, role, cancellationToken);
                if (!assign.IsSuccess)
                {
                    roleErrors.Add(assign.Error ?? _l["Users.Action.FailedAssignRole", role].Value);

                    await _audit.WriteAsync(new AuditEvent
                    {
                        Action = AuditActions.RoleAssignedToUser,
                        TargetType = AuditTargetTypes.User,
                        TargetId = id,
                        TargetLabel = command.Email,
                        Outcome = AuditOutcome.Failure,
                        FailureReason = assign.Error,
                        Payload = new Dictionary<string, string?> { ["role"] = role },
                    }, cancellationToken);
                }
                else
                {
                    await _audit.WriteAsync(new AuditEvent
                    {
                        Action = AuditActions.RoleAssignedToUser,
                        TargetType = AuditTargetTypes.User,
                        TargetId = id,
                        TargetLabel = command.Email,
                        Outcome = AuditOutcome.Success,
                        Payload = new Dictionary<string, string?> { ["role"] = role },
                    }, cancellationToken);
                }
            }
            if (roleErrors.Count > 0)
            {
                Errors = roleErrors;
            }
        }

        // When a temporary password was generated, keep the admin on this page
        // so they can copy the password before navigating away. The page renders
        // the temp password panel plus a link to the detail. When the admin
        // supplied a password and there are no role errors, redirect straight
        // to detail.
        if (GeneratedPassword is null && Errors.Count == 0 && CreatedUserId is not null)
        {
            return Redirect($"/visuauth/admin/users/{CreatedUserId}");
        }

        // Clear the form so the success view does not re-populate it.
        Form = new CreateUserForm();
        await LoadRolesAsync(cancellationToken);
        return Page();
    }

    private async Task LoadRolesAsync(CancellationToken cancellationToken)
    {
        if (Capabilities.SupportsRoleManagement)
        {
            AvailableRoles = await _roleStore.ListAsync(tenantId: null, cancellationToken);
        }
    }

    public sealed class CreateUserForm
    {
        public string? Email { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; } = true;

        /// <summary>Role names the admin ticked on the create form.</summary>
        public IList<string> SelectedRoles { get; set; } = [];
    }
}
