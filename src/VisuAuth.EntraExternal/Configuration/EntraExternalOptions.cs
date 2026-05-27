using System.ComponentModel.DataAnnotations;

namespace VisuAuth.EntraExternal.Configuration;

/// <summary>
/// Configuration the consumer fills in for the Microsoft Entra External ID
/// adapter (formerly Azure AD B2C). Bind from <c>VisuAuth:EntraExternal</c>
/// in <c>appsettings.json</c> (or, more commonly for the secret, from
/// <c>user-secrets</c> or environment).
/// </summary>
/// <remarks>
/// <para>
/// External ID tenants are <b>customer-facing</b>: end users sign in with
/// an email + password local account managed by Microsoft, optionally with
/// federated social providers (Google, Facebook, Apple) and progressive
/// user-attribute collection during signup. The admin surface VisuAuth
/// exposes here is for support / ops scenarios — list / create / disable /
/// reset users in the directory — NOT for designing the end-user signup
/// flow itself (that lives in the Entra portal's "User flows" blade).
/// </para>
/// <para>
/// Like the Workforce adapter, this one uses the <b>app-only /
/// client-credentials</b> flow against Microsoft Graph: VisuAuth never
/// holds a customer's bearer token, it acts as the registered app and
/// pulls / mutates the directory through Graph.
/// </para>
/// <para>
/// Minimum Microsoft Graph application permissions the registered app must
/// have (with admin consent) for the full IUserStore + IRoleStore surface:
/// <list type="bullet">
///   <item><c>User.Read.All</c> — list / get</item>
///   <item><c>User.ReadWrite.All</c> — create / update / delete</item>
///   <item><c>UserAuthenticationMethod.ReadWrite.All</c> — reset password</item>
///   <item><c>AppRoleAssignment.ReadWrite.All</c> — role assignments</item>
///   <item><c>Application.Read.All</c> — read the target app's appRoles catalogue</item>
/// </list>
/// External ID exposes the same Graph endpoints as Workforce — what differs
/// is the <i>shape</i> of a user (External users carry an
/// <c>identities[]</c> array with <c>signInType = emailAddress</c> instead
/// of a server-managed <c>userPrincipalName</c>) and the authority URL
/// (<c>{tenant}.ciamlogin.com</c>). The user-shape difference shows up in
/// <see cref="VisuAuth.EntraExternal.Mapping.EntraExternalUserMapper"/>;
/// the authority URL only matters for OIDC sign-in, which v0.3 wires via
/// <c>Microsoft.Identity.Web</c> in a follow-up PR.
/// </para>
/// </remarks>
public sealed class EntraExternalOptions
{
    /// <summary>
    /// Directory (tenant) ID — the GUID for the Microsoft Entra External
    /// tenant VisuAuth manages. Found in the Entra portal under "Overview"
    /// of the tenant.
    /// </summary>
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the registered app VisuAuth authenticates
    /// as. Generated when you register the app in the Entra portal.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret of the registered app. Stored encrypted at rest is the
    /// consumer's responsibility (we recommend <c>dotnet user-secrets</c>
    /// in dev and an environment variable / key vault in production).
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The tenant's initial domain — <c>{tenant}.onmicrosoft.com</c>. Used
    /// as the <c>issuer</c> when minting an External-ID user's
    /// <c>identities[]</c> entry on <see cref="VisuAuth.Abstractions.Users.IUserStore.CreateAsync"/>.
    /// Required: Graph rejects <c>POST /users</c> for External tenants
    /// without a well-formed identities array, and the issuer must match
    /// a verified domain on the tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// External ID treats the initial domain (the one Microsoft assigns
    /// when the tenant is created) as the authoritative issuer for
    /// password-based local accounts. Even after adding custom domains,
    /// the issuer typically stays as <c>{tenant}.onmicrosoft.com</c> —
    /// custom domains affect the <i>login URL</i>, not the identity
    /// shape persisted on the user.
    /// </para>
    /// <para>
    /// Set without the leading <c>https://</c> or any path — just the
    /// hostname (e.g. <c>"contoso.onmicrosoft.com"</c>).
    /// </para>
    /// </remarks>
    [Required]
    public string TenantDomain { get; set; } = string.Empty;

    /// <summary>
    /// <b>Application (client) ID</b> — NOT object id — of the application
    /// whose <c>appRoles</c> the
    /// <see cref="VisuAuth.Abstractions.Roles.IRoleStore"/> surfaces and
    /// assigns. Defaults to <see cref="ClientId"/> — i.e. VisuAuth's own
    /// registered app is the role catalogue.
    /// </summary>
    /// <remarks>
    /// Same semantic as Workforce's <c>EntraOptions.AppRoleResourceId</c>:
    /// the adapter uses this value in a Graph
    /// <c>$filter=appId eq '{AppRoleResourceId}'</c> against
    /// <c>/servicePrincipals</c>. App roles are declared in the application
    /// manifest (Entra portal → App registrations → {your app} → App roles).
    /// Create / rename / delete throw <see cref="NotSupportedException"/>;
    /// list + assign + remove work.
    /// </remarks>
    public string? AppRoleResourceId { get; set; }

    /// <summary>
    /// Graph endpoint base URL. Defaults to the public cloud
    /// (<c>https://graph.microsoft.com/v1.0</c>). Override for sovereign
    /// clouds if Microsoft ships an External ID in one (the public cloud
    /// is the only option at the time of writing). Trailing slash optional.
    /// </summary>
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Default email domain the admin Create-User form should suggest.
    /// Surfaced through
    /// <see cref="VisuAuth.Abstractions.Capabilities.UserBackendCapabilities.EmailDomainSuffix"/>
    /// so the UI renders a fixed-suffix input ("type the local part,
    /// <c>@domain</c> stays locked").
    /// </summary>
    /// <remarks>
    /// <para>
    /// External ID is more permissive than Workforce here: a customer's
    /// email can be in any domain (gmail.com, an employer's domain, …)
    /// because the identity shape stores it as
    /// <c>identities[].issuerAssignedId</c>, NOT as a UPN that must live
    /// on a verified tenant domain. So this option is purely UX
    /// scaffolding — defaults the local-account local-part to a
    /// company-friendly suffix when an operator is creating support
    /// users. Leave null to render a free-text email input for the
    /// general case where customers sign up with whatever email they
    /// want.
    /// </para>
    /// <para>
    /// Set without the leading <c>@</c> for readability
    /// (e.g. <c>"contoso.com"</c>); the adapter prefixes it when
    /// populating the capability.
    /// </para>
    /// </remarks>
    public string? DefaultEmailDomain { get; set; }
}
