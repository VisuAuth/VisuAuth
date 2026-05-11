using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

/// <summary>
/// Read-only user detail page. Mutations (lockout, password reset, 2FA reset,
/// session revocation, etc.) ship in the next PR — the buttons consult the
/// backend's <see cref="UserBackendCapabilities"/> to decide what to render.
/// </summary>
public sealed class DetailModel(IUserStore userStore) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));

    [BindProperty(SupportsGet = true, Name = "id")]
    public string Id { get; set; } = string.Empty;

    public UserDetail Detail { get; private set; } = default!;

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        var detail = await _userStore.GetDetailAsync(Id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        return Page();
    }
}
