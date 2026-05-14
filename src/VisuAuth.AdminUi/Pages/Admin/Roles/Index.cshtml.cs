using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Capabilities;
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
    IStringLocalizer<AdminSharedResources> localizer) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
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

        var result = await _roleStore.CreateAsync(trimmed, tenantId: null, cancellationToken);

        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.CreateFailed"].Value];
        }
        else
        {
            ActionMessage = _l["Roles.Action.Created", trimmed].Value;
            NewRoleName = null;
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

        var result = await _roleStore.RenameAsync(id, trimmed, cancellationToken);
        if (!result.IsSuccess)
        {
            EditingRoleId = id;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.RenameFailed"].Value];
        }
        else
        {
            ActionMessage = _l["Roles.Action.Renamed", trimmed].Value;
            RenamedRoleName = null;
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

        var result = await _roleStore.DeleteAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["Roles.Error.DeleteFailed"].Value];
        }
        else
        {
            ActionMessage = _l["Roles.Action.Deleted", name].Value;
        }

        await LoadAsync(cancellationToken);
        return Partial("_RolesCatalogue", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Roles = await _roleStore.ListAsync(tenantId: null, cancellationToken);
    }
}
