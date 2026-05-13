namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Configuration for the WebView deep-link sign-in flow. When a native app
/// opens an in-app browser at <c>/visuauth/login?returnUrl=&lt;scheme://...&gt;</c>,
/// and that scheme is in <see cref="AllowedSchemes"/>, the server emits a
/// JWT after a successful sign-in and redirects to the callback URL with
/// the token appended (placement controlled by <see cref="TokenPlacement"/>).
/// </summary>
public sealed class WebViewCallbackOptions
{
    /// <summary>
    /// Custom URL schemes the host accepts as WebView callback targets,
    /// e.g. <c>myapp</c>, <c>acme-internal</c>. Match is case-insensitive.
    /// <para>
    /// <c>http</c> and <c>https</c> are silently ignored even if added —
    /// allowing them would turn the login page into an open-redirect
    /// gadget. Web callers continue to use the standard local-URL guard.
    /// </para>
    /// </summary>
    public IList<string> AllowedSchemes { get; set; } = new List<string>();

    /// <summary>
    /// Where the issuer appends the access token on the callback URL.
    /// Defaults to <see cref="WebViewTokenPlacement.Fragment"/> — fragments
    /// are not sent to servers in <c>Referer</c> headers and do not appear
    /// in HTTP access logs, mirroring the OAuth2 implicit-flow convention.
    /// </summary>
    public WebViewTokenPlacement TokenPlacement { get; set; } = WebViewTokenPlacement.Fragment;

    /// <summary>
    /// When true, the login page renders a confirmation panel showing the
    /// callback URL (with the JWT) instead of immediately issuing a
    /// redirect. A "Continue" button performs the real redirect when the
    /// user is ready (or on a device that actually has the app installed).
    /// <para>
    /// Intended for desktop development: a custom scheme like
    /// <c>myapp://</c> is not registered as a handler on a dev machine, so
    /// the silent redirect looks like "nothing happened". The preview gives
    /// the developer visibility into the flow and a copyable URL.
    /// </para>
    /// <para>
    /// Production deployments should leave this <c>false</c> — mobile
    /// clients expect the silent redirect.
    /// </para>
    /// </summary>
    public bool ShowPreviewPage { get; set; }
}

/// <summary>Where to attach the JWT on the WebView callback URL.</summary>
public enum WebViewTokenPlacement
{
    /// <summary>Append after a <c>#</c> (default; recommended).</summary>
    Fragment,

    /// <summary>Append as <c>?access_token=...&amp;expires_at=...&amp;user_id=...</c>.</summary>
    Query,
}
