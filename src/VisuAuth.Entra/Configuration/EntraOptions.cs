using System.ComponentModel.DataAnnotations;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Configuration the consumer fills in for the Microsoft Entra ID adapter.
/// Bind from <c>VisuAuth:Entra</c> in <c>appsettings.json</c> (or, more
/// commonly for the secret, from <c>user-secrets</c> or environment).
/// </summary>
/// <remarks>
/// <para>
/// The adapter uses the <b>app-only / client-credentials</b> auth flow against
/// Microsoft Graph (see CLAUDE.md §6 and the PR-#34 design discussion):
/// VisuAuth itself never holds a user's bearer token, it acts as the
/// registered app and pulls / mutates the directory through Graph.
/// </para>
/// <para>
/// Minimum Graph delegated / application permissions the registered app
/// must have (with admin consent) for the full IUserStore + IRoleStore
/// surface to work:
/// <list type="bullet">
///   <item><c>User.Read.All</c> — list / get</item>
///   <item><c>User.ReadWrite.All</c> — create / update / delete</item>
///   <item><c>UserAuthenticationMethod.ReadWrite.All</c> — reset password / 2FA</item>
///   <item><c>Directory.AccessAsUser.All</c> — revoke sessions</item>
///   <item><c>AppRoleAssignment.ReadWrite.All</c> — role assignments</item>
///   <item><c>Application.Read.All</c> — read the target app's appRoles catalogue</item>
/// </list>
/// Capabilities are independently enforced by Entra; if a permission is
/// missing the corresponding store method bubbles up the Graph
/// <c>403 Forbidden</c> as a <see cref="VisuAuth.Abstractions.Common.UserResult"/>
/// failure rather than crashing.
/// </para>
/// </remarks>
public sealed class EntraOptions
{
    /// <summary>
    /// Directory (tenant) ID — the GUID for the Microsoft Entra tenant
    /// VisuAuth manages. Found in the Entra portal under "Overview" of the
    /// tenant.
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
    /// <b>Application (client) ID</b> — NOT object id — of the application
    /// whose <c>appRoles</c> the
    /// <see cref="VisuAuth.Abstractions.Roles.IRoleStore"/> surfaces and
    /// assigns. Defaults to <see cref="ClientId"/> — i.e. VisuAuth's own
    /// registered app is the role catalogue.
    /// </summary>
    /// <remarks>
    /// The adapter uses this value in a Graph
    /// <c>$filter=appId eq '{AppRoleResourceId}'</c> against
    /// <c>/servicePrincipals</c>, so it must be an Application Id (the same
    /// shape as <see cref="ClientId"/>). The name "ResourceId" reflects the
    /// OAuth role naming (the resource being protected), not Graph's
    /// "object id" terminology — the two are different GUIDs.
    /// App roles are declared in the application manifest (Azure portal → App
    /// registrations → {your app} → App roles). The Entra adapter cannot
    /// create / rename / delete them at runtime — those operations throw
    /// <see cref="NotSupportedException"/>. Listing + assigning + removing
    /// IS supported.
    /// </remarks>
    public string? AppRoleResourceId { get; set; }

    /// <summary>
    /// Graph endpoint base URL. Defaults to the public cloud
    /// (<c>https://graph.microsoft.com/v1.0</c>). Override for sovereign
    /// clouds (Entra Government, Entra China). Trailing slash optional.
    /// </summary>
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Default verified email domain the admin Create-User form should
    /// suggest. Surfaced through
    /// <see cref="VisuAuth.Abstractions.Capabilities.UserBackendCapabilities.EmailDomainSuffix"/>
    /// so the UI renders a fixed-suffix input ("type the local part,
    /// @domain stays locked"). Avoids the Graph 400 every operator hits
    /// the first time they try to create a user with an external email.
    /// </summary>
    /// <remarks>
    /// Set without the leading <c>@</c> for readability
    /// (e.g. <c>"visuauth.onmicrosoft.com"</c>); the adapter prefixes it
    /// when populating the capability. Null = no suggestion, the form
    /// renders a free-text email input. Multi-domain tenants pick the
    /// most-common one as default; users with mailboxes in another
    /// verified domain can still be created via the API by passing a
    /// full email through <c>CreateUserCommand.Email</c>.
    /// </remarks>
    public string? DefaultEmailDomain { get; set; }
}
