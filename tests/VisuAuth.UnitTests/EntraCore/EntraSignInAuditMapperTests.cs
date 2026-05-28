using FluentAssertions;
using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.EntraCore.Auditing;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Pins the pure projection + OData filter logic of the Entra sign-in
/// audit reader (no Graph round-trip). The mapper is where every "Entra
/// signIn field → VisuAuth audit field" and "AuditFilter → $filter" rule
/// lives.
/// </summary>
public sealed class EntraSignInAuditMapperTests
{
    [Fact]
    public void ToEntryView_SuccessfulSignIn_MapsToLoginSucceeded()
    {
        var signIn = new SignIn
        {
            Id = "11111111-1111-1111-1111-111111111111",
            CreatedDateTime = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UserPrincipalName = "alice@contoso.com",
            UserId = "user-1",
            IpAddress = "203.0.113.5",
            AppDisplayName = "VisuAuth Sample",
            ClientAppUsed = "Browser",
            Status = new SignInStatus { ErrorCode = 0 },
        };

        var view = EntraSignInAuditMapper.ToEntryView(signIn);

        view.Id.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        view.Action.Should().Be(AuditActions.LoginSucceeded);
        view.Outcome.Should().Be(AuditOutcome.Success);
        view.FailureReason.Should().BeNull();
        view.TargetType.Should().Be(AuditTargetTypes.User);
        view.TargetId.Should().Be("user-1");
        view.TargetLabel.Should().Be("alice@contoso.com");
        view.ActorEmail.Should().Be("alice@contoso.com");
        view.ActorIpAddress.Should().Be("203.0.113.5");
        view.Timestamp.Should().Be(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        view.PayloadJson.Should().Contain("VisuAuth Sample").And.Contain("Browser");
    }

    [Fact]
    public void ToEntryView_FailedSignIn_MapsToLoginFailed_WithReason()
    {
        var signIn = new SignIn
        {
            Id = "22222222-2222-2222-2222-222222222222",
            CreatedDateTime = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero),
            UserPrincipalName = "bob@contoso.com",
            Status = new SignInStatus { ErrorCode = 50126, FailureReason = "Invalid username or password." },
        };

        var view = EntraSignInAuditMapper.ToEntryView(signIn);

        view.Action.Should().Be(AuditActions.LoginFailed);
        view.Outcome.Should().Be(AuditOutcome.Failure);
        view.FailureReason.Should().Be("Invalid username or password.");
    }

    [Fact]
    public void ToEntryView_NullStatus_TreatedAsSuccess()
    {
        var view = EntraSignInAuditMapper.ToEntryView(new SignIn { Id = Guid.NewGuid().ToString(), Status = null });
        view.Outcome.Should().Be(AuditOutcome.Success, "absent status / zero error code is a successful sign-in");
    }

    [Fact]
    public void ToEntryView_NonGuidId_StillProducesANonEmptyGuid()
    {
        var view = EntraSignInAuditMapper.ToEntryView(new SignIn { Id = "not-a-guid" });
        view.Id.Should().NotBe(Guid.Empty, "the row key falls back to a fresh GUID when Graph's id isn't parseable");
    }

    [Fact]
    public void ToEntryView_NoApp_OmitsPayload()
    {
        var view = EntraSignInAuditMapper.ToEntryView(new SignIn { Id = Guid.NewGuid().ToString() });
        view.PayloadJson.Should().BeNull("no app / client-app context → no payload badge");
    }

    [Fact]
    public void BuildListFilter_Empty_ReturnsNull()
    {
        EntraSignInAuditMapper.BuildListFilter(new AuditFilter()).Should().BeNull();
    }

    [Fact]
    public void BuildListFilter_DateRange_FormatsIso8601Utc()
    {
        var filter = new AuditFilter
        {
            From = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
        };

        var result = EntraSignInAuditMapper.BuildListFilter(filter);

        result.Should().Contain("createdDateTime ge 2026-05-01T00:00:00Z");
        result.Should().Contain("createdDateTime le 2026-05-08T00:00:00Z");
        result.Should().Contain(" and ");
    }

    [Fact]
    public void BuildListFilter_ActorSearch_UsesStartswith_AndEscapesQuotes()
    {
        var result = EntraSignInAuditMapper.BuildListFilter(new AuditFilter { ActorSearch = "O'Brien" });
        result.Should().Contain("startswith(userPrincipalName,'O''Brien')",
            "OData literals escape a single quote by doubling it");
    }

    [Theory]
    [InlineData(AuditOutcome.Success, "status/errorCode eq 0")]
    [InlineData(AuditOutcome.Failure, "status/errorCode ne 0")]
    public void BuildListFilter_Outcome_MapsToErrorCodePredicate(AuditOutcome outcome, string expected)
    {
        EntraSignInAuditMapper.BuildListFilter(new AuditFilter { Outcome = outcome })
            .Should().Be(expected);
    }

    [Fact]
    public void BuildListFilter_LoginFailedAction_ImpliesFailureOutcome()
    {
        EntraSignInAuditMapper.BuildListFilter(new AuditFilter { Action = AuditActions.LoginFailed })
            .Should().Be("status/errorCode ne 0");
    }

    [Fact]
    public void BuildCountFilter_LoginSucceeded_FiltersSuccessInRange()
    {
        var result = EntraSignInAuditMapper.BuildCountFilter(
            AuditActions.LoginSucceeded,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero));

        result.Should().Contain("status/errorCode eq 0");
        result.Should().Contain("createdDateTime ge 2026-05-01T00:00:00Z");
        result.Should().Contain("createdDateTime le 2026-05-08T00:00:00Z");
    }

    [Fact]
    public void BuildCountFilter_LoginFailed_FiltersFailure()
    {
        EntraSignInAuditMapper.BuildCountFilter(AuditActions.LoginFailed, default, default)
            .Should().Contain("status/errorCode ne 0");
    }

    [Fact]
    public void BuildCountFilter_NonLoginAction_ReturnsNull()
    {
        // Only the login codes are backed by sign-ins; anything else can't
        // be counted from this source.
        EntraSignInAuditMapper.BuildCountFilter(AuditActions.UserCreated, default, default)
            .Should().BeNull();
    }
}
