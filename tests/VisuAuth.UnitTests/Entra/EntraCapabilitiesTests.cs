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
    public void RegistrationAndExternalProviders_AreFalse_BecauseEntraOwnsBothSurfaces()
    {
        EntraCapabilities.Value.SupportsRegistration.Should().BeFalse();
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
    public void Lockout_IsFalse_BecauseEntraOwnsItInternally()
    {
        EntraCapabilities.Value.SupportsLockout.Should().BeFalse(
            "Entra has smart lockout — the admin 'lock' surface is mapped via SetEnabled (accountEnabled) instead");
    }

    [Fact]
    public void TwoFactorReset_IsFalse_InV02_DocumentedScopeLimitation()
    {
        EntraCapabilities.Value.SupportsTwoFactorReset.Should().BeFalse(
            "per-method DELETE in Graph requires a typed builder per auth-method subtype; deferred to v0.3");
        EntraCapabilities.Value.SupportsTwoFactor.Should().BeFalse(
            "TOTP setup pages don't apply — Entra users enrol authenticators through Microsoft's own UX");
    }
}
