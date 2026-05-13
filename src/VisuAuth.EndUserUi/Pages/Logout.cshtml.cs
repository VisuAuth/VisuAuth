using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Sign-out endpoint. POST-only — GET would let a third-party page log
/// the user out via a crafted image / link, which is a known CSRF vector.
/// </summary>
public sealed class LogoutModel(IAuthenticationFlow authentication) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    /// <summary>GET shows a minimal confirmation page with a POST form.</summary>
    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await _authentication.SignOutAsync(cancellationToken);
        return Redirect(SanitiseReturnUrl(ReturnUrl));
    }

    private string SanitiseReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }
        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }
}
