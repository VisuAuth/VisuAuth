using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Pages.ExternalLogin;

/// <summary>
/// POST-only initiator for an external sign-in. Builds an ASP.NET Core
/// <see cref="ChallengeResult"/> that hands control to the OAuth handler
/// for <c>scheme</c> (Google, Microsoft, Apple, …); the handler redirects
/// the user to the provider, and the provider posts back to
/// <c>/visuauth/external-login/callback</c> via the redirect URL we set
/// here.
/// </summary>
/// <remarks>
/// GET is intentionally not implemented — this is purely a redirect
/// endpoint and a GET would let third-party pages trigger the OAuth flow
/// via crafted <c>&lt;img&gt;</c> / link, which is the same CSRF vector
/// that prevents <c>/visuauth/logout</c> from accepting GET.
/// </remarks>
public sealed class StartModel(IExternalLoginFlow externalLogin) : PageModel
{
    private readonly IExternalLoginFlow _externalLogin = externalLogin ?? throw new ArgumentNullException(nameof(externalLogin));

    public async Task<IActionResult> OnPostAsync(
        [FromForm] string? scheme,
        [FromForm(Name = "returnUrl")] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!_externalLogin.Capabilities.SupportsExternalProviders || string.IsNullOrWhiteSpace(scheme))
        {
            return RedirectToLogin(returnUrl);
        }

        // Defend against a tampered hidden field — only schemes that
        // actually have a registered handler can be challenged. Without
        // this, a malicious POST could probe for installed providers.
        var providers = await _externalLogin.GetProvidersAsync(cancellationToken);
        if (!providers.Any(p => string.Equals(p.Scheme, scheme, StringComparison.Ordinal)))
        {
            return RedirectToLogin(returnUrl);
        }

        // The OAuth handler re-enters our pipeline at /signin-{scheme} (its
        // own callback path), then redirects to RedirectUri once it has
        // signed the user into the external cookie. RedirectUri carries the
        // returnUrl through so the callback page can pass it on after the
        // strategy resolves.
        var redirectUri = Url.Page(
            "/ExternalLogin/Callback",
            pageHandler: null,
            values: new { returnUrl });
        var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = redirectUri,
        };
        // Stash scheme + returnUrl in the auth properties so the callback can
        // reconstruct context even if the redirect URL is dropped (e.g. by
        // a strict CSP). Identity uses the same convention.
        properties.Items["LoginProvider"] = scheme;
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            properties.Items["returnUrl"] = returnUrl;
        }

        return Challenge(properties, scheme);
    }

    private RedirectResult RedirectToLogin(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect($"/visuauth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
        return Redirect("/visuauth/login");
    }
}
