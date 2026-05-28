using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EntraExternal;

/// <summary>
/// Single source of truth for what the Entra External ID adapter declares
/// it can do. Both <see cref="EntraExternalUserStore"/> and
/// <see cref="EntraExternalAuthenticationFlow"/> read from this so the
/// flags can never drift between the two facets of the adapter.
/// </summary>
/// <remarks>
/// <para>
/// The flag set parallels the Workforce
/// <c>VisuAuth.Entra.EntraCapabilities</c> with one nuance worth flagging
/// upfront: <see cref="UserBackendCapabilities.SupportsRegistration"/> is
/// still <c>true</c> for the <i>admin</i>-create path (admins onboarding
/// internal support / preview users via <c>/admin/users/new</c> works
/// through Graph <c>POST /users</c>). End-user self-service signup at
/// <c>/visuauth/register</c> is gated by Microsoft's hosted "User flows"
/// and the v0.3 PR-C work will wire it via <c>Microsoft.Identity.Web</c>
/// — until that lands, the
/// <see cref="EntraExternalAuthenticationFlow.RegisterAsync"/> shim
/// returns a graceful failure for direct API consumers.
/// </para>
/// <para>
/// Rationale per flag:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SupportsLocalLogin = false</b> — Microsoft owns the customer
///     login UX (the hosted page at <c>{tenant}.ciamlogin.com</c>). Same
///     headline flip as the Workforce adapter — triggers VisuAuth's
///     "Sign in with Microsoft" hint instead of the email / password
///     form (CLAUDE.md §6 + §1.2).
///   </item>
///   <item>
///     <b>SupportsRegistration = true</b> — admin-create through Graph
///     <c>POST /users</c> works (it's the
///     <see cref="EntraExternalUserStore.CreateAsync"/> happy path).
///     The end-user <c>/register</c> page also routes through this
///     capability in the v0.3 milestone but the actual flow is hosted
///     by Microsoft (configured under the tenant's "User flows" blade);
///     PR-C wires the redirect. v0.4+ may split this into a dedicated
///     <c>SupportsAdminUserCreation</c> capability if the two paths
///     need to diverge further.
///   </item>
///   <item>
///     <b>SupportsPasswordReset = true</b> — admin can rotate via Graph's
///     PATCH passwordProfile. End-user self-service reset is the tenant's
///     hosted SSPR flow; the admin UI doesn't ride that.
///   </item>
///   <item>
///     <b>SupportsTwoFactor = false</b> — multi-factor setup pages don't
///     apply; External customers enrol authenticators through Microsoft's
///     own UX.
///   </item>
///   <item>
///     <b>SupportsTwoFactorReset = false</b> — same v0.2 limitation as
///     the Workforce adapter: per-method DELETE in Graph requires a typed
///     builder per subtype. Slated for v0.4.
///   </item>
///   <item>
///     <b>SupportsLockout = false</b> — Entra uses smart lockout that
///     can't be flipped per-user from outside; admin "lock" = disable
///     account, mapped via
///     <see cref="VisuAuth.Abstractions.Users.IUserStore.SetEnabledAsync"/>.
///   </item>
///   <item>
///     <b>SupportsEmailConfirmation = false</b> — Microsoft validates
///     emails during the hosted signup flow; the admin doesn't surface
///     a separate confirm pipeline.
///   </item>
///   <item>
///     <b>SupportsRoleManagement = true</b> — app roles via the same
///     Graph endpoints as the Workforce adapter. Create / rename / delete
///     throw NotSupported (declared in the app manifest); list + assign
///     + remove work.
///   </item>
///   <item>
///     <b>SupportsSessionRevocation = true</b> — Graph
///     <c>revokeSignInSessions</c> invalidates every refresh token.
///   </item>
///   <item>
///     <b>SupportsExternalProviders = false</b> — federated providers
///     (Google, Facebook, Apple) ARE supported by External ID, but they
///     are configured at the tenant level and rendered by the hosted
///     Microsoft login page, not by VisuAuth's providers admin section.
///     Flipping this to true would render an empty providers admin
///     page — the wrong UX.
///   </item>
///   <item>
///     <b>SupportsImpersonation = false</b> — out of scope.
///   </item>
///   <item>
///     <b>SupportsCustomClaims = true</b> — Graph extension properties
///     (open + schema) AND the user-attribute collection External ID
///     ships with. v0.3 admin UI surfaces them in a follow-up.
///   </item>
///   <item>
///     <b>SupportsAuditLog = true</b> — Entra has its own auditLogs
///     endpoint. The flag hides the in-tenant EF audit-log section
///     (irrelevant for an External-only consumer that hasn't wired the
///     Identity adapter).
///   </item>
///   <item>
///     <b>SupportsBulkOperations = false</b> — out of scope. v0.4+.
///   </item>
/// </list>
/// </remarks>
internal static class EntraExternalCapabilities
{
    public static UserBackendCapabilities Value { get; } = new()
    {
        SupportsLocalLogin = false,
        SupportsRegistration = true,
        SupportsPasswordReset = true,
        SupportsTwoFactor = false,
        SupportsTwoFactorReset = false,
        SupportsLockout = false,
        SupportsEmailConfirmation = false,
        SupportsRoleManagement = true,
        // Same as Workforce: app roles are manifest-declared, so
        // EntraExternalRoleStore.Create/Rename/Delete throw NotSupported.
        // The admin Roles page hides those controls when this is false.
        SupportsRoleMutation = false,
        SupportsSessionRevocation = true,
        SupportsExternalProviders = false,
        SupportsImpersonation = false,
        SupportsCustomClaims = true,
        SupportsAuditLog = true,
        SupportsBulkOperations = false,
    };
}
