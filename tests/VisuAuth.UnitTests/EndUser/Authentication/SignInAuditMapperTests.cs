using FluentAssertions;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EndUserUi.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.Authentication;

/// <summary>
/// Locks in the outcome → audit-action table. These tests document the
/// canonical mapping so any future change to the codes shows up as a
/// concrete diff (and won't accidentally renumber existing entries on
/// consumer dashboards).
/// </summary>
public sealed class SignInAuditMapperTests
{
    [Theory]
    [InlineData(SignInOutcome.Success, AuditActions.LoginSucceeded, AuditOutcome.Success)]
    [InlineData(SignInOutcome.RequiresTwoFactor, AuditActions.LoginRequiresTwoFactor, AuditOutcome.Success)]
    [InlineData(SignInOutcome.LockedOut, AuditActions.LoginLockedOut, AuditOutcome.Failure)]
    [InlineData(SignInOutcome.NotAllowed, AuditActions.LoginFailed, AuditOutcome.Failure)]
    [InlineData(SignInOutcome.InvalidCredentials, AuditActions.LoginFailed, AuditOutcome.Failure)]
    public void FromOutcome_MapsKnownOutcome_ToExpectedActionAndOutcome(
        SignInOutcome outcome,
        string expectedAction,
        AuditOutcome expectedAuditOutcome)
    {
        var shape = SignInAuditMapper.FromOutcome(new SignInResult { Outcome = outcome });

        shape.Should().NotBeNull();
        shape!.Action.Should().Be(expectedAction);
        shape.Outcome.Should().Be(expectedAuditOutcome);
    }

    [Fact]
    public void FromOutcome_NotAllowed_PrefersResultErrorOverDefaultReason()
    {
        var shape = SignInAuditMapper.FromOutcome(new SignInResult
        {
            Outcome = SignInOutcome.NotAllowed,
            Error = "Email not confirmed",
        });

        shape!.FailureReason.Should().Be("Email not confirmed");
    }

    [Fact]
    public void FromOutcome_NotAllowed_FallsBackToGenericReason_WhenErrorIsNull()
    {
        var shape = SignInAuditMapper.FromOutcome(new SignInResult
        {
            Outcome = SignInOutcome.NotAllowed,
            Error = null,
        });

        shape!.FailureReason.Should().Be("Sign-in not allowed");
    }

    [Fact]
    public void FromOutcome_RedirectToExternalProvider_ReturnsNull_SoExternalLoginCanOwnAudit()
    {
        SignInAuditMapper
            .FromOutcome(new SignInResult { Outcome = SignInOutcome.RedirectToExternalProvider })
            .Should().BeNull(
                "this outcome is owned by /external-login/* — auditing here would double-log");
    }
}
