using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra;
using VisuAuth.Entra.Configuration;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Covers the Graph-touching paths of <see cref="EntraUserStore"/> by
/// wiring a <see cref="FakeGraphHandler"/> in place of the real HTTP
/// transport. Every store method we expose hits the Kiota pipeline end-
/// to-end (auth header injection, serialisation, deserialisation, error
/// unwrapping) — only the wire packet is canned. This gives confidence
/// in the mapping shape without requiring a live Entra tenant.
/// </summary>
public sealed class EntraUserStoreGraphTests
{
    private const string UserJson = """
        {
          "id": "u-1",
          "userPrincipalName": "alice@contoso.com",
          "mail": "alice@contoso.com",
          "displayName": "Alice",
          "accountEnabled": true,
          "businessPhones": ["+55 11 99999-9999"],
          "createdDateTime": "2026-01-01T00:00:00Z"
        }
        """;

    private const string UserListJson = """
        {
          "value": [
            {
              "id": "u-1",
              "userPrincipalName": "alice@contoso.com",
              "mail": "alice@contoso.com",
              "accountEnabled": true,
              "createdDateTime": "2026-01-01T00:00:00Z"
            },
            {
              "id": "u-2",
              "userPrincipalName": "bob@contoso.com",
              "mail": "bob@contoso.com",
              "accountEnabled": false,
              "createdDateTime": "2026-02-01T00:00:00Z"
            }
          ]
        }
        """;

    [Fact]
    public async Task GetAsync_HappyPath_ReturnsSummaryFromGraphResponse()
    {
        var handler = new FakeGraphHandler().SetupGet("/users/u-1", UserJson);
        var sut = BuildStore(handler);

        var summary = await sut.GetAsync("u-1");

        summary.Should().NotBeNull();
        summary!.Id.Should().Be("u-1");
        summary.Email.Should().Be("alice@contoso.com");
        summary.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_OnNotFound_ReturnsNull_WithoutThrowing()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/missing", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "Resource 'missing' does not exist.");
        var sut = BuildStore(handler);

        var summary = await sut.GetAsync("missing");

        summary.Should().BeNull("the IUserStore contract returns null when the user is absent, not throw");
    }

    [Fact]
    public async Task GetDetailAsync_FetchesUser_ReturnsDetail_EvenWhenRoleResolutionEmpty()
    {
        // GetDetailAsync makes 3 calls under the hood: get user, get
        // appRoleAssignments for the user, get servicePrincipal by appId.
        // The roles slot is filled best-effort — empty array when there
        // are no assignments is the path most test fixtures land in, and
        // exercising it covers ResolveRolesAsync's empty-collection
        // branch without forcing us to round-trip Guid encoding through
        // raw JSON.
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1", UserJson)
            .SetupGet("/users/u-1/appRoleAssignments", """{ "value": [] }""")
            .SetupGet("/servicePrincipals", """{ "value": [] }""");
        var sut = BuildStore(handler);

        var detail = await sut.GetDetailAsync("u-1");

        detail.Should().NotBeNull();
        detail!.Id.Should().Be("u-1");
        detail.Email.Should().Be("alice@contoso.com");
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
    public async Task ListAsync_BuildsFilterAndReturnsMappedSummaries()
    {
        var handler = new FakeGraphHandler().SetupGet("/users", UserListJson);
        var sut = BuildStore(handler);

        var page = await sut.ListAsync(new UserFilter { SearchTerm = "alice", PageSize = 50 });

        page.Items.Should().HaveCount(2);
        page.Items[0].Email.Should().Be("alice@contoso.com");
        page.Items[1].IsEnabled.Should().BeFalse("bob's accountEnabled is false in the fixture");
        page.TotalCount.Should().BeNull("Graph doesn't return a cheap total alongside a page");
        page.NextCursor.Should().BeNull("the fixture has no @odata.nextLink, so this is the last page");

        var listRequest = handler.RecordedRequests.Single();
        // Query string has `$` URL-encoded as %24. We assert on the
        // decoded form to keep the assertion human-readable.
        var decoded = Uri.UnescapeDataString(listRequest.RequestUri!.Query);
        decoded.Should().Contain("$filter=", "search term must push down as $filter");
        listRequest.Headers.GetValues("ConsistencyLevel").Single().Should().Be("eventual",
            "advanced filter capabilities require eventual consistency header");
    }

    [Fact]
    public async Task ListAsync_OnGraphError_LogsAndReturnsEmptyPage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "Insufficient privileges.");
        var sut = BuildStore(handler);

        var page = await sut.ListAsync(new UserFilter());

        page.Items.Should().BeEmpty("Graph failures degrade gracefully — empty page beats a 500 for the admin UI");
        page.NextCursor.Should().BeNull();
        page.TotalCount.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WhenGraphReturnsNextLink_ExposesCursor_AndFollowingItReplaysTheSkiptokenUrl()
    {
        const string firstPageJson = """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/users?$skiptoken=OPAQUE_TOKEN_123",
              "value": [
                { "id": "u-1", "userPrincipalName": "a@contoso.com", "accountEnabled": true, "createdDateTime": "2026-01-01T00:00:00Z" }
              ]
            }
            """;
        var handler = new FakeGraphHandler().SetupGet("/users", firstPageJson);
        var sut = BuildStore(handler);

        var first = await sut.ListAsync(new UserFilter { PageSize = 1 });
        first.NextCursor.Should().NotBeNull("Graph returned an @odata.nextLink, so a cursor must be surfaced");

        // Following the cursor replays Graph's continuation URL verbatim —
        // the skiptoken must survive the opaque-cursor round-trip.
        await sut.ListAsync(new UserFilter { PageSize = 1, Cursor = first.NextCursor });

        var followUp = handler.RecordedRequests[^1];
        Uri.UnescapeDataString(followUp.RequestUri!.Query)
            .Should().Contain("$skiptoken=OPAQUE_TOKEN_123");
    }

    [Fact]
    public async Task ListAsync_WithCursorForADifferentHost_IgnoresItAndFetchesFirstPage()
    {
        // A tampered cursor pointing off the Graph origin must never be
        // followed (it would leak the bearer token). The store falls back to a
        // normal first-page request instead.
        var handler = new FakeGraphHandler().SetupGet("/users", UserListJson);
        var sut = BuildStore(handler);

        var evilCursor = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("https://evil.example.com/users?$skiptoken=x"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var page = await sut.ListAsync(new UserFilter { PageSize = 50, Cursor = evilCursor });

        page.Items.Should().HaveCount(2, "the off-origin cursor is rejected and the first page is fetched");
        var request = handler.RecordedRequests.Single();
        request.RequestUri!.Host.Should().Be("graph.microsoft.com",
            "the request must target Graph, never the attacker host in the tampered cursor");
    }

    [Fact]
    public async Task CreateAsync_HappyPath_PostsToUsersAndReturnsId_WithTemporaryPasswordInMetadata()
    {
        var handler = new FakeGraphHandler()
            .SetupPostJson("/users", """{"id":"u-new","userPrincipalName":"new@contoso.com"}""");
        var sut = BuildStore(handler);

        var result = await sut.CreateAsync(new CreateUserCommand { Email = "new@contoso.com" });

        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().Be("u-new");
        result.Metadata.Should().ContainKey(EntraUserStore.TemporaryPasswordMetadataKey);
        result.Metadata![EntraUserStore.TemporaryPasswordMetadataKey].Should().NotBeEmpty(
            "Create must surface the generated temp password so the admin can hand it over");
    }

    [Fact]
    public async Task CreateAsync_OnGraphError_ReturnsFailureWithGraphMessage()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Post, "/users", HttpStatusCode.Conflict,
                "Request_BadRequest", "Another user with this UPN already exists.");
        var sut = BuildStore(handler);

        var result = await sut.CreateAsync(new CreateUserCommand { Email = "dup@contoso.com" });

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
        result.UserId.Should().Be("u-1");
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

        var result = await sut.SetEnabledAsync("u-1", false);

        result.IsSuccess.Should().BeTrue();
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

        var result = await sut.DeleteAsync("u-1");

        result.IsSuccess.Should().BeTrue();
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

        var result = await sut.RevokeSessionsAsync("u-1");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_PatchesPasswordProfile_AndReturnsTemporaryPassword()
    {
        var handler = new FakeGraphHandler().SetupPatch("/users/u-1");
        var sut = BuildStore(handler);

        var result = await sut.ResetPasswordAsync("u-1");

        result.IsSuccess.Should().BeTrue();
        result.Metadata.Should().ContainKey(EntraUserStore.TemporaryPasswordMetadataKey);
        result.Metadata![EntraUserStore.TemporaryPasswordMetadataKey].Should().NotBeEmpty();
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
                { "value": [ { "@odata.type": "#microsoft.graph.phoneAuthenticationMethod", "id": "phone-1" } ] }
                """)
            .SetupDelete("/authentication/phoneMethods/phone-1");
        var sut = BuildStore(handler);

        var result = await sut.ResetTwoFactorAsync("u-1");

        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().Be("u-1");
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

    private static EntraUserStore BuildStore(FakeGraphHandler handler)
    {
        // Inject the fake handler via a real Kiota HttpClient request
        // adapter — same code path as production, only the underlying
        // transport differs.
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        var graph = new GraphServiceClient(adapter);
        var options = Options.Create(new EntraOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            ClientSecret = "fake",
            AppRoleResourceId = "00000000-0000-0000-0000-000000000003",
        });
        return new EntraUserStore(graph, options, NullLogger<EntraUserStore>.Instance);
    }
}
