using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraExternal;
using VisuAuth.EntraExternal.Configuration;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Covers the Graph-touching paths of <see cref="EntraExternalUserStore"/>
/// by wiring a <see cref="FakeGraphHandler"/> in place of the real HTTP
/// transport. Parallels the Workforce
/// <c>EntraUserStoreGraphTests</c> in shape — same auth-free
/// HttpClient swap, same canned JSON pattern, plus the External-specific
/// <c>identities[]</c> shape on Create + read.
/// </summary>
public sealed class EntraExternalUserStoreGraphTests
{
    // Identities-bearing user — what an External tenant actually returns.
    // The cpim_ UPN is auto-generated; the customer-typed email lives in
    // identities[].issuerAssignedId. Read paths must surface the latter.
    private const string ExternalUserJson = """
        {
          "id": "u-1",
          "userPrincipalName": "cpim_abcdef@contoso.onmicrosoft.com",
          "mail": null,
          "displayName": "Alice",
          "accountEnabled": true,
          "identities": [
            {
              "signInType": "emailAddress",
              "issuer": "contoso.onmicrosoft.com",
              "issuerAssignedId": "alice@personal.example"
            }
          ],
          "businessPhones": ["+55 11 99999-9999"],
          "createdDateTime": "2026-01-01T00:00:00Z"
        }
        """;

    private const string ExternalUserListJson = """
        {
          "value": [
            {
              "id": "u-1",
              "userPrincipalName": "cpim_abc@contoso.onmicrosoft.com",
              "mail": null,
              "accountEnabled": true,
              "identities": [
                { "signInType": "emailAddress", "issuer": "contoso.onmicrosoft.com", "issuerAssignedId": "alice@personal.example" }
              ],
              "createdDateTime": "2026-01-01T00:00:00Z"
            },
            {
              "id": "u-2",
              "userPrincipalName": "cpim_def@contoso.onmicrosoft.com",
              "mail": null,
              "accountEnabled": false,
              "identities": [
                { "signInType": "emailAddress", "issuer": "contoso.onmicrosoft.com", "issuerAssignedId": "bob@personal.example" }
              ],
              "createdDateTime": "2026-02-01T00:00:00Z"
            }
          ]
        }
        """;

    [Fact]
    public async Task GetAsync_HappyPath_SurfacesIdentitiesEmail_NotCpimUpn()
    {
        var handler = new FakeGraphHandler().SetupGet("/users/u-1", ExternalUserJson);
        var sut = BuildStore(handler);

        var summary = await sut.GetAsync("u-1");

        summary.Should().NotBeNull();
        summary!.Id.Should().Be("u-1");
        summary.Email.Should().Be("alice@personal.example",
            "External summary must prefer the customer-typed identities[] email over the auto-generated cpim_ UPN");
        summary.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_OnNotFound_ReturnsNull_WithoutThrowing()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/missing", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "Resource 'missing' does not exist.");
        var sut = BuildStore(handler);

        (await sut.GetAsync("missing")).Should().BeNull(
            "the IUserStore contract returns null when the user is absent, not throw");
    }

    [Fact]
    public async Task GetDetailAsync_FetchesUser_ReturnsDetail_EvenWhenRoleResolutionEmpty()
    {
        // GetDetailAsync makes 3 calls under the hood: get user, get
        // appRoleAssignments for the user, get servicePrincipal by appId.
        // Empty assignments + empty service principal is the path most
        // External tenants land in by default (app roles are opt-in via
        // the manifest), and exercising it covers ResolveRolesAsync's
        // empty-collection branch.
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1", ExternalUserJson)
            .SetupGet("/users/u-1/appRoleAssignments", """{ "value": [] }""")
            .SetupGet("/servicePrincipals", """{ "value": [] }""");
        var sut = BuildStore(handler);

        var detail = await sut.GetDetailAsync("u-1");

        detail.Should().NotBeNull();
        detail!.Id.Should().Be("u-1");
        detail.Email.Should().Be("alice@personal.example",
            "External detail must surface the customer-typed identities[] email");
        detail.Roles.Should().BeEmpty("no assignments fixture means no roles");
    }

    [Fact]
    public async Task GetDetailAsync_OnNotFound_ReturnsNull_WithoutThrowing()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/missing", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "x");
        var sut = BuildStore(handler);

        (await sut.GetDetailAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_BuildsIdentitiesAwareFilterAndReturnsMappedSummaries()
    {
        var handler = new FakeGraphHandler().SetupGet("/users", ExternalUserListJson);
        var sut = BuildStore(handler);

        var page = await sut.ListAsync(new UserFilter { SearchTerm = "alice", PageSize = 50 });

        page.Items.Should().HaveCount(2);
        page.Items[0].Email.Should().Be("alice@personal.example",
            "list projection prefers identities over cpim UPN, same as the detail/summary paths");
        page.Items[1].IsEnabled.Should().BeFalse("bob's accountEnabled is false in the fixture");
        page.TotalCount.Should().BeNull("Graph doesn't return a cheap total alongside a page");
        page.NextCursor.Should().BeNull("the fixture has no @odata.nextLink, so this is the last page");

        var listRequest = handler.RecordedRequests.Single();
        var decoded = Uri.UnescapeDataString(listRequest.RequestUri!.Query);
        decoded.Should().Contain("$filter=", "search term must push down as $filter");
        decoded.Should().Contain("identities/any",
            "the External adapter's search must include the identities/* predicate so customer-typed emails are findable");
        listRequest.Headers.GetValues("ConsistencyLevel").Single().Should().Be("eventual",
            "identities/any (and other advanced filter capabilities) require eventual consistency");
    }

    [Fact]
    public async Task ListAsync_OnGraphError_LogsAndReturnsEmptyPage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "Insufficient privileges.");
        var sut = BuildStore(handler);

        var page = await sut.ListAsync(new UserFilter());

        page.Items.Should().BeEmpty(
            "Graph failures degrade gracefully — empty page beats a 500 for the admin UI");
        page.NextCursor.Should().BeNull();
        page.TotalCount.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WhenGraphReturnsNextLink_ExposesCursor_AndFollowingItReplaysTheSkiptokenUrl()
    {
        const string firstPageJson = """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/users?$skiptoken=EXT_OPAQUE_456",
              "value": [
                { "id": "u-1", "userPrincipalName": "cpim_a@contoso.onmicrosoft.com", "accountEnabled": true, "createdDateTime": "2026-01-01T00:00:00Z" }
              ]
            }
            """;
        var handler = new FakeGraphHandler().SetupGet("/users", firstPageJson);
        var sut = BuildStore(handler);

        var first = await sut.ListAsync(new UserFilter { PageSize = 1 });
        first.NextCursor.Should().NotBeNull("Graph returned an @odata.nextLink, so a cursor must be surfaced");

        await sut.ListAsync(new UserFilter { PageSize = 1, Cursor = first.NextCursor });

        var followUp = handler.RecordedRequests[^1];
        Uri.UnescapeDataString(followUp.RequestUri!.Query)
            .Should().Contain("$skiptoken=EXT_OPAQUE_456");
    }

    [Fact]
    public async Task CreateAsync_HappyPath_PostsToUsersAndReturnsId_WithTemporaryPasswordInMetadata()
    {
        var handler = new FakeGraphHandler()
            .SetupPostJson("/users", """{"id":"u-new","userPrincipalName":"cpim_new@contoso.onmicrosoft.com"}""");
        var sut = BuildStore(handler);

        var result = await sut.CreateAsync(new CreateUserCommand { Email = "new@personal.example" });

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().Be("u-new");
        result.Metadata.Should().ContainKey(EntraExternalUserStore.TemporaryPasswordMetadataKey);
        result.Metadata![EntraExternalUserStore.TemporaryPasswordMetadataKey].Should().NotBeEmpty(
            "Create must surface the generated temp password so the admin can hand it over");
    }

    [Fact]
    public async Task CreateAsync_OnGraphError_ReturnsFailureWithGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Post, "/users", HttpStatusCode.Conflict,
                "Request_BadRequest", "Another user with this email already exists.");
        var sut = BuildStore(handler);

        var result = await sut.CreateAsync(new CreateUserCommand { Email = "dup@personal.example" });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Another user");
    }

    [Fact]
    public async Task UpdateAsync_PatchesUserAndReturnsSuccess()
    {
        var handler = new FakeGraphHandler().SetupPatch("/users/u-1");
        var sut = BuildStore(handler);

        var result = await sut.UpdateAsync("u-1", new UpdateUserCommand { UserName = "Updated Display" });

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().Be("u-1");
    }

    [Fact]
    public async Task UpdateAsync_OnNotFound_ReturnsUserNotFoundFailure()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Patch, "/users/missing", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "doesn't matter");
        var sut = BuildStore(handler);

        var result = await sut.UpdateAsync("missing", new UpdateUserCommand());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.",
            "the single-source UserNotFoundMessage is what every 404 branch surfaces");
    }

    [Fact]
    public async Task SetEnabledAsync_PatchesAccountEnabled()
    {
        var handler = new FakeGraphHandler().SetupPatch("/users/u-1");
        var sut = BuildStore(handler);

        (await sut.SetEnabledAsync("u-1", false)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetEnabledAsync_OnError_SurfacesGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Patch, "/users/u-1", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "permission denied");
        var sut = BuildStore(handler);

        var result = await sut.SetEnabledAsync("u-1", true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("permission denied");
    }

    [Fact]
    public async Task DeleteAsync_OnSuccess_ReturnsSuccess()
    {
        var handler = new FakeGraphHandler().SetupDelete("/users/u-1");
        var sut = BuildStore(handler);

        (await sut.DeleteAsync("u-1")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_OnNotFound_ReturnsUserNotFound()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Delete, "/users/missing", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "x");
        var sut = BuildStore(handler);

        var result = await sut.DeleteAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task RevokeSessionsAsync_PostsToRevokeEndpoint()
    {
        var handler = new FakeGraphHandler()
            .Setup(HttpMethod.Post, "/users/u-1/revokeSignInSessions",
                HttpStatusCode.OK, """{"value":true}""");
        var sut = BuildStore(handler);

        (await sut.RevokeSessionsAsync("u-1")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_PatchesPasswordProfile_AndReturnsTemporaryPassword()
    {
        var handler = new FakeGraphHandler().SetupPatch("/users/u-1");
        var sut = BuildStore(handler);

        var result = await sut.ResetPasswordAsync("u-1");

        result.IsSuccess.Should().BeTrue();
        result.Metadata.Should().ContainKey(EntraExternalUserStore.TemporaryPasswordMetadataKey);
        result.Metadata![EntraExternalUserStore.TemporaryPasswordMetadataKey].Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResetPasswordAsync_OnError_ReturnsFailure()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Patch, "/users/u-1", HttpStatusCode.BadRequest,
                "Request_BadRequest", "password does not meet complexity");
        var sut = BuildStore(handler);

        var result = await sut.ResetPasswordAsync("u-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("complexity");
    }

    [Fact]
    public async Task ResetTwoFactorAsync_DeletesAuthMethods_ReturnsSuccess()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1/authentication/methods", """
                { "value": [ { "@odata.type": "#microsoft.graph.microsoftAuthenticatorAuthenticationMethod", "id": "auth-1" } ] }
                """)
            .SetupDelete("/authentication/microsoftAuthenticatorMethods/auth-1");
        var sut = BuildStore(handler);

        var result = await sut.ResetTwoFactorAsync("u-1");

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().Be("u-1");
    }

    [Fact]
    public async Task ResetTwoFactorAsync_OnNotFound_ReturnsUserNotFound()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/missing/authentication/methods", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "x");
        var sut = BuildStore(handler);

        var result = await sut.ResetTwoFactorAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task ResetTwoFactorAsync_OnError_ReturnsFailureWithGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/u-1/authentication/methods", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "Requires UserAuthenticationMethod.ReadWrite.All");
        var sut = BuildStore(handler);

        var result = await sut.ResetTwoFactorAsync("u-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("UserAuthenticationMethod");
    }

    private static EntraExternalUserStore BuildStore(FakeGraphHandler handler)
    {
        // Inject the fake handler via a real Kiota HttpClient request
        // adapter — same code path as production, only the underlying
        // transport differs.
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
        return new EntraExternalUserStore(graph, options, NullLogger<EntraExternalUserStore>.Instance);
    }
}
