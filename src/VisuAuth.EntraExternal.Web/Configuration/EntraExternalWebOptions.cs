using System.ComponentModel.DataAnnotations;

namespace VisuAuth.EntraExternal.Web.Configuration;

/// <summary>
/// Configuration the consumer fills in for the End-user OIDC sign-in
/// flow against an Entra External ID tenant. Bind from
/// <c>VisuAuth:EntraExternal:Web</c>. Distinct from the admin-side
/// <c>EntraExternalOptions</c> (which holds the Graph app-only client
/// secret): the web sign-in flow is delegated OIDC and authenticates
/// the END USER, not the registered app.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two configuration sections?</b> Graph CRUD and end-user
/// sign-in use different OAuth flows against different app registrations
/// in the typical deployment:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="VisuAuth.EntraExternal.Configuration.EntraExternalOptions"/>
///     — app-only (client credentials), confidential client with a
///     directory-scoped secret. The app reads / writes the customer
///     directory through Graph on behalf of the registered app itself.
///   </item>
///   <item>
///     <see cref="EntraExternalWebOptions"/> (this type) — authorization
///     code with PKCE, a separate app registration with a public client
///     redirect URI configured for the consumer's domain. The end user
///     signs into the hosted Microsoft page and a session cookie is
///     issued back to the consumer's app.
///   </item>
/// </list>
/// <para>
/// Microsoft's documentation treats these as separate apps too; sharing
/// a single app between the two flows is technically possible but adds
/// security surface (the same client secret signs both admin Graph
/// requests and end-user authentication redirects) and is not
/// recommended.
/// </para>
/// </remarks>
public sealed class EntraExternalWebOptions
{
    /// <summary>
    /// Tenant subdomain — <c>{tenant}</c> in <c>{tenant}.ciamlogin.com</c>.
    /// Used to build the OIDC authority URL. <b>Not</b> the same as
    /// <see cref="VisuAuth.EntraExternal.Configuration.EntraExternalOptions.TenantDomain"/>:
    /// that one carries <c>.onmicrosoft.com</c> as the issuer claim on
    /// identities[]. The two values look similar (<c>contoso</c> vs
    /// <c>contoso.onmicrosoft.com</c>) but live in different roles.
    /// </summary>
    [Required]
    public string TenantSubdomain { get; set; } = string.Empty;

    /// <summary>
    /// Directory (tenant) ID — same GUID as
    /// <see cref="VisuAuth.EntraExternal.Configuration.EntraExternalOptions.TenantId"/>
    /// in a typical deployment. Microsoft.Identity.Web uses it to validate
    /// the <c>iss</c> claim on incoming tokens.
    /// </summary>
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the <b>end-user-facing</b> app
    /// registration (the one with the OIDC redirect URI). NOT the admin
    /// Graph app — see the remarks on the class.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for the end-user app registration. Optional only when
    /// the app is configured as a public client (no secret). For a typical
    /// confidential web client, this is required — leave blank and OIDC
    /// will reject the token exchange with <c>invalid_client</c>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Path the OIDC callback redirects to after Microsoft authenticates
    /// the user. Defaults to <c>/signin-oidc</c>, the
    /// <c>Microsoft.Identity.Web</c> convention. Must match the redirect
    /// URI configured on the app registration's Authentication blade
    /// EXACTLY (scheme + host + this path).
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Path the sign-out flow redirects to after Microsoft clears the
    /// hosted session. Defaults to <c>/signout-callback-oidc</c>. Must
    /// match the post-logout redirect URI on the app registration.
    /// </summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>
    /// Default sign-in user flow name (e.g. <c>SignUpSignIn</c>). Entra
    /// External uses <i>user flows</i> (the post-B2C rebrand) to customise
    /// the hosted sign-up / sign-in pages — pass the name of the flow you
    /// want VisuAuth's "Sign in with Microsoft" button to launch. v0.3
    /// ships sign-in only; PR D adds flow selection + attribute mapping.
    /// </summary>
    /// <remarks>
    /// Optional in v0.3: when null, Microsoft.Identity.Web uses the
    /// default authority and Entra falls back to whatever flow the tenant
    /// has marked as default in its policy bindings.
    /// </remarks>
    public string? SignInUserFlow { get; set; }

    /// <summary>
    /// Computed OIDC authority URL — the issuer Microsoft.Identity.Web
    /// uses to discover the OpenID configuration document. External
    /// tenants use <c>https://{tenant}.ciamlogin.com/{tenant-id}/v2.0</c>,
    /// NOT the Workforce <c>https://login.microsoftonline.com/{tenant-id}</c>.
    /// </summary>
    /// <remarks>
    /// Hard-coded path shape rather than configurable because the External
    /// authority URL is a fixed contract Microsoft publishes; making it
    /// overridable would invite typos and provide no real value. Sovereign
    /// clouds for External don't exist as of writing — if Microsoft ships
    /// one, this becomes an option.
    /// </remarks>
    public string GetAuthority()
        => $"https://{TenantSubdomain}.ciamlogin.com/{TenantId}/v2.0";
}
