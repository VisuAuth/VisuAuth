using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.Entra;

/// <summary>
/// Single source of truth for what the Entra adapter declares it can do.
/// Both <see cref="EntraUserStore"/> and <see cref="EntraAuthenticationFlow"/>
/// read from this so the flags can never drift between the two facets of
/// the adapter — the UI consults whichever is convenient and gets the
/// same answer.
/// </summary>
/// <remarks>
/// <para>
/// Rationale for each flag:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SupportsLocalLogin = false</b> — Microsoft owns the login UX.
///     This is the headline capability flip that triggers VisuAuth's
///     "Sign in with Microsoft" button instead of the email / password
///     form (CLAUDE.md §6 + §1.2).
///   </item>
///   <item>
///     <b>SupportsRegistration = false</b> — Entra ID workforce tenants
///     require invite-flow (B2B) or self-service signup configured at the
///     tenant level. Out of scope for the v0.2 admin adapter; v0.3 Entra
///     External ID will reopen this.
///   </item>
///   <item>
///     <b>SupportsPasswordReset = true</b> — the admin can rotate via
///     Graph's authentication methods API. End-user self-service reset
///     is the tenant's SSPR; the admin UI doesn't ride that flow.
///   </item>
///   <item>
///     <b>SupportsTwoFactor = false</b> — TOTP setup pages don't apply;
///     Entra users enrol authenticators through Microsoft's own UX.
///   </item>
///   <item>
///     <b>SupportsTwoFactorReset = true</b> — the admin can wipe a user's
///     authentication methods (forces re-enrolment).
///   </item>
///   <item>
///     <b>SupportsLockout = false</b> — Entra uses smart lockout that
///     can't be flipped per-user from outside; admin "lock" = disable
///     account, mapped via <see cref="VisuAuth.Abstractions.Users.IUserStore.SetEnabledAsync"/>.
///   </item>
///   <item>
///     <b>SupportsEmailConfirmation = false</b> — Entra validates emails
///     at directory-creation time.
///   </item>
///   <item>
///     <b>SupportsRoleManagement = true</b> — app roles (NOT directory
///     roles) via <see cref="VisuAuth.Abstractions.Roles.IRoleStore"/>.
///     Create / rename / delete still throw NotSupported (declared in
///     the app manifest), but list + assign + remove work.
///   </item>
///   <item>
///     <b>SupportsSessionRevocation = true</b> — Graph
///     <c>revokeSignInSessions</c> endpoint invalidates every refresh
///     token the user holds.
///   </item>
///   <item>
///     <b>SupportsExternalProviders = false</b> — Entra IS the IdP;
///     external-provider config doesn't apply.
///   </item>
///   <item>
///     <b>SupportsImpersonation = false</b> — out of scope.
///   </item>
///   <item>
///     <b>SupportsCustomClaims = true</b> — Graph extension properties
///     (open + schema) are readable through the user store, even if the
///     v0.2 admin UI doesn't surface them yet.
///   </item>
///   <item>
///     <b>SupportsAuditLog = true</b> — Entra has its own auditLogs
///     endpoint. v0.2 doesn't ship an <c>IAuditReader</c> wrapper; the
///     flag is set so the dashboard hides the empty in-tenant audit log
///     section (which would still ship the EF reader from the Identity
///     adapter, irrelevant for an Entra-only consumer).
///   </item>
///   <item>
///     <b>SupportsBulkOperations = false</b> — Graph has bulk endpoints
///     but the admin UI doesn't expose them. v0.3+.
///   </item>
/// </list>
/// </remarks>
internal static class EntraCapabilities
{
    public static UserBackendCapabilities Value { get; } = new()
    {
        SupportsLocalLogin = false,
        // SupportsRegistration covers two distinct paths in the v0.2 UI:
        // (a) end-user self-service signup at /visuauth/register, and
        // (b) admin-create at /admin/users/new. Entra can't do (a) — the
        // tenant-level signup flow is Microsoft's — but (b) IS supported
        // through Graph's POST /users (which EntraUserStore.CreateAsync
        // already implements). Flipping to true unblocks the admin
        // surface; the end-user /register page still resolves to
        // UserResult.Failure via EntraAuthenticationFlow.RegisterAsync,
        // so the "self-service" half stays honest. v0.3 splits this into
        // a dedicated SupportsAdminUserCreation capability.
        SupportsRegistration = true,
        SupportsPasswordReset = true,
        SupportsTwoFactor = false,
        // EntraUserStore.ResetTwoFactorAsync deletes the user's registered
        // authentication methods via Graph (per-subtype typed DELETE,
        // shared with the External adapter through EntraTwoFactorReset), so
        // the admin "reset 2FA" button surfaces and works in Entra mode.
        // Needs UserAuthenticationMethod.ReadWrite.All on the app.
        SupportsTwoFactorReset = true,
        SupportsLockout = false,
        SupportsEmailConfirmation = false,
        SupportsRoleManagement = true,
        // App roles are declared in the app-registration manifest, not at
        // runtime — EntraRoleStore.Create/Rename/Delete throw NotSupported.
        // Flagging false hides those controls on the admin Roles page so an
        // operator never submits a form that can't succeed.
        SupportsRoleMutation = false,
        SupportsSessionRevocation = true,
        SupportsExternalProviders = false,
        SupportsImpersonation = false,
        SupportsCustomClaims = true,
        SupportsAuditLog = true,
        SupportsBulkOperations = false,
    };
}
