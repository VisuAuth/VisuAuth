using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Roles;

/// <summary>
/// Roles catalogue page. Lists every role known to the backend with member
/// counts and supports inline create / delete via htmx swaps. Rename is
/// deferred to a follow-up PR — needs a view↔edit toggle that would bloat
/// this change.
/// </summary>
public sealed class IndexModel(
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
    public string? NewRoleName { get; set; }

    [BindProperty]
    public string? RenamedRoleName { get; set; }

    public IReadOnlyList<RoleSummary> Roles { get; private set; } = [];

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    public string? ActionMessage { get; private set; }

    public IReadOnlyList<string> ActionErrors { get; private set; } = [];

    /// <summary>When set, the row with this id renders inline as a rename form.</summary>
    public string? EditingRoleId { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial("_RolesCatalogue", this);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var trimmed = NewRoleName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ActionErrors = [_l["Roles.Error.NameRequired"].Value];
            await LoadAsync(cancellationToken);
            return Partial("_RolesCatalogue", this);
        }

        StoreResult result;
        try
        {
            result = await _roleStore.CreateAsync(trimmed, tenantId: null, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // Defence in depth: the UI hides the create form when
            // Capabilities.SupportsRoleMutation is false, but a stale form
            // or a direct POST can still reach here. Surface the friendly
            // message instead of letting the exception 500 the htmx swap.
            return await RoleMutationNotSupportedAsync(cancellationToken);
        }

        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.CreateFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleCreated,
                TargetType = AuditTargetTypes.Role,
                TargetLabel = trimmed,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);
        }
        else
        {
            ActionMessage = _l["Roles.Action.Created", trimmed].Value;
            NewRoleName = null;

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleCreated,
                TargetType = AuditTargetTypes.Role,
                TargetId = trimmed,
                TargetLabel = trimmed,
                Outcome = AuditOutcome.Success,
            }, cancellationToken);
        }

        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    public async Task<IActionResult> OnGetEditRoleAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = [_l["Roles.Error.MissingId"].Value];
        }
        else
        {
            EditingRoleId = id;
        }
        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    public async Task<IActionResult> OnPostRenameAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = [_l["Roles.Error.MissingId"].Value];
            await LoadAsync(cancellationToken);
            return Partial("_RolesCatalogue", this);
        }

        var trimmed = RenamedRoleName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            // Keep the row in edit mode so the admin can fix the input rather
            // than losing their place.
            EditingRoleId = id;
            ActionErrors = [_l["Roles.Error.NameRequired"].Value];
            await LoadAsync(cancellationToken);
            return Partial("_RolesCatalogue", this);
        }

        var preRename = await _roleStore.GetAsync(id, cancellationToken);
        var oldName = preRename?.Name ?? id;

        StoreResult result;
        try
        {
            result = await _roleStore.RenameAsync(id, trimmed, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return await RoleMutationNotSupportedAsync(cancellationToken);
        }
        if (!result.IsSuccess)
        {
            EditingRoleId = id;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.RenameFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleRenamed,
                TargetType = AuditTargetTypes.Role,
                TargetId = id,
                TargetLabel = oldName,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
                Payload = new Dictionary<string, string?> { ["newName"] = trimmed },
            }, cancellationToken);
        }
        else
        {
            ActionMessage = _l["Roles.Action.Renamed", trimmed].Value;
            RenamedRoleName = null;

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleRenamed,
                TargetType = AuditTargetTypes.Role,
                TargetId = id,
                TargetLabel = trimmed,
                Outcome = AuditOutcome.Success,
                Payload = new Dictionary<string, string?>
                {
                    ["from"] = oldName,
                    ["to"] = trimmed,
                },
            }, cancellationToken);
        }

        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = [_l["Roles.Error.MissingId"].Value];
            await LoadAsync(cancellationToken);
            return Partial("_RolesCatalogue", this);
        }

        var role = await _roleStore.GetAsync(id, cancellationToken);
        var name = role?.Name ?? id;

        StoreResult result;
        try
        {
            result = await _roleStore.DeleteAsync(id, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return await RoleMutationNotSupportedAsync(cancellationToken);
        }
        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.DeleteFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleDeleted,
                TargetType = AuditTargetTypes.Role,
                TargetId = id,
                TargetLabel = name,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);
        }
        else
        {
            ActionMessage = _l["Roles.Action.Deleted", name].Value;

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.RoleDeleted,
                TargetType = AuditTargetTypes.Role,
                TargetId = id,
                TargetLabel = name,
                Outcome = AuditOutcome.Success,
            }, cancellationToken);
        }

        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    /// <summary>
    /// Shared response for the three mutating handlers when the backend
    /// declares its roles externally (Microsoft Entra app roles live in
    /// the application manifest). Surfaces the friendly explanation and
    /// re-renders the catalogue partial instead of letting the
    /// <see cref="NotSupportedException"/> the Graph adapters throw bubble
    /// up as a 500.
    /// </summary>
    private async Task<IActionResult> RoleMutationNotSupportedAsync(CancellationToken cancellationToken)
    {
        ActionErrors = [_l["Roles.Error.MutationNotSupported"].Value];
        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Roles = await _roleStore.ListAsync(tenantId: null, cancellationToken);
    }
}
