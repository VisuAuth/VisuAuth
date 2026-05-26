using FluentAssertions;
using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra.Mapping;
using Xunit;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Lock-down for the pure projection layer between Microsoft Graph
/// entities and VisuAuth DTOs. The mapper is where every "Entra calls
/// it X, VisuAuth surfaces it as Y" rule lives, so a regression here
/// changes what the admin UI shows — these tests make that change
/// visible in code review.
/// </summary>
public sealed class EntraUserMapperTests
{
    // Hoisted to satisfy CA1861 ("avoid allocating a fresh array each call")
    // — the fact data is the same shape across every roles-related test.
    private static readonly string[] TwoRoleArray = ["Admin", "Editor"];
    [Fact]
    public void ToSummary_MapsBasicFieldsAndDefaultsTwoFactorToFalse()
    {
        var user = new GraphUser
        {
            Id = "u-1",
            UserPrincipalName = "alice@contoso.com",
            Mail = "alice@contoso.com",
            BusinessPhones = ["+55 11 99999-9999"],
            AccountEnabled = true,
            CreatedDateTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SignInActivity = new SignInActivity
            {
                LastSignInDateTime = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            },
        };

        var summary = EntraUserMapper.ToSummary(user);

        summary.Id.Should().Be("u-1");
        summary.Email.Should().Be("alice@contoso.com");
        summary.UserName.Should().Be("alice@contoso.com");
        summary.PhoneNumber.Should().Be("+55 11 99999-9999");
        summary.IsEnabled.Should().BeTrue();
        summary.EmailConfirmed.Should().BeTrue("Entra validates UPN at directory-creation time");
        summary.TwoFactorEnabled.Should().BeFalse("the User entity itself doesn't carry 2FA — needs /authentication/methods");
        summary.LastSignInAt.Should().Be(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToSummary_FallsBackToUpn_WhenMailIsMissing()
    {
        var user = new GraphUser
        {
            Id = "u-2",
            UserPrincipalName = "bob@contoso.com",
            Mail = null,
        };

        var summary = EntraUserMapper.ToSummary(user);

        summary.Email.Should().Be("bob@contoso.com",
            "service accounts often lack a published mail — UPN is the next best identity surface");
    }

    [Fact]
    public void ToSummary_AccountEnabledNull_DefaultsToTrue()
    {
        var user = new GraphUser { Id = "u-3", AccountEnabled = null };

        EntraUserMapper.ToSummary(user).IsEnabled.Should().BeTrue(
            "a newly-fetched user with null accountEnabled is typically a freshly-created row — default to enabled rather than misreport as locked");
    }

    [Fact]
    public void ToDetail_MapsRolesArgument_AndLeavesClaimsExternalLoginsEmpty()
    {
        var user = new GraphUser
        {
            Id = "u-4",
            UserPrincipalName = "carol@contoso.com",
            Mail = "carol@contoso.com",
            BusinessPhones = ["+1-555-0100"],
            AccountEnabled = false,
        };

        var detail = EntraUserMapper.ToDetail(user, TwoRoleArray);

        detail.Roles.Should().BeEquivalentTo(TwoRoleArray);
        detail.Claims.Should().BeEmpty("v0.2 doesn't surface Graph extension properties yet");
        detail.ExternalLogins.Should().BeEmpty("Entra IS the external IdP — listing it would be circular");
        detail.IsEnabled.Should().BeFalse();
        detail.PhoneNumberConfirmed.Should().BeTrue("presence of a businessPhone is the closest Entra equivalent");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToGraphCreate_GeneratesTemporaryPassword_WhenCommandPasswordIsMissing(string? supplied)
    {
        var command = new CreateUserCommand
        {
            Email = "new.user@contoso.com",
            UserName = "New User",
            Password = supplied,
        };
        var (graphUser, tempPassword) = EntraUserMapper.ToGraphCreate(command, () => "GENERATED-PWD-123");

        tempPassword.Should().Be("GENERATED-PWD-123");
        graphUser.PasswordProfile!.Password.Should().Be("GENERATED-PWD-123");
        graphUser.PasswordProfile.ForceChangePasswordNextSignIn.Should().BeTrue(
            "the generated-password path forces a rotation on first sign-in");
    }

    [Fact]
    public void ToGraphCreate_PreservesCallerPassword_AndDoesNotForceChange()
    {
        var command = new CreateUserCommand
        {
            Email = "x@y.com",
            Password = "ExplicitPassword!1",
        };
        var (graphUser, tempPassword) = EntraUserMapper.ToGraphCreate(command, () => "should-not-be-called");

        tempPassword.Should().Be("ExplicitPassword!1");
        graphUser.PasswordProfile!.ForceChangePasswordNextSignIn.Should().BeFalse(
            "if the admin handed over an explicit password they own the rotation policy");
    }

    [Fact]
    public void ToGraphCreate_NoPhone_StillSetsBusinessPhonesAsEmptyList_NotNull()
    {
        // Graph types businessPhones as `Collection(Edm.String)[Nullable=False]`
        // — a null on the wire triggers a 400 ("does not allow null
        // values"). The mapper must serialise "no phones" as the empty
        // list, not as a missing property.
        var (graphUser, _) = EntraUserMapper.ToGraphCreate(
            new CreateUserCommand { Email = "x@y.com", PhoneNumber = null },
            () => "pwd");

        graphUser.BusinessPhones.Should().NotBeNull("Graph rejects null for non-nullable collection types");
        graphUser.BusinessPhones.Should().BeEmpty();
    }

    [Fact]
    public void ToGraphCreate_DerivesMailNicknameFromEmailLocalPart()
    {
        var command = new CreateUserCommand { Email = "olivia@contoso.com" };
        var (graphUser, _) = EntraUserMapper.ToGraphCreate(command, () => "x");
        graphUser.MailNickname.Should().Be("olivia");
        graphUser.UserPrincipalName.Should().Be("olivia@contoso.com");
        graphUser.AccountEnabled.Should().BeTrue("newly-created users default to enabled");
    }

    [Fact]
    public void ToGraphUpdate_LeavesUntouchedFieldsNull_SoGraphPatchKeepsThem()
    {
        var patch = EntraUserMapper.ToGraphUpdate(new UpdateUserCommand { UserName = "New display" });
        patch.DisplayName.Should().Be("New display");
        patch.UserPrincipalName.Should().BeNull();
        patch.Mail.Should().BeNull();
        patch.BusinessPhones.Should().BeNull("absent fields in the command must NOT translate to null on the wire — PATCH would clear them");
    }

    [Fact]
    public void ToGraphUpdate_WithEmail_DoesNotPatchUpnOrMail_BecauseGraphRejectsItForExternals()
    {
        // Graph rejects userPrincipalName / mail patches for B2B external
        // users (the {addr}#EXT#@{tenant} format) with HTTP 403, even when
        // User.ReadWrite.All is granted. The mapper deliberately drops
        // Email from the PATCH body so the generic admin "save" stays
        // safe — UPN / mail changes have to go through the Entra portal.
        var patch = EntraUserMapper.ToGraphUpdate(new UpdateUserCommand
        {
            Email = "new@contoso.com",
            UserName = "New display",
            PhoneNumber = "+5511",
        });
        patch.DisplayName.Should().Be("New display");
        patch.BusinessPhones.Should().ContainSingle().Which.Should().Be("+5511");
        patch.UserPrincipalName.Should().BeNull("UPN is read-only in Graph for B2B externals — see ToGraphUpdate remarks");
        patch.Mail.Should().BeNull("mail is server-managed");
    }

    [Fact]
    public void ToGraphUpdate_BlankPhoneClearsList()
    {
        var patch = EntraUserMapper.ToGraphUpdate(new UpdateUserCommand { PhoneNumber = "   " });
        patch.BusinessPhones.Should().NotBeNull().And.BeEmpty(
            "an explicit empty/whitespace phone is the admin signalling 'remove the number'");
    }

    [Fact]
    public void BuildGraphFilter_Empty_ReturnsNull_SoGraphIsNotPassedEmptyClause()
    {
        EntraUserMapper.BuildGraphFilter(new UserFilter()).Should().BeNull();
    }

    [Theory]
    [InlineData(true, "accountEnabled eq true")]
    [InlineData(false, "accountEnabled eq false")]
    public void BuildGraphFilter_MapsIsEnabledToAccountEnabled(bool enabled, string expected)
    {
        EntraUserMapper.BuildGraphFilter(new UserFilter { IsEnabled = enabled })
            .Should().Be(expected);
    }

    [Fact]
    public void BuildGraphFilter_LockedOutFlipsToAccountEnabledFalse()
    {
        EntraUserMapper.BuildGraphFilter(new UserFilter { IsLockedOut = true })
            .Should().Be("accountEnabled eq false",
                "IsLockedOut = true maps to accountEnabled = false in Entra-land");
    }

    [Fact]
    public void BuildGraphFilter_SearchEscapesSingleQuotes()
    {
        var filter = EntraUserMapper.BuildGraphFilter(new UserFilter { SearchTerm = "O'Reilly" });
        filter.Should().Contain("O''Reilly",
            "OData filter literals escape single quotes by doubling them");
    }

    [Fact]
    public void BuildGraphFilter_CombinesMultipleClauses_WithAnd()
    {
        var filter = EntraUserMapper.BuildGraphFilter(new UserFilter
        {
            SearchTerm = "alice",
            IsEnabled = true,
        });

        filter.Should().Contain("accountEnabled eq true").And.Contain("startswith");
        filter.Should().Contain(" and ");
    }
}
