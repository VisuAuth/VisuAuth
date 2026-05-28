namespace VisuAuth.Abstractions.Capabilities;

/// <summary>
/// Declares which features a user backend supports. The admin UI and end-user UI
/// inspect these flags at runtime and hide controls for unsupported operations.
/// </summary>
/// <remarks>
/// Different backends have very different capabilities. ASP.NET Core Identity owns
/// its data and supports the full surface. Microsoft Entra ID, in contrast, is a
/// cloud IAM where login flows are hosted by Microsoft and many operations are
/// exposed only through Microsoft Graph. Adapters describe their reality through
/// this record so the UI degrades gracefully.
/// </remarks>
public sealed record UserBackendCapabilities
{
    /// <summary>The backend can authenticate users locally (email + password form).</summary>
    public bool SupportsLocalLogin { get; init; }

    /// <summary>Self-service registration is possible.</summary>
    public bool SupportsRegistration { get; init; }

    /// <summary>Password reset flow is supported.</summary>
    public bool SupportsPasswordReset { get; init; }

    /// <summary>The admin can reset the two-factor configuration of a user.</summary>
    public bool SupportsTwoFactorReset { get; init; }

    /// <summary>
    /// The backend can manage TOTP authenticator setup, challenge, and recovery
    /// codes for end users. Drives whether <c>/visuauth/two-factor/*</c> pages
    /// surface controls and whether the layout shows the "Setup 2FA" link.
    /// </summary>
    public bool SupportsTwoFactor { get; init; }

    /// <summary>The admin can impersonate (log in as) another user.</summary>
    public bool SupportsImpersonation { get; init; }

    /// <summary>Custom claims can be read and written through the backend.</summary>
    public bool SupportsCustomClaims { get; init; }

    /// <summary>The backend exposes role management.</summary>
    public bool SupportsRoleManagement { get; init; }

    /// <summary>
    /// The role catalogue can be mutated at runtime — i.e. roles can be
    /// created, renamed, and deleted through <see cref="VisuAuth.Abstractions.Roles.IRoleStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="SupportsRoleManagement"/>: a backend can
    /// fully support <i>assigning</i> roles (list / get / assign / remove)
    /// while forbidding <i>defining</i> them at runtime. Microsoft Entra is
    /// the canonical example — app roles are declared in the application
    /// registration manifest, so the Graph adapters' <c>CreateAsync</c> /
    /// <c>RenameAsync</c> / <c>DeleteAsync</c> throw
    /// <see cref="NotSupportedException"/> per the IRoleStore contract.
    /// </para>
    /// <para>
    /// The admin Roles page consults this flag to hide the create / rename
    /// / delete controls when the backend can't honour them, so an operator
    /// never submits a form that would surface a NotSupported error. The
    /// ASP.NET Core Identity adapter sets it <c>true</c> (it owns its role
    /// table); the Entra and Entra External adapters set it <c>false</c>.
    /// Defaults to <c>false</c> — a new adapter that hasn't reasoned about
    /// runtime role mutation is presented as read-only, which is the safe
    /// degradation (hides a feature rather than 500-ing on submit).
    /// </para>
    /// </remarks>
    public bool SupportsRoleMutation { get; init; }

    /// <summary>An audit log of identity events is available.</summary>
    public bool SupportsAuditLog { get; init; }

    /// <summary>Bulk operations (mass enable/disable, invite, etc.) are supported.</summary>
    public bool SupportsBulkOperations { get; init; }

    /// <summary>The backend can revoke active sessions.</summary>
    public bool SupportsSessionRevocation { get; init; }

    /// <summary>External identity providers (Google, Apple, etc.) can be configured.</summary>
    public bool SupportsExternalProviders { get; init; }

    /// <summary>The backend uses an explicit email-confirmation step.</summary>
    public bool SupportsEmailConfirmation { get; init; }

    /// <summary>The backend supports lockout after failed attempts.</summary>
    public bool SupportsLockout { get; init; }

    /// <summary>
    /// When set, the admin Create-User form locks the email input to this
    /// fixed suffix and only lets the operator type the local part. Used
    /// by backends that reject arbitrary domains — typically Microsoft
    /// Entra ID, where the user principal name has to belong to a verified
    /// tenant domain (otherwise Graph returns 400 "The domain portion of
    /// the userPrincipalName property is invalid").
    /// </summary>
    /// <remarks>
    /// Include the leading <c>@</c> when setting (e.g.
    /// <c>"@visuauth.onmicrosoft.com"</c>). Null means "any domain" — the
    /// input stays a single free-text field, matching the historical
    /// behaviour of the Identity adapter. Multi-domain tenants pick one
    /// "default" to suggest; the operator can still bypass via the API by
    /// passing a full email in CreateUserCommand.Email — this flag drives
    /// UI ergonomics, not validation.
    /// </remarks>
    public string? EmailDomainSuffix { get; init; }
}
