using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

/// <summary>
/// User detail page. Read-only sections render via <c>_DetailContent</c>;
/// mutations (profile edit, lockout, password reset, 2FA reset, session
/// revocation, role assignment) post to dedicated handlers and refresh the
/// detail content via htmx.
/// </summary>
public sealed class DetailModel(
    IUserStore userStore,
    IRoleStore roleStore,
    IAuditWriter auditWriter,
    IStringLocalizer<AdminSharedResources> localizer) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<AdminSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty(SupportsGet = true, Name = "id")]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public ProfileForm Profile { get; set; } = new();

    public UserDetail Detail { get; private set; } = default!;

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    /// <summary>All roles known to the backend, used to populate the assign-role dropdown.</summary>
    public IReadOnlyList<RoleSummary> AvailableRoles { get; private set; } = [];

    /// <summary>When true, the profile section renders in edit mode.</summary>
    public bool ProfileEditMode { get; private set; }

    /// <summary>Shown once at the top of the page after a successful mutation.</summary>
    public string? ActionMessage { get; private set; }

    /// <summary>One-time temporary password surfaced by <c>ResetPasswordAsync</c>.</summary>
    public string? LastTemporaryPassword { get; private set; }

    /// <summary>Validation / Identity errors from the last mutation attempt.</summary>
    public IReadOnlyList<string> ActionErrors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        => await LoadAndRenderAsync(partialOnHtmx: "_DetailContent", cancellationToken);

    public async Task<IActionResult> OnGetEditProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        ProfileEditMode = true;
        Profile = ProfileForm.From(Detail);
        return Partial("_ProfileSection", this);
    }

    public async Task<IActionResult> OnGetViewProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        return loaded is null ? NotFound() : Partial("_ProfileSection", this);
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        var command = new UpdateUserCommand
        {
            Email = NormalizeOrNull(Profile.Email),
            UserName = NormalizeOrNull(Profile.UserName),
            PhoneNumber = Profile.PhoneNumber ?? string.Empty,
        };

        var result = await _userStore.UpdateAsync(Id, command, cancellationToken);

        if (!result.IsSuccess)
        {
            ProfileEditMode = true;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Users.Action.UpdateFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.UserUpdated,
                TargetType = AuditTargetTypes.User,
                TargetId = Id,
                TargetLabel = Detail.Email,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            // Reload to make sure the form repopulates with the canonical state.
            await LoadDetailAsync(cancellationToken);
            return Partial("_DetailContent", this);
        }

        await LoadDetailAsync(cancellationToken);
        ActionMessage = _l["Users.Action.ProfileUpdated"].Value;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserUpdated,
            TargetType = AuditTargetTypes.User,
            TargetId = Id,
            TargetLabel = Detail.Email,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

        return Partial("_DetailContent", this);
    }

    public Task<IActionResult> OnPostLockAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.SetEnabledAsync(Id, enabled: false, cancellationToken),
            success: _l["Users.Action.AccountLocked"].Value,
            auditAction: AuditActions.UserLocked,
            cancellationToken);

    public Task<IActionResult> OnPostUnlockAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.SetEnabledAsync(Id, enabled: true, cancellationToken),
            success: _l["Users.Action.AccountUnlocked"].Value,
            auditAction: AuditActions.UserUnlocked,
            cancellationToken);

    public Task<IActionResult> OnPostResetPasswordAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.ResetPasswordAsync(Id, cancellationToken),
            success: _l["Users.Action.TempPasswordGenerated"].Value,
            auditAction: AuditActions.UserPasswordResetByAdmin,
            cancellationToken,
            onSuccess: r =>
            {
                if (r.Metadata.TryGetValue("temporaryPassword", out var pwd))
                {
                    LastTemporaryPassword = pwd;
                }
            });

    public Task<IActionResult> OnPostResetTwoFactorAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.ResetTwoFactorAsync(Id, cancellationToken),
            success: _l["Users.Action.TwoFactorReset"].Value,
            auditAction: AuditActions.UserTwoFactorResetByAdmin,
            cancellationToken);

    public Task<IActionResult> OnPostRevokeSessionsAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.RevokeSessionsAsync(Id, cancellationToken),
            success: _l["Users.Action.SessionsRevoked"].Value,
            auditAction: AuditActions.UserSessionsRevokedByAdmin,
            cancellationToken);

    public Task<IActionResult> OnPostAssignRoleAsync(string? roleName, CancellationToken cancellationToken)
    {
        var trimmed = roleName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return InvalidRoleAsync(_l["Users.Action.PickRoleToAssign"].Value, cancellationToken);
        }
        return ExecuteAsync(
            () => _roleStore.AssignRoleAsync(Id, trimmed, cancellationToken),
            success: _l["Users.Action.RoleAssigned", trimmed].Value,
            auditAction: AuditActions.RoleAssignedToUser,
            cancellationToken,
            auditPayload: new Dictionary<string, string?> { ["role"] = trimmed });
    }

    public Task<IActionResult> OnPostRemoveRoleAsync(string? roleName, CancellationToken cancellationToken)
    {
        var trimmed = roleName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return InvalidRoleAsync(_l["Users.Action.MissingRoleName"].Value, cancellationToken);
        }
        return ExecuteAsync(
            () => _roleStore.RemoveRoleAsync(Id, trimmed, cancellationToken),
            success: _l["Users.Action.RoleRemoved", trimmed].Value,
            auditAction: AuditActions.RoleRemovedFromUser,
            cancellationToken,
            auditPayload: new Dictionary<string, string?> { ["role"] = trimmed });
    }

    private async Task<IActionResult> InvalidRoleAsync(string message, CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }
        ActionErrors = [message];
        return Partial("_DetailContent", this);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        // Snapshot the email BEFORE delete so the audit row carries a
        // human-readable label even though the user no longer exists.
        var preDelete = await _userStore.GetDetailAsync(Id, cancellationToken);
        var snapshottedEmail = preDelete?.Email;

        var result = await _userStore.DeleteAsync(Id, cancellationToken);

        // Failed delete keeps the admin on the detail page so they can read
        // the error. Successful delete sends them back to the list — the user
        // is gone, so there's nothing to render on the detail route any more.
        if (!result.IsSuccess)
        {
            var loaded = await LoadDetailAsync(cancellationToken);
            if (loaded is null)
            {
                return NotFound();
            }

            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Users.Action.DeleteFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.UserDeleted,
                TargetType = AuditTargetTypes.User,
                TargetId = Id,
                TargetLabel = snapshottedEmail,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            return Partial("_DetailContent", this);
        }

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserDeleted,
            TargetType = AuditTargetTypes.User,
            TargetId = Id,
            TargetLabel = snapshottedEmail,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

        return Redirect("/visuauth/admin/users");
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task<UserResult>> action,
        string success,
        string auditAction,
        CancellationToken cancellationToken,
        Action<UserResult>? onSuccess = null,
        IReadOnlyDictionary<string, string?>? auditPayload = null)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        var result = await action();

        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Users.Action.Generic"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = auditAction,
                TargetType = AuditTargetTypes.User,
                TargetId = Id,
                TargetLabel = Detail.Email,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
                Payload = auditPayload,
            }, cancellationToken);
        }
        else
        {
            ActionMessage = success;
            onSuccess?.Invoke(result);

            await _audit.WriteAsync(new AuditEvent
            {
                Action = auditAction,
                TargetType = AuditTargetTypes.User,
                TargetId = Id,
                TargetLabel = Detail.Email,
                Outcome = AuditOutcome.Success,
                Payload = auditPayload,
            }, cancellationToken);
        }

        // Always reload — security stamp, lockout end, 2FA flag, etc. may have moved.
        await LoadDetailAsync(cancellationToken);
        return Partial("_DetailContent", this);
    }

    private async Task<IActionResult> LoadAndRenderAsync(string partialOnHtmx, CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        Profile = ProfileForm.From(Detail);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial(partialOnHtmx, this);
        }

        return Page();
    }

    private async Task<UserDetail?> LoadDetailAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return null;
        }

        var detail = await _userStore.GetDetailAsync(Id, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        Detail = detail;

        // Role catalogue powers the assign-role dropdown. Capability-gated so
        // adapters that opt out of role management do not pay the round-trip.
        if (Capabilities.SupportsRoleManagement)
        {
            AvailableRoles = await _roleStore.ListAsync(detail.TenantId, cancellationToken);
        }

        return detail;
    }

    private static string? NormalizeOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Form posted by the profile edit panel.</summary>
    public sealed class ProfileForm
    {
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }

        public static ProfileForm From(UserDetail detail) => new()
        {
            Email = detail.Email,
            UserName = detail.UserName,
            PhoneNumber = detail.PhoneNumber,
        };
    }
}
