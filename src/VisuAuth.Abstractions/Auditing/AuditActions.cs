namespace VisuAuth.Abstractions.Auditing;

/// <summary>
/// Centralised registry of action codes VisuAuth itself emits. Consumers
/// are free to use ad-hoc strings for their own audit events; this class
/// is just the canonical list the admin UI knows about for filtering and
/// localisation.
/// </summary>
/// <remarks>
/// Codes are PascalCase verbs/nouns chosen for stable grep-ability. They
/// MUST stay backward-compatible — renaming a code orphans historical
/// entries in consumer databases.
/// </remarks>
public static class AuditActions
{
    // --- User mutations (admin-driven) ---
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeleted = "UserDeleted";
    public const string UserLocked = "UserLocked";
    public const string UserUnlocked = "UserUnlocked";
    public const string UserPasswordResetByAdmin = "UserPasswordResetByAdmin";
    public const string UserTwoFactorResetByAdmin = "UserTwoFactorResetByAdmin";
    public const string UserSessionsRevokedByAdmin = "UserSessionsRevokedByAdmin";
    public const string RoleAssignedToUser = "RoleAssignedToUser";
    public const string RoleRemovedFromUser = "RoleRemovedFromUser";

    // --- Role + Tenant catalogue (admin-driven) ---
    public const string RoleCreated = "RoleCreated";
    public const string RoleRenamed = "RoleRenamed";
    public const string RoleDeleted = "RoleDeleted";
    public const string TenantCreated = "TenantCreated";
    public const string TenantRenamed = "TenantRenamed";
    public const string TenantDeleted = "TenantDeleted";

    // --- External providers (admin-driven) ---
    public const string ExternalProviderSaved = "ExternalProviderSaved";
    public const string ExternalProviderEnabled = "ExternalProviderEnabled";
    public const string ExternalProviderDisabled = "ExternalProviderDisabled";
    public const string ExternalProviderBulkEnabled = "ExternalProviderBulkEnabled";
    public const string ExternalProviderBulkDisabled = "ExternalProviderBulkDisabled";
    public const string ExternalProviderOrphanDeleted = "ExternalProviderOrphanDeleted";

    // --- Adapter configuration (admin-driven) ---
    public const string AdapterConfigSaved = "AdapterConfigSaved";

    // --- End-user self-service ---
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginRequiresTwoFactor = "LoginRequiresTwoFactor";
    public const string LoginLockedOut = "LoginLockedOut";
    public const string LogoutSucceeded = "LogoutSucceeded";
    public const string PasswordChangedBySelf = "PasswordChangedBySelf";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordResetCompleted = "PasswordResetCompleted";
    public const string EmailConfirmed = "EmailConfirmed";
    public const string ProfileUpdated = "ProfileUpdated";
    public const string UserRegistered = "UserRegistered";

    // --- Two-factor ---
    public const string TwoFactorEnabled = "TwoFactorEnabled";
    public const string TwoFactorDisabledBySelf = "TwoFactorDisabledBySelf";
    public const string TwoFactorChallengePassed = "TwoFactorChallengePassed";
    public const string TwoFactorChallengeFailed = "TwoFactorChallengeFailed";
    public const string TwoFactorRecoveryCodeUsed = "TwoFactorRecoveryCodeUsed";
    public const string TwoFactorRecoveryCodesRegenerated = "TwoFactorRecoveryCodesRegenerated";

    // --- External login ---
    public const string ExternalLoginSucceeded = "ExternalLoginSucceeded";
    public const string ExternalLoginFailed = "ExternalLoginFailed";
    public const string ExternalLoginLinked = "ExternalLoginLinked";
    public const string ExternalLoginUnlinked = "ExternalLoginUnlinked";
    public const string ExternalLoginAutoCreated = "ExternalLoginAutoCreated";

    // --- Mobile / JWT ---
    public const string JwtIssued = "JwtIssued";
    public const string JwtIssueFailed = "JwtIssueFailed";

    // --- System ---
    public const string SystemRetentionPurge = "SystemRetentionPurge";
}

/// <summary>
/// Conventional <see cref="AuditEvent.TargetType"/> values for VisuAuth's
/// own entries. Consumers can use other values for their domain entries.
/// </summary>
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Role = "Role";
    public const string Tenant = "Tenant";
    public const string ExternalProvider = "ExternalProvider";
    public const string AdapterConfig = "AdapterConfig";
    public const string ExternalLogin = "ExternalLogin";
    public const string System = "System";
    public const string Session = "Session";
}
