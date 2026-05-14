using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EndUserUi.Pages;

/// <summary>
/// Public sign-in page. Authenticates via <see cref="IAuthenticationFlow"/>
/// so the page stays adapter-agnostic — the ASP.NET Identity adapter sets
/// the cookie; future Entra adapter would redirect to Microsoft's hosted
/// login instead.
/// </summary>
/// <remarks>
/// Also handles the WebView deep-link flow (CLAUDE.md §9.1 Flow 2): when
/// the configured <see cref="WebViewCallbackOptions.AllowedSchemes"/>
/// recognises the <c>returnUrl</c> scheme, after a successful sign-in the
/// page mints a JWT via <see cref="IJwtIssuer"/> and redirects to the
/// callback URL with the token appended.
/// </remarks>
public sealed class LoginModel(
    IAuthenticationFlow authentication,
    IJwtIssuer jwtIssuer,
    IOptions<WebViewCallbackOptions> webViewOptions,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly IJwtIssuer _jwtIssuer = jwtIssuer ?? throw new ArgumentNullException(nameof(jwtIssuer));
    private readonly WebViewCallbackOptions _webViewOptions = webViewOptions?.Value ?? throw new ArgumentNullException(nameof(webViewOptions));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public LoginForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    public UserBackendCapabilities Capabilities => _authentication.Capabilities;

    /// <summary>Banner shown when the previous submission failed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// When set, the page renders a deep-link preview panel instead of the
    /// sign-in form — the user has already been authenticated and the
    /// server is just confirming the callback URL before the final redirect.
    /// Only ever populated when <see cref="WebViewCallbackOptions.ShowPreviewPage"/>
    /// is true.
    /// </summary>
    public string? DeepLinkPreviewUrl { get; private set; }

    public IActionResult OnGet()
    {
        if (!Capabilities.SupportsLocalLogin)
        {
            // Backends without local sign-in (e.g. Entra) still render the
            // page, but with a redirect-style message. A future PR adds the
            // "sign in with Microsoft" button here.
            ErrorMessage = _l["Login.Error.LocalNotSupported"].Value;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsLocalLogin)
        {
            ErrorMessage = _l["Login.Error.LocalNotSupported"].Value;
            return Page();
        }

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Form.Email) || string.IsNullOrWhiteSpace(Form.Password))
        {
            ErrorMessage = _l["Login.Error.EmailPasswordRequired"].Value;
            return Page();
        }

        var result = await _authentication.SignInWithPasswordAsync(
            Form.Email.Trim(),
            Form.Password,
            Form.RememberMe,
            cancellationToken);

        switch (result.Outcome)
        {
            case SignInOutcome.Success:
                return await ResolveSuccessRedirectAsync(result.UserId, cancellationToken);

            case SignInOutcome.RequiresTwoFactor:
                // Two-factor challenge page lands with the register/reset PR.
                // For now surface a message so the admin knows what is happening.
                ErrorMessage = _l["Login.Error.TwoFactor"].Value;
                return Page();

            case SignInOutcome.LockedOut:
                ErrorMessage = _l["Login.Error.Locked"].Value;
                return Page();

            case SignInOutcome.NotAllowed:
                ErrorMessage = result.Error ?? _l["Login.Error.NotAllowed"].Value;
                return Page();

            case SignInOutcome.RedirectToExternalProvider:
                // External-provider redirect lands with the providers PR.
                ErrorMessage = _l["Login.Error.ExternalRequired"].Value;
                return Page();

            case SignInOutcome.InvalidCredentials:
            default:
                // Deliberately generic — do not leak whether the email exists.
                ErrorMessage = _l["Login.Error.Invalid"].Value;
                return Page();
        }
    }

    private async Task<IActionResult> ResolveSuccessRedirectAsync(string? userId, CancellationToken cancellationToken)
    {
        // WebView deep-link path takes priority — the cookie was set as a
        // side effect of sign-in, but the native app cares about the JWT
        // we ship via the callback URL.
        if (!string.IsNullOrEmpty(userId) && TryParseAllowedDeepLink(ReturnUrl, out var deepLink))
        {
            var token = await _jwtIssuer.IssueAsync(userId, cancellationToken);
            if (token is not null)
            {
                var callbackUrl = AppendTokenToCallback(deepLink, token);
                if (_webViewOptions.ShowPreviewPage)
                {
                    // Dev / desktop convenience: render the same Login page
                    // with the form replaced by a confirmation panel so the
                    // developer can see the callback URL, copy the token,
                    // and click Continue to fire the real redirect.
                    DeepLinkPreviewUrl = callbackUrl;
                    return Page();
                }
                return Redirect(callbackUrl);
            }
            // If JWT issuance fails (e.g. user already locked out between
            // SignInManager and IssueAsync), fall back to the safe local
            // default rather than redirect the app with no token.
        }

        return Redirect(SanitiseLocalReturnUrl(ReturnUrl));
    }

    private string SanitiseLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }
        // Only accept local URLs in the web flow. Deep-link callbacks are
        // handled separately and never reach this branch.
        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }

    private bool TryParseAllowedDeepLink(string? returnUrl, out Uri deepLink)
    {
        deepLink = null!;
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var scheme = parsed.Scheme;
        // http / https can never be deep-link targets even when listed —
        // accepting them would turn the login page into an open-redirect.
        // Web flows go through the local-URL guard above instead.
        if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var allowed in _webViewOptions.AllowedSchemes)
        {
            if (string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase))
            {
                deepLink = parsed;
                return true;
            }
        }
        return false;
    }

    private string AppendTokenToCallback(Uri deepLink, JwtTokenResult token)
    {
        var parameters = string.Join('&',
            $"access_token={Uri.EscapeDataString(token.AccessToken)}",
            $"expires_at={Uri.EscapeDataString(token.ExpiresAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture))}",
            $"user_id={Uri.EscapeDataString(token.UserId)}");

        var separator = _webViewOptions.TokenPlacement switch
        {
            WebViewTokenPlacement.Query => deepLink.Query.Length > 0 ? "&" : "?",
            _ => "#",
        };

        // Fragment placement always replaces whatever was there — uncommon
        // but the OAuth2 idiom does the same. Query mode appends.
        if (_webViewOptions.TokenPlacement == WebViewTokenPlacement.Fragment)
        {
            var withoutFragment = deepLink.GetLeftPart(UriPartial.Query);
            return $"{withoutFragment}#{parameters}";
        }

        return $"{deepLink.GetLeftPart(UriPartial.Query)}{separator}{parameters}{deepLink.Fragment}";
    }

    public sealed class LoginForm
    {
        public string? Email { get; set; }

        public string? Password { get; set; }

        public bool RememberMe { get; set; } = true;
    }
}
