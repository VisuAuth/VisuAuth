using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.EndUserUi.Pages.ExternalLogin;

/// <summary>
/// Lands here after the OAuth provider successfully authenticates and the
/// host's external cookie has been established. Resolves the linked local
/// user (or creates one per the configured first-time strategy) and signs
/// the user in. On success, redirects to <c>returnUrl</c> — including the
/// WebView deep-link path when the URL matches the allow-listed scheme.
/// </summary>
public sealed class CallbackModel(
    IExternalLoginFlow externalLogin,
    IJwtIssuer jwtIssuer,
    ITenantContext tenantContext,
    IOptions<ExternalLoginOptions> externalOptions,
    IOptions<WebViewCallbackOptions> webViewOptions,
    IAuditWriter auditWriter,
    IStringLocalizer<EndUserSharedResources> localizer) : PageModel
{
    /// <summary>Audit payload key carrying the external auth scheme name.</summary>
    private const string ProviderKey = "provider";

    private readonly IExternalLoginFlow _externalLogin = externalLogin ?? throw new ArgumentNullException(nameof(externalLogin));
    private readonly IJwtIssuer _jwtIssuer = jwtIssuer ?? throw new ArgumentNullException(nameof(jwtIssuer));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly ExternalLoginOptions _externalOptions = externalOptions?.Value ?? throw new ArgumentNullException(nameof(externalOptions));
    private readonly WebViewCallbackOptions _webViewOptions = webViewOptions?.Value ?? throw new ArgumentNullException(nameof(webViewOptions));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<EndUserSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "remoteError")]
    public string? RemoteError { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(RemoteError))
        {
            // Provider sent us back with an error (user cancelled, app
            // misconfigured, …). Surface it back on the login page so the
            // user can pick another option.
            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.ExternalLoginFailed,
                TargetType = AuditTargetTypes.ExternalLogin,
                Outcome = AuditOutcome.Failure,
                FailureReason = RemoteError,
                Payload = new Dictionary<string, string?> { ["source"] = "remoteError" },
            }, cancellationToken);
            return RedirectToLoginWithError(_l["ExternalLogin.Error.RemoteError"].Value);
        }

        var result = await _externalLogin.CompleteSignInAsync(
            _externalOptions.FirstTimeStrategy,
            cancellationToken);

        switch (result.Outcome)
        {
            case ExternalSignInOutcome.Success:
                await _audit.WriteAsync(new AuditEvent
                {
                    Action = AuditActions.ExternalLoginSucceeded,
                    TargetType = AuditTargetTypes.User,
                    TargetId = result.UserId,
                    TargetLabel = result.PendingEmail,
                    Outcome = AuditOutcome.Success,
                    Payload = new Dictionary<string, string?>
                    {
                        [ProviderKey] = result.PendingProvider,
                    },
                }, cancellationToken);
                return await ResolveSuccessRedirectAsync(result.UserId, cancellationToken);

            case ExternalSignInOutcome.RequiresConfirmation:
                // Hand the user off to the confirm page. The pending claims
                // (provider, email, display name) live on the external
                // cookie itself — Confirm re-reads them via
                // IExternalLoginFlow.GetPendingInfoAsync(), no TempData
                // needed. Only returnUrl rides along as a query param.
                var confirmUrl = string.IsNullOrWhiteSpace(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl)
                    ? "/visuauth/external-login/confirm"
                    : $"/visuauth/external-login/confirm?returnUrl={Uri.EscapeDataString(ReturnUrl)}";
                return Redirect(confirmUrl);

            case ExternalSignInOutcome.NoExternalSession:
                await _audit.WriteAsync(new AuditEvent
                {
                    Action = AuditActions.ExternalLoginFailed,
                    TargetType = AuditTargetTypes.ExternalLogin,
                    Outcome = AuditOutcome.Failure,
                    FailureReason = "No external session",
                }, cancellationToken);
                return RedirectToLoginWithError(_l["ExternalLogin.Error.NoSession"].Value);

            case ExternalSignInOutcome.LockedOut:
                await _audit.WriteAsync(new AuditEvent
                {
                    Action = AuditActions.LoginLockedOut,
                    TargetType = AuditTargetTypes.User,
                    TargetId = result.UserId,
                    TargetLabel = result.PendingEmail,
                    Outcome = AuditOutcome.Failure,
                    Payload = new Dictionary<string, string?> { [ProviderKey] = result.PendingProvider },
                }, cancellationToken);
                return RedirectToLoginWithError(_l["Login.Error.Locked"].Value);

            case ExternalSignInOutcome.NotAllowed:
                await _audit.WriteAsync(new AuditEvent
                {
                    Action = AuditActions.ExternalLoginFailed,
                    TargetType = AuditTargetTypes.User,
                    TargetId = result.UserId,
                    TargetLabel = result.PendingEmail,
                    Outcome = AuditOutcome.Failure,
                    FailureReason = "Sign-in not allowed",
                    Payload = new Dictionary<string, string?> { [ProviderKey] = result.PendingProvider },
                }, cancellationToken);
                return RedirectToLoginWithError(_l["Login.Error.NotAllowed"].Value);

            // ExternalSignInOutcome.Failed and any future outcome both fall
            // to the generic "we could not sign you in" branch.
            default:
                var detail = result.Errors.Count > 0
                    ? string.Join("; ", result.Errors)
                    : _l["ExternalLogin.Error.SignInFailed"].Value;
                await _audit.WriteAsync(new AuditEvent
                {
                    Action = AuditActions.ExternalLoginFailed,
                    TargetType = AuditTargetTypes.ExternalLogin,
                    Outcome = AuditOutcome.Failure,
                    FailureReason = detail,
                    Payload = new Dictionary<string, string?> { [ProviderKey] = result.PendingProvider },
                }, cancellationToken);
                return RedirectToLoginWithError(detail);
        }
    }

    private async Task<IActionResult> ResolveSuccessRedirectAsync(string? userId, CancellationToken cancellationToken)
    {
        // Mirror the Login page's WebView path so external sign-in produces
        // a JWT-bearing deep-link redirect when the returnUrl is one of the
        // allow-listed custom schemes. This keeps the mobile contract
        // identical regardless of which flow the user chose.
        if (!string.IsNullOrEmpty(userId) && TryParseAllowedDeepLink(ReturnUrl, out var deepLink))
        {
            var token = await _jwtIssuer.IssueAsync(userId, cancellationToken);
            if (token is not null)
            {
                return Redirect(AppendTokenToCallback(deepLink, token));
            }
        }

        return Redirect(SanitiseLocalReturnUrl(ReturnUrl));
    }

    private RedirectResult RedirectToLoginWithError(string error)
    {
        TempData["ExternalLogin.Error"] = error;
        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return Redirect($"/visuauth/login?returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        }
        return Redirect("/visuauth/login");
    }

    private string SanitiseLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }
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
        if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (_webViewOptions.AllowedSchemes.Any(allowed =>
                string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase)))
        {
            deepLink = parsed;
            return true;
        }
        return false;
    }

    private string AppendTokenToCallback(Uri deepLink, JwtTokenResult token)
    {
        // Same shape as Login.cshtml.cs uses — keep mobile clients agnostic
        // about which sign-in path produced the token.
        var parameters = string.Join('&',
            $"access_token={Uri.EscapeDataString(token.AccessToken)}",
            $"expires_at={Uri.EscapeDataString(token.ExpiresAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture))}",
            $"user_id={Uri.EscapeDataString(token.UserId)}");
        var separator = _webViewOptions.TokenPlacement switch
        {
            WebViewTokenPlacement.Query => deepLink.Query.Length > 0 ? "&" : "?",
            _ => "#",
        };
        if (_webViewOptions.TokenPlacement == WebViewTokenPlacement.Fragment)
        {
            var withoutFragment = deepLink.GetLeftPart(UriPartial.Query);
            return $"{withoutFragment}#{parameters}";
        }
        return $"{deepLink.GetLeftPart(UriPartial.Query)}{separator}{parameters}{deepLink.Fragment}";
    }

    /// <summary>
    /// Resolved at request time by the confirm page so it can scope the
    /// new account to the current tenant when multi-tenancy is on. Public
    /// to keep the cshtml syntactically straightforward.
    /// </summary>
    public string? CurrentTenantId => _tenantContext.IsMultiTenancyEnabled
        ? _tenantContext.CurrentTenantId
        : null;
}
