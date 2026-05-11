using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

public sealed class IndexModel(IUserStore userStore) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; private set; } = 25;

    public PagedResult<UserSummary> Result { get; private set; } = PagedResult<UserSummary>.Empty();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var filter = new UserFilter
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
            Page = PageNumber < 1 ? 1 : PageNumber,
            PageSize = PageSize,
            SortBy = UserSortBy.Email,
            Descending = false,
        };

        Result = await _userStore.ListAsync(filter, cancellationToken);

        // When htmx requests the page, only render the table partial — saves
        // bandwidth and keeps the sidebar/layout stable.
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial("_UsersTable", this);
        }

        return Page();
    }
}
