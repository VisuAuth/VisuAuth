using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Email confirmation landing page. The link the user receives in their
/// inbox points here with <c>userId</c> + <c>token</c> query params; on
/// GET we attempt the confirmation immediately and show the result.
/// </summary>
public sealed class ConfirmEmailModel(IAuthenticationFlow authentication) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));

    [BindProperty(SupportsGet = true, Name = "userId")]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true, Name = "token")]
    public string? Token { get; set; }

    public bool Succeeded { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(Token))
        {
            Errors = ["This link is missing the user id or token. Open the link from your email."];
            return Page();
        }

        var result = await _authentication.ConfirmEmailAsync(UserId, Token, cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to confirm email."];
        }
        else
        {
            Succeeded = true;
        }

        return Page();
    }
}
