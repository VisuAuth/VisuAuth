using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

public sealed class IndexModel(IUserStore userStore, IRoleStore roleStore) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly IRoleStore _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true, Name = "role")]
    public string? RoleFilter { get; set; }

    /// <summary>"active", "locked", or empty (any).</summary>
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? StatusFilter { get; set; }

    /// <summary>"true", "false", or empty (any).</summary>
    [BindProperty(SupportsGet = true, Name = "verified")]
    public string? VerifiedFilter { get; set; }

    /// <summary>"true", "false", or empty (any).</summary>
    [BindProperty(SupportsGet = true, Name = "twofa")]
    public string? TwoFactorFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; private set; } = 25;

    public PagedResult<UserSummary> Result { get; private set; } = PagedResult<UserSummary>.Empty();

    /// <summary>Role catalogue used to populate the filter dropdown.</summary>
    public IReadOnlyList<RoleSummary> AvailableRoles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var filter = new UserFilter
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
            Role = string.IsNullOrWhiteSpace(RoleFilter) ? null : RoleFilter.Trim(),
            IsLockedOut = ParseLockedOut(StatusFilter),
            EmailConfirmed = ParseBool(VerifiedFilter),
            TwoFactorEnabled = ParseBool(TwoFactorFilter),
            Page = PageNumber < 1 ? 1 : PageNumber,
            PageSize = PageSize,
            SortBy = UserSortBy.Email,
            Descending = false,
        };

        Result = await _userStore.ListAsync(filter, cancellationToken);

        if (_userStore.Capabilities.SupportsRoleManagement)
        {
            AvailableRoles = await _roleStore.ListAsync(tenantId: null, cancellationToken);
        }

        // When htmx requests the page, only render the table partial — saves
        // bandwidth and keeps the sidebar/layout stable.
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial("_UsersTable", this);
        }

        return Page();
    }

    private static bool? ParseLockedOut(string? value) => value switch
    {
        "locked" => true,
        "active" => false,
        _ => null,
    };

    private static bool? ParseBool(string? value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}
