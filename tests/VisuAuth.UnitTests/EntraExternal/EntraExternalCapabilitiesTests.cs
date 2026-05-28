using FluentAssertions;
using VisuAuth.EntraExternal;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Locks in the External adapter's capability declarations. Each flag
/// drives a UI surface (login form vs hosted-Microsoft hint, providers
/// section, etc.), so a regression is a visible product-behaviour change
/// — pinning the table here makes the change show up in review.
/// </summary>
public sealed class EntraExternalCapabilitiesTests
{
    [Fact]
    public void LocalLogin_IsFalse_SoEndUserPagesSwapToSignInWithMicrosoft()
    {
        EntraExternalCapabilities.Value.SupportsLocalLogin.Should().BeFalse(
            "headline flip — false hides the password form on /visuauth/login in favour of the Microsoft sign-in hint");
    }

    [Fact]
    public void Registration_IsTrue_BecauseAdminCreateUserGoesThroughPostUsers()
    {
        // Same dual-meaning as Workforce in v0.3: covers both admin-create
        // (works via Graph POST /users with identities[]) AND end-user
        // self-service signup (still hosted by Microsoft; PR-C wires the
        // OIDC redirect to the hosted page). Flipping false now would
        // hide /admin/users/new — the wrong outcome.
        EntraExternalCapabilities.Value.SupportsRegistration.Should().BeTrue();
    }

    [Fact]
    public void ExternalProviders_IsFalse_BecauseFederationIsHostedOnTheCiamLoginPage()
    {
        EntraExternalCapabilities.Value.SupportsExternalProviders.Should().BeFalse(
            "federated providers ARE supported by External ID, but they're configured at the tenant level and rendered by the hosted Microsoft login page — not by VisuAuth's providers admin section");
    }

    [Fact]
    public void PasswordResetAndRoleManagementAndSessionRevocation_AreSupported()
    {
        EntraExternalCapabilities.Value.SupportsPasswordReset.Should().BeTrue();
        EntraExternalCapabilities.Value.SupportsRoleManagement.Should().BeTrue();
        EntraExternalCapabilities.Value.SupportsSessionRevocation.Should().BeTrue();
    }

    [Fact]
    public void RoleMutation_IsFalse_BecauseAppRolesAreManifestDeclared()
    {
        EntraExternalCapabilities.Value.SupportsRoleMutation.Should().BeFalse(
            "same as Workforce — app roles are manifest-declared, so the admin Roles page hides create/rename/delete");
    }

    [Fact]
    public void Lockout_IsFalse_BecauseEntraSmartLockoutIsOpaque()
    {
        EntraExternalCapabilities.Value.SupportsLockout.Should().BeFalse(
            "Entra has smart lockout — the admin 'lock' surface maps via SetEnabled (accountEnabled) instead");
    }

    [Fact]
    public void TwoFactorReset_IsFalse_InV03_SharedLimitationWithWorkforce()
    {
        EntraExternalCapabilities.Value.SupportsTwoFactorReset.Should().BeFalse(
            "per-method DELETE in Graph needs typed builders per auth-method subtype; deferred to v0.4 with the Workforce adapter");
        EntraExternalCapabilities.Value.SupportsTwoFactor.Should().BeFalse(
            "multi-factor enrolment pages don't apply — External customers enrol via Microsoft's hosted surfaces");
    }

    [Fact]
    public void EmailDomainSuffix_NotSetOnTheStaticSingleton_StoreOverlaysItPerOptions()
    {
        // The static value never carries an EmailDomainSuffix — the store
        // overlays it from EntraExternalOptions.DefaultEmailDomain at
        // construction time. Keeping the singleton "policy-only" lets the
        // capability stay a pure const (Sonar / immutability friendly).
        EntraExternalCapabilities.Value.EmailDomainSuffix.Should().BeNull();
    }
}
