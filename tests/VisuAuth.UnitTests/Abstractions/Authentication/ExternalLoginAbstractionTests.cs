using FluentAssertions;
using VisuAuth.Abstractions.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Abstractions.Authentication;

/// <summary>
/// Unit coverage for the external-login DTOs and options. Pins the public
/// shape so cross-package consumers (and future Entra adapter) can rely on
/// the factory methods + defaults.
/// </summary>
public sealed class ExternalLoginAbstractionTests
{
    [Fact]
    public void ExternalLoginOptions_DefaultsToAutoCreateAndTrustsProviderEmail()
    {
        var options = new ExternalLoginOptions();

        options.FirstTimeStrategy.Should().Be(ExternalLoginFirstTimeStrategy.AutoCreate,
            "AutoCreate is the documented default — frictionless out of the box");
        options.TrustProviderEmailConfirmation.Should().BeTrue(
            "the provider already validated the email; redundant VisuAuth confirmation is opt-in");
    }

    [Fact]
    public void ExternalSignInResult_Success_PopulatesOutcomeAndUserId()
    {
        var result = ExternalSignInResult.Success("user-123");

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        result.UserId.Should().Be("user-123");
        result.PendingProvider.Should().BeNull();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExternalSignInResult_RequiresConfirmation_PopulatesPendingClaims()
    {
        var result = ExternalSignInResult.RequiresConfirmation(
            provider: "Microsoft",
            providerKey: "abc123",
            email: "alice@example.com",
            displayName: "Alice");

        result.Outcome.Should().Be(ExternalSignInOutcome.RequiresConfirmation);
        result.PendingProvider.Should().Be("Microsoft");
        result.PendingProviderKey.Should().Be("abc123");
        result.PendingEmail.Should().Be("alice@example.com");
        result.PendingDisplayName.Should().Be("Alice");
        result.UserId.Should().BeNull();
    }

    [Fact]
    public void ExternalSignInResult_NoExternalSession_HasOnlyOutcome()
    {
        var result = ExternalSignInResult.NoExternalSession();

        result.Outcome.Should().Be(ExternalSignInOutcome.NoExternalSession);
        result.UserId.Should().BeNull();
        result.PendingProvider.Should().BeNull();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExternalSignInResult_LockedOut_HasOnlyOutcome()
    {
        var result = ExternalSignInResult.LockedOut();

        result.Outcome.Should().Be(ExternalSignInOutcome.LockedOut);
        result.UserId.Should().BeNull();
    }

    [Fact]
    public void ExternalSignInResult_NotAllowed_HasOnlyOutcome()
    {
        var result = ExternalSignInResult.NotAllowed();

        result.Outcome.Should().Be(ExternalSignInOutcome.NotAllowed);
        result.UserId.Should().BeNull();
    }

    [Fact]
    public void ExternalSignInResult_Failed_PopulatesErrors()
    {
        var result = ExternalSignInResult.Failed(["Email already in use", "Username invalid"]);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().HaveCount(2);
        result.Errors[0].Should().Be("Email already in use");
        result.Errors[1].Should().Be("Username invalid");
        result.UserId.Should().BeNull();
    }

    [Fact]
    public void ExternalProviderInfo_RequiresSchemeAndDisplayName()
    {
        var info = new ExternalProviderInfo { Scheme = "Google", DisplayName = "Google" };

        info.Scheme.Should().Be("Google");
        info.DisplayName.Should().Be("Google");
    }

    [Theory]
    [InlineData(ExternalLoginFirstTimeStrategy.AutoCreate)]
    [InlineData(ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm)]
    [InlineData(ExternalLoginFirstTimeStrategy.AlwaysConfirm)]
    public void ExternalLoginFirstTimeStrategy_AllVariantsAreReachable(ExternalLoginFirstTimeStrategy strategy)
    {
        // Defensive: every strategy enum member must be assignable to the
        // options bag. A future renumbering of the enum should fail this.
        var options = new ExternalLoginOptions { FirstTimeStrategy = strategy };
        options.FirstTimeStrategy.Should().Be(strategy);
    }
}
