using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Sign-out endpoint. POST-only — GET would let a third-party page log
/// the user out via a crafted image / link, which is a known CSRF vector.
/// </summary>
public sealed class LogoutModel(
    IAuthenticationFlow authentication,
    IAuditWriter auditWriter) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    /// <summary>GET shows a minimal confirmation page with a POST form.</summary>
    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Snapshot the actor identity BEFORE sign-out — once SignOutAsync
        // runs, HttpContext.User reverts to anonymous and the audit writer
        // would record an unauthenticated actor.
        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue(ClaimTypes.Name);

        await _authentication.SignOutAsync(cancellationToken);

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.LogoutSucceeded,
            TargetType = AuditTargetTypes.User,
            TargetId = userId,
            TargetLabel = email,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

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
