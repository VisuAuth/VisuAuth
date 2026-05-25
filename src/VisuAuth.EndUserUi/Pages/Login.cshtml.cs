using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.EndUserUi.Authentication;
// SignInResult exists in both VisuAuth and Microsoft.AspNetCore.Mvc; alias
// the VisuAuth one so ApplyResponseAsync's parameter type stays unambiguous.
using SignInResult = VisuAuth.Abstractions.Authentication.SignInResult;

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
    IExternalLoginFlow externalLogin,
    IJwtIssuer jwtIssuer,
    IOptions<WebViewCallbackOptions> webViewOptions,
    SignInAuditEmitter auditEmitter,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    private readonly IAuthenticationFlow _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly IExternalLoginFlow _externalLogin = externalLogin ?? throw new ArgumentNullException(nameof(externalLogin));
    private readonly IJwtIssuer _jwtIssuer = jwtIssuer ?? throw new ArgumentNullException(nameof(jwtIssuer));
    private readonly WebViewCallbackOptions _webViewOptions = webViewOptions?.Value ?? throw new ArgumentNullException(nameof(webViewOptions));
    private readonly SignInAuditEmitter _auditEmitter = auditEmitter ?? throw new ArgumentNullException(nameof(auditEmitter));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    /// <summary>
    /// External providers registered with the host (Google, Microsoft, etc.).
    /// Empty when the consumer wired no provider — the cshtml then suppresses
    /// the entire "or sign in with" section.
    /// </summary>
    public IReadOnlyList<ExternalProviderInfo> ExternalProviders { get; private set; } = [];

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

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsLocalLogin)
        {
            // Backends without local sign-in (e.g. Entra) still render the
            // page, but with a redirect-style message. External-provider
            // buttons below still render — they may be the only sign-in path.
            ErrorMessage = _l["Login.Error.LocalNotSupported"].Value;
        }
        // Pull through any error message stashed by /external-login/callback
        // when the OAuth round-trip succeeded against the provider but the
        // local sign-in afterwards fell through (no email claim, locked-out
        // user, identity-create failure, …). Without this, the user gets
        // bounced back to /login silently and has no idea what went wrong.
        if (TempData["ExternalLogin.Error"] is string externalError && !string.IsNullOrWhiteSpace(externalError))
        {
            ErrorMessage = externalError;
        }
        await LoadExternalProvidersAsync(cancellationToken);
        return Page();
    }

    private async Task LoadExternalProvidersAsync(CancellationToken cancellationToken)
    {
        if (!_externalLogin.Capabilities.SupportsExternalProviders)
        {
            return;
        }
        ExternalProviders = await _externalLogin.GetProvidersAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Always re-load providers so a re-rendered failed login still shows
        // the "or sign in with" buttons — otherwise users on a wrong-password
        // attempt would see them disappear.
        await LoadExternalProvidersAsync(cancellationToken);

        var guard = GuardLocalLoginInput();
        if (guard is not null)
        {
            return guard;
        }

        var attemptedEmail = Form.Email!.Trim();
        var result = await _authentication.SignInWithPasswordAsync(
            attemptedEmail,
            Form.Password!,
            Form.RememberMe,
            cancellationToken);

        // Audit via the shared emitter — adds the rememberMe payload only
        // on the Web channel since RememberMe is meaningless for API auth.
        await _auditEmitter.EmitAsync(
            result,
            attemptedEmail,
            SignInChannel.Web,
            extraPayload: new Dictionary<string, string?>
            {
                ["rememberMe"] = Form.RememberMe ? "true" : "false",
            },
            cancellationToken: cancellationToken);

        return await ApplyResponseAsync(SignInPageResponseMapper.Map(result), result, cancellationToken);
    }

    /// <summary>
    /// Early-bail checks for the local-login form — adapter capability
    /// flag + model validity. Returns the IActionResult to render, or null
    /// when the input is fit to hit the sign-in flow.
    /// </summary>
    private PageResult? GuardLocalLoginInput()
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
        return null;
    }

    /// <summary>
    /// Translates the response mapper's decision into the actual
    /// IActionResult, setting ErrorMessage when the decision wants the
    /// page re-rendered. The page model owns the navigation side-effects
    /// (returnUrl resolution, two-factor URL build) because they need
    /// LoginModel-specific state (ReturnUrl, Form.RememberMe).
    /// </summary>
    private async Task<IActionResult> ApplyResponseAsync(
        SignInPageOutcome outcome,
        SignInResult result,
        CancellationToken cancellationToken)
    {
        switch (outcome.Decision)
        {
            case SignInPageDecision.RedirectSuccess:
                return await ResolveSuccessRedirectAsync(result.UserId, cancellationToken);
            case SignInPageDecision.RedirectTwoFactor:
                return RedirectToTwoFactor(Form.RememberMe);
            default:
                ErrorMessage = outcome.RawErrorMessage
                    ?? (outcome.ErrorResourceKey is { Length: > 0 } key ? _l[key].Value : null);
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

    private RedirectResult RedirectToTwoFactor(bool rememberMe)
    {
        // remember-me always rides the 2FA hop, so the query string is never
        // empty — keep the URL builder unconditional and skip the dead-code
        // empty-list branch Sonar (rightly) flags.
        var query = new List<string>(2)
        {
            // Forward the checkbox so the persistent flag survives the hop.
            // Without it the cookie always lands as session-scope after the
            // challenge, regardless of what the user ticked on /login.
            $"rememberMe={(rememberMe ? "true" : "false")}",
        };
        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            query.Add($"returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        }

        return Redirect($"/visuauth/two-factor/verify?{string.Join('&', query)}");
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
