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
    IStringLocalizer<AdminSharedResources> localizer,
    IEmailDomainSource? emailDomainSource = null) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<AdminSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    // Optional: only Entra-style adapters register an IEmailDomainSource.
    // A null source keeps the existing single-suffix / free-text UX, so the
    // ASP.NET Identity adapter (and any consumer that never wires one) is
    // unaffected by the multi-domain dropdown.
    private readonly IEmailDomainSource? _emailDomainSource = emailDomainSource;

    [BindProperty]
    public CreateUserForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    /// <summary>All roles known to the backend, used to populate the role checkbox list.</summary>
    public IReadOnlyList<RoleSummary> AvailableRoles { get; private set; } = [];

    /// <summary>
    /// Verified email domains the admin can pick from. Populated only when an
    /// <see cref="IEmailDomainSource"/> is registered and the tenant exposes
    /// two or more domains; otherwise empty and the form falls back to the
    /// single locked suffix (<see cref="UserBackendCapabilities.EmailDomainSuffix"/>)
    /// or free-text entry.
    /// </summary>
    public IReadOnlyList<string> EmailDomainChoices { get; private set; } = [];

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
        await LoadEmailDomainsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsRegistration)
        {
            Errors = [_l["Users.Error.RegistrationNotSupported"].Value];
            await LoadRolesAsync(cancellationToken);
            await LoadEmailDomainsAsync(cancellationToken);
            return Page();
        }

        if (!ModelState.IsValid)
        {
            Errors = ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
                .ToList();
            await LoadRolesAsync(cancellationToken);
            await LoadEmailDomainsAsync(cancellationToken);
            return Page();
        }

        await LoadEmailDomainsAsync(cancellationToken);

        var resolvedEmail = ResolveEmail();

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

        CreatedUserId = result.ResourceId;
        if (result.Metadata.TryGetValue("temporaryPassword", out var temp))
        {
            GeneratedPassword = temp;
        }

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserCreated,
            TargetType = AuditTargetTypes.User,
            TargetId = result.ResourceId,
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

    // Surfaces the tenant's verified domains for the dropdown. Only meaningful
    // with 2+ domains — a single-domain tenant keeps the locked-suffix UX, and
    // a missing source (the ASP.NET Identity adapter) leaves the list empty.
    private async Task LoadEmailDomainsAsync(CancellationToken cancellationToken)
    {
        if (_emailDomainSource is null)
        {
            return;
        }

        var domains = await _emailDomainSource.GetEmailDomainsAsync(cancellationToken);
        EmailDomainChoices = domains.Count >= 2 ? domains : [];
    }

    // Combines the editable local part with the chosen domain. Precedence:
    //   1. A multi-domain dropdown selection (validated against EmailDomainChoices
    //      so a tampered POST can't inject an arbitrary domain).
    //   2. The single locked EmailDomainSuffix capability.
    //   3. Free text exactly as typed (already contains '@', or no suffix at all).
    private string ResolveEmail()
    {
        var rawEmail = Form.Email?.Trim() ?? string.Empty;

        // A value already containing '@' is a full address — never re-append.
        if (rawEmail.Contains('@', StringComparison.Ordinal))
        {
            return rawEmail;
        }

        var chosenDomain = Form.EmailDomain?.Trim();
        if (!string.IsNullOrEmpty(chosenDomain) &&
            EmailDomainChoices.Contains(chosenDomain, StringComparer.OrdinalIgnoreCase))
        {
            return rawEmail + "@" + chosenDomain;
        }

        return Capabilities.EmailDomainSuffix is { Length: > 0 } suffix
            ? rawEmail + suffix
            : rawEmail;
    }

    public sealed class CreateUserForm
    {
        public string? Email { get; set; }

        /// <summary>
        /// Verified domain picked from the multi-domain dropdown (without the
        /// leading <c>@</c>). Ignored when the tenant exposes a single domain
        /// or none. Validated against the rendered choices server-side.
        /// </summary>
        public string? EmailDomain { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; } = true;

        /// <summary>Role names the admin ticked on the create form.</summary>
        public IList<string> SelectedRoles { get; set; } = [];
    }
}
