using FluentAssertions;
using VisuAuth.Entra;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Locks in the Entra adapter's capability declarations. Each flag drives a
/// UI surface (login form vs Microsoft button, locked-user tile, etc.), so
/// a regression is a visible product-behaviour change — these tests pin the
/// table so the change shows up in review.
/// </summary>
public sealed class EntraCapabilitiesTests
{
    [Fact]
    public void LocalLogin_IsFalse_SoEndUserPagesSwapToSignInWithMicrosoft()
    {
        EntraCapabilities.Value.SupportsLocalLogin.Should().BeFalse(
            "this is the headline capability flip — false triggers the 'Sign in with Microsoft' button on /visuauth/login");
    }

    [Fact]
    public void Registration_IsTrue_BecauseAdminCreateUserGoesThroughPostUsers()
    {
        // v0.2 SupportsRegistration intentionally covers both end-user
        // self-service signup AND admin-create. Entra can't do the
        // former (Microsoft owns the tenant signup flow), but the
        // latter works via Graph POST /users — keeping the capability
        // true unblocks the admin "Criar usuário" page. The end-user
        // /register page still resolves to UserResult.Failure via
        // EntraAuthenticationFlow.RegisterAsync, so the "self-service"
        // half stays honest from a behaviour standpoint. v0.3 splits
        // these into separate capabilities.
        EntraCapabilities.Value.SupportsRegistration.Should().BeTrue();
    }

    [Fact]
    public void ExternalProviders_IsFalse_BecauseEntraIsTheIdP()
    {
        EntraCapabilities.Value.SupportsExternalProviders.Should().BeFalse(
            "Entra IS the IdP — the providers admin page would be circular");
    }

    [Fact]
    public void PasswordResetAndRoleManagementAndSessionRevocation_AreSupported()
    {
        EntraCapabilities.Value.SupportsPasswordReset.Should().BeTrue();
        EntraCapabilities.Value.SupportsRoleManagement.Should().BeTrue();
        EntraCapabilities.Value.SupportsSessionRevocation.Should().BeTrue();
    }

    [Fact]
    public void RoleMutation_IsFalse_BecauseAppRolesAreManifestDeclared()
    {
        EntraCapabilities.Value.SupportsRoleMutation.Should().BeFalse(
            "app roles are declared in the app-registration manifest, not at runtime — the admin Roles page hides create/rename/delete so EntraRoleStore's NotSupported throw is never reached from the UI");
    }

    [Fact]
    public void Lockout_IsFalse_BecauseEntraOwnsItInternally()
    {
        EntraCapabilities.Value.SupportsLockout.Should().BeFalse(
            "Entra has smart lockout — the admin 'lock' surface is mapped via SetEnabled (accountEnabled) instead");
    }

    [Fact]
    public void TwoFactorReset_IsTrue_AdminCanWipeAuthenticationMethods()
    {
        EntraCapabilities.Value.SupportsTwoFactorReset.Should().BeTrue(
            "EntraUserStore.ResetTwoFactorAsync deletes the user's registered auth methods via Graph (per-subtype DELETE)");
        EntraCapabilities.Value.SupportsTwoFactor.Should().BeFalse(
            "TOTP setup pages still don't apply — Entra users enrol authenticators through Microsoft's own UX");
    }
}
