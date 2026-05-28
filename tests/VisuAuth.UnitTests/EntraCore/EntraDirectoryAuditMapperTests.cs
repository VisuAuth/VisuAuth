using FluentAssertions;
using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.EntraCore.Auditing;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Pure projection + OData filter logic for the directoryAudits source of
/// the Entra audit reader. Unlike sign-ins, the action is Entra's raw
/// <c>activityDisplayName</c> and the actor can be a user or an app.
/// </summary>
public sealed class EntraDirectoryAuditMapperTests
{
    [Fact]
    public void ToEntryView_UserInitiatedSuccess_MapsActorAndTargetAndActivity()
    {
        var audit = new DirectoryAudit
        {
            Id = "33333333-3333-3333-3333-333333333333",
            ActivityDateTime = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero),
            ActivityDisplayName = "Add user",
            Category = "UserManagement",
            Result = OperationResult.Success,
            InitiatedBy = new AuditActivityInitiator
            {
                User = new UserIdentity { Id = "admin-1", UserPrincipalName = "admin@contoso.com", IpAddress = "198.51.100.7" },
            },
            TargetResources = [new TargetResource { Type = "User", Id = "new-user-1", DisplayName = "New User" }],
        };

        var view = EntraDirectoryAuditMapper.ToEntryView(audit);

        view.Action.Should().Be("Add user", "directory entries surface Entra's raw activity name");
        view.Outcome.Should().Be(AuditOutcome.Success);
        view.TargetType.Should().Be("User");
        view.TargetId.Should().Be("new-user-1");
        view.TargetLabel.Should().Be("New User");
        view.ActorUserId.Should().Be("admin-1");
        view.ActorEmail.Should().Be("admin@contoso.com");
        view.ActorIpAddress.Should().Be("198.51.100.7");
        view.Timestamp.Should().Be(new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero));
        view.PayloadJson.Should().Contain("UserManagement");
    }

    [Fact]
    public void ToEntryView_AppInitiated_UsesAppDisplayNameAsActor()
    {
        var audit = new DirectoryAudit
        {
            Id = Guid.NewGuid().ToString(),
            ActivityDisplayName = "Update application",
            Result = OperationResult.Success,
            InitiatedBy = new AuditActivityInitiator { App = new AppIdentity { DisplayName = "Provisioning Service" } },
        };

        EntraDirectoryAuditMapper.ToEntryView(audit).ActorEmail.Should().Be("Provisioning Service",
            "app-initiated activities fall back to the app display name for the actor label");
    }

    [Fact]
    public void ToEntryView_FailureResult_MapsToFailureWithReason()
    {
        var audit = new DirectoryAudit
        {
            Id = Guid.NewGuid().ToString(),
            ActivityDisplayName = "Add member to role",
            Result = OperationResult.Failure,
            ResultReason = "Insufficient privileges.",
        };

        var view = EntraDirectoryAuditMapper.ToEntryView(audit);
        view.Outcome.Should().Be(AuditOutcome.Failure);
        view.FailureReason.Should().Be("Insufficient privileges.");
    }

    [Fact]
    public void ToEntryView_NullResult_TreatedAsSuccess()
    {
        var view = EntraDirectoryAuditMapper.ToEntryView(new DirectoryAudit { Id = Guid.NewGuid().ToString(), Result = null });
        view.Outcome.Should().Be(AuditOutcome.Success);
    }

    [Fact]
    public void ToEntryView_NoTargetResources_FallsBackToDirectoryObject()
    {
        var view = EntraDirectoryAuditMapper.ToEntryView(new DirectoryAudit { Id = Guid.NewGuid().ToString(), ActivityDisplayName = "x" });
        view.TargetType.Should().Be("DirectoryObject");
        view.TargetId.Should().BeNull();
    }

    [Fact]
    public void ToEntryView_NonGuidId_StillProducesNonEmptyGuid()
    {
        EntraDirectoryAuditMapper.ToEntryView(new DirectoryAudit { Id = "Directory_xyz_123" })
            .Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void BuildListFilter_Empty_ReturnsNull()
    {
        EntraDirectoryAuditMapper.BuildListFilter(new AuditFilter()).Should().BeNull();
    }

    [Fact]
    public void BuildListFilter_DateRange_UsesActivityDateTime()
    {
        var result = EntraDirectoryAuditMapper.BuildListFilter(new AuditFilter
        {
            From = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
        });

        result.Should().Contain("activityDateTime ge 2026-05-01T00:00:00Z");
        result.Should().Contain("activityDateTime le 2026-05-08T00:00:00Z");
    }

    [Theory]
    [InlineData(AuditOutcome.Success, "result eq 'success'")]
    [InlineData(AuditOutcome.Failure, "result eq 'failure'")]
    public void BuildListFilter_Outcome_MapsToResultPredicate(AuditOutcome outcome, string expected)
    {
        EntraDirectoryAuditMapper.BuildListFilter(new AuditFilter { Outcome = outcome })
            .Should().Be(expected);
    }

    [Fact]
    public void BuildListFilter_Action_MatchesActivityDisplayName_Escaped()
    {
        EntraDirectoryAuditMapper.BuildListFilter(new AuditFilter { Action = "Add user's role" })
            .Should().Be("activityDisplayName eq 'Add user''s role'");
    }
}
