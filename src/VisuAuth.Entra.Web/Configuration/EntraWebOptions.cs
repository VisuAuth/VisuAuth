using System.ComponentModel.DataAnnotations;

namespace VisuAuth.Entra.Web.Configuration;

/// <summary>
/// Configuration for operator sign-in against an Entra ID (Workforce) tenant.
/// Bind from <c>VisuAuth:Entra:Web</c>. Distinct from the admin-side
/// <see cref="VisuAuth.Entra.Configuration.EntraOptions"/> (which holds the
/// Graph app-only client secret): this flow is delegated OIDC and authenticates
/// the <b>operator</b> reaching the dashboard, not the registered app.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two configuration sections?</b> Graph CRUD and operator sign-in use
/// different OAuth flows, and Microsoft's guidance treats them as separate app
/// registrations:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="VisuAuth.Entra.Configuration.EntraOptions"/> — app-only
///     (client credentials). The app reads / writes the directory through Graph
///     as itself, which is why an unprotected admin over this adapter is so
///     dangerous: those permissions are the app's, not the visitor's.
///   </item>
///   <item>
///     <see cref="EntraWebOptions"/> (this type) — authorization code, a
///     registration with a redirect URI on the consumer's domain. The operator
///     signs into the hosted Microsoft page and gets a session cookie back.
///   </item>
/// </list>
/// <para>
/// Sharing one app registration between both flows is possible but means the
/// same secret signs admin Graph calls and end-user redirects. Not recommended.
/// </para>
/// </remarks>
public sealed class EntraWebOptions
{
    /// <summary>
    /// Directory (tenant) ID — normally the same GUID as
    /// <see cref="VisuAuth.Entra.Configuration.EntraOptions.TenantId"/>.
    /// </summary>
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the <b>sign-in</b> app registration (the one
    /// with the OIDC redirect URI) — not the Graph app.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for the sign-in app registration. Required for a
    /// confidential web client; leave blank only for a public client.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Authority host. Defaults to the global cloud. Override for sovereign
    /// clouds (e.g. <c>https://login.microsoftonline.us/</c>).
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Path Microsoft redirects to after authenticating. Defaults to
    /// <c>/signin-oidc</c>, the Microsoft.Identity.Web convention. Must match
    /// the app registration's redirect URI exactly (scheme + host + path).
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Path the sign-out flow returns to. Defaults to
    /// <c>/signout-callback-oidc</c>. Must match the post-logout redirect URI.
    /// </summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";
}
