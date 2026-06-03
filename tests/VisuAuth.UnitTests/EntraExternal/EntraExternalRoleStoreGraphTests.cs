using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.EntraExternal;
using VisuAuth.EntraExternal.Configuration;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Graph-touching paths of <see cref="EntraExternalRoleStore"/>, mocked
/// via <see cref="FakeGraphHandler"/>. Parallels the Workforce
/// <c>EntraRoleStoreGraphTests</c> — same auth-free HttpClient swap,
/// same canned JSON pattern. The app-role Graph contract is identical
/// across tenant families so the assertions are direct copies of the
/// Workforce ones, swapped to the External store + options types.
/// </summary>
public sealed class EntraExternalRoleStoreGraphTests
{
    private const string ServicePrincipalJson = """
        {
          "value": [
            {
              "id": "00000000-0000-0000-0000-0000000000aa",
              "appRoles": [
                {"id":"11111111-1111-1111-1111-111111111111","displayName":"Admin","value":"admin","isEnabled":true},
                {"id":"22222222-2222-2222-2222-222222222222","displayName":"Editor","value":"editor","isEnabled":true}
              ]
            }
          ]
        }
        """;

    private const string EmptyServicePrincipalJson = """{ "value": [] }""";

    [Fact]
    public async Task ListAsync_ProjectsAppRolesFromServicePrincipal_WithMemberCounts()
    {
        // Order matters: FakeGraphHandler picks the FIRST matching route by
        // substring, so the more specific path (.../appRoleAssignedTo) must
        // be registered before the parent prefix (/servicePrincipals).
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals/00000000-0000-0000-0000-0000000000aa/appRoleAssignedTo", """
                {
                  "value": [
                    {"id":"a-1","appRoleId":"11111111-1111-1111-1111-111111111111","principalId":"00000000-0000-0000-0000-000000000099","resourceId":"00000000-0000-0000-0000-0000000000aa"},
                    {"id":"a-2","appRoleId":"11111111-1111-1111-1111-111111111111","principalId":"00000000-0000-0000-0000-0000000000bb","resourceId":"00000000-0000-0000-0000-0000000000aa"},
                    {"id":"a-3","appRoleId":"22222222-2222-2222-2222-222222222222","principalId":"00000000-0000-0000-0000-0000000000cc","resourceId":"00000000-0000-0000-0000-0000000000aa"}
                  ]
                }
                """)
            .SetupGet("/servicePrincipals", ServicePrincipalJson);

        var roles = await BuildStore(handler).ListAsync(tenantId: null);

        roles.Should().HaveCount(2);
        roles.Should().Contain(r => r.Name == "Admin" && r.MemberCount == 2);
        roles.Should().Contain(r => r.Name == "Editor" && r.MemberCount == 1);
    }

    [Fact]
    public async Task ListAsync_WhenServicePrincipalMissing_ReturnsEmpty()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", EmptyServicePrincipalJson);
        (await BuildStore(handler).ListAsync(null)).Should().BeEmpty(
            "no target service principal means we can't surface any roles — fail open");
    }

    [Fact]
    public async Task ListAsync_OnGraphError_DegradesGracefullyToEmpty()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/servicePrincipals", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "missing Application.Read.All");
        (await BuildStore(handler).ListAsync(null)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_FoundGuid_ReturnsRoleSummary()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", ServicePrincipalJson);
        var role = await BuildStore(handler).GetAsync("11111111-1111-1111-1111-111111111111");
        role.Should().NotBeNull();
        role!.Name.Should().Be("Admin");
    }

    [Fact]
    public async Task GetAsync_UnknownGuid_ReturnsNull()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", ServicePrincipalJson);
        (await BuildStore(handler).GetAsync("99999999-9999-9999-9999-999999999999")).Should().BeNull();
    }

    [Fact]
    public async Task GetRolesForUserAsync_ResolvesNamesFromIds()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupGet("/users/u-1/appRoleAssignments", """
                {
                  "value": [
                    {"id":"a-1","appRoleId":"11111111-1111-1111-1111-111111111111","principalId":"00000000-0000-0000-0000-000000000099","resourceId":"00000000-0000-0000-0000-0000000000aa"}
                  ]
                }
                """);

        var roles = await BuildStore(handler).GetRolesForUserAsync("u-1");
        roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Fact]
    public async Task GetRolesForUserAsync_OnGraphError_ReturnsEmptyInsteadOfThrowing()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupError(HttpMethod.Get, "/users/u-1/appRoleAssignments", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "x");
        (await BuildStore(handler).GetRolesForUserAsync("u-1")).Should().BeEmpty();
    }

    [Fact]
    public async Task AssignRoleAsync_PostsAppRoleAssignment_OnHappyPath()
    {
        // userId must be a valid GUID — the assign path Guid.Parse's it
        // to fill PrincipalId on the POST body.
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupPostJson("/users/00000000-0000-0000-0000-000000000099/appRoleAssignments",
                """{"id":"new-assign","appRoleId":"11111111-1111-1111-1111-111111111111"}""");

        var result = await BuildStore(handler).AssignRoleAsync(
            "00000000-0000-0000-0000-000000000099", "Admin");

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().Be("00000000-0000-0000-0000-000000000099");
    }

    [Fact]
    public async Task AssignRoleAsync_NonGuidUserId_ReturnsFailureWithFormatHint()
    {
        // Guid.Parse on a non-GUID userId throws FormatException, which
        // the store catches and surfaces as an actionable StoreResult.
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupPostJson("/users/not-a-guid/appRoleAssignments", "{}");
        var result = await BuildStore(handler).AssignRoleAsync("not-a-guid", "Admin");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("GUID");
    }

    [Fact]
    public async Task AssignRoleAsync_UnknownRoleName_ReturnsFailure()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", ServicePrincipalJson);
        var result = await BuildStore(handler).AssignRoleAsync(
            "00000000-0000-0000-0000-000000000099", "DoesNotExist");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("DoesNotExist");
    }

    [Fact]
    public async Task RemoveRoleAsync_DeletesMatchingAssignment_AndReturnsSuccess()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupGet("/users/u-1/appRoleAssignments", """
                {
                  "value": [
                    {"id":"a-1","appRoleId":"11111111-1111-1111-1111-111111111111","principalId":"00000000-0000-0000-0000-000000000099","resourceId":"00000000-0000-0000-0000-0000000000aa"}
                  ]
                }
                """)
            .SetupDelete("/users/u-1/appRoleAssignments/a-1");
        (await BuildStore(handler).RemoveRoleAsync("u-1", "Admin")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveRoleAsync_NoMatchingAssignment_IsIdempotentNoop()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupGet("/users/u-1/appRoleAssignments", """{ "value": [] }""");
        var result = await BuildStore(handler).RemoveRoleAsync("u-1", "Admin");
        result.IsSuccess.Should().BeTrue("removing a role the user doesn't have is a no-op, not an error");
        result.Metadata.Should().ContainKey("noop");
    }

    [Fact]
    public async Task RemoveRoleAsync_UnknownRole_ReturnsFailure()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", ServicePrincipalJson);
        var result = await BuildStore(handler).RemoveRoleAsync("u-1", "DoesNotExist");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("DoesNotExist");
    }

    [Fact]
    public async Task AssignRoleAsync_WhenServicePrincipalMissing_ReturnsFailure()
    {
        var handler = new FakeGraphHandler().SetupGet("/servicePrincipals", EmptyServicePrincipalJson);

        var result = await BuildStore(handler).AssignRoleAsync(
            "00000000-0000-0000-0000-000000000099", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("service principal");
    }

    [Fact]
    public async Task AssignRoleAsync_OnGraphError_SurfacesGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupError(HttpMethod.Post, "/users/00000000-0000-0000-0000-000000000099/appRoleAssignments",
                HttpStatusCode.Forbidden, "Authorization_RequestDenied", "missing AppRoleAssignment.ReadWrite.All");

        var result = await BuildStore(handler).AssignRoleAsync(
            "00000000-0000-0000-0000-000000000099", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("AppRoleAssignment.ReadWrite.All");
    }

    [Fact]
    public async Task RemoveRoleAsync_OnGraphError_SurfacesGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/servicePrincipals", ServicePrincipalJson)
            .SetupError(HttpMethod.Get, "/users/u-1/appRoleAssignments",
                HttpStatusCode.Forbidden, "Authorization_RequestDenied", "missing Directory.Read.All");

        var result = await BuildStore(handler).RemoveRoleAsync("u-1", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Directory.Read.All");
    }

    private static EntraExternalRoleStore BuildStore(FakeGraphHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        var graph = new GraphServiceClient(adapter);
        var options = Options.Create(new EntraExternalOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            ClientSecret = "fake",
            TenantDomain = "contoso.onmicrosoft.com",
            AppRoleResourceId = "00000000-0000-0000-0000-000000000003",
        });
        return new EntraExternalRoleStore(graph, options);
    }
}
