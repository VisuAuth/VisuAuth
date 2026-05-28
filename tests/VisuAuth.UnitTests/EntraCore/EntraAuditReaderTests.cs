using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.EntraCore.Auditing;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Graph-touching paths of <see cref="EntraAuditReader"/>, mocked via the
/// shared <see cref="FakeGraphHandler"/>. Covers the two-source merge
/// (signIns + directoryAudits), the action-aware source routing, the
/// client-side actor filter on directory audits, graceful per-source 403
/// degradation, and the sign-in-backed day rollup (incl. next-link paging).
/// </summary>
public sealed class EntraAuditReaderTests
{
    private const string OneSignInJson = """
        {
          "value": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "createdDateTime": "2026-05-02T10:00:00Z",
              "userPrincipalName": "alice@contoso.com",
              "userId": "user-1",
              "status": { "errorCode": 0 }
            }
          ]
        }
        """;

    private const string OneDirectoryAuditJson = """
        {
          "value": [
            {
              "id": "33333333-3333-3333-3333-333333333333",
              "activityDateTime": "2026-05-03T09:00:00Z",
              "activityDisplayName": "Add user",
              "category": "UserManagement",
              "result": "success",
              "initiatedBy": { "user": { "id": "admin-1", "userPrincipalName": "admin@contoso.com" } },
              "targetResources": [ { "type": "User", "id": "new-1", "displayName": "New User" } ]
            }
          ]
        }
        """;

    private const string EmptyJson = """{ "value": [] }""";

    [Fact]
    public void Ctor_NullGraph_Throws()
    {
        var act = () => new EntraAuditReader(null!, NullLogger<EntraAuditReader>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("graphClient");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new EntraAuditReader(BuildGraph(new FakeGraphHandler()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ListAsync_NoActionFilter_MergesBothSources_NewestFirst()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/auditLogs/signIns", OneSignInJson)                  // 2026-05-02
            .SetupGet("/auditLogs/directoryAudits", OneDirectoryAuditJson); // 2026-05-03
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { PageSize = 50 });

        page.Items.Should().HaveCount(2);
        page.Items[0].Action.Should().Be("Add user", "the directory audit (05-03) is newer than the sign-in (05-02)");
        page.Items[1].Action.Should().Be(AuditActions.LoginSucceeded);
    }

    [Fact]
    public async Task ListAsync_LoginActionFilter_QueriesSignInsOnly()
    {
        // Only the signIns route is registered — if the reader also queried
        // directoryAudits the FakeGraphHandler would throw "no route".
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", OneSignInJson);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { Action = AuditActions.LoginSucceeded });

        page.Items.Should().ContainSingle().Which.Action.Should().Be(AuditActions.LoginSucceeded);
        handler.RecordedRequests.Should().OnlyContain(r =>
            r.RequestUri!.AbsolutePath.Contains("/auditLogs/signIns", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAsync_DirectoryActionFilter_QueriesDirectoryOnly()
    {
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/directoryAudits", OneDirectoryAuditJson);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { Action = "Add user" });

        page.Items.Should().ContainSingle().Which.Action.Should().Be("Add user");
        handler.RecordedRequests.Should().OnlyContain(r =>
            r.RequestUri!.AbsolutePath.Contains("/auditLogs/directoryAudits", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAsync_ActorSearch_FiltersDirectoryClientSide()
    {
        const string twoAudits = """
            {
              "value": [
                { "id": "a", "activityDateTime": "2026-05-03T09:00:00Z", "activityDisplayName": "Add user",
                  "result": "success", "initiatedBy": { "user": { "userPrincipalName": "admin@contoso.com" } } },
                { "id": "b", "activityDateTime": "2026-05-03T08:00:00Z", "activityDisplayName": "Update user",
                  "result": "success", "initiatedBy": { "user": { "userPrincipalName": "other@contoso.com" } } }
              ]
            }
            """;
        var handler = new FakeGraphHandler()
            .SetupGet("/auditLogs/signIns", EmptyJson)
            .SetupGet("/auditLogs/directoryAudits", twoAudits);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { ActorSearch = "admin@" });

        page.Items.Should().ContainSingle().Which.ActorEmail.Should().Be("admin@contoso.com",
            "directory actor search is applied client-side (nested initiatedBy path isn't reliably filterable in Graph)");
    }

    [Fact]
    public async Task ListAsync_PushesSignInFilterAndPageSize()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/auditLogs/signIns", EmptyJson)
            .SetupGet("/auditLogs/directoryAudits", EmptyJson);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        await reader.ListAsync(new AuditFilter { Outcome = AuditOutcome.Failure, PageSize = 25 });

        var signInRequest = handler.RecordedRequests.Single(r =>
            r.RequestUri!.AbsolutePath.Contains("/auditLogs/signIns", StringComparison.Ordinal));
        var query = Uri.UnescapeDataString(signInRequest.RequestUri!.Query);
        query.Should().Contain("$top=25");
        query.Should().Contain("status/errorCode ne 0");
    }

    [Fact]
    public async Task ListAsync_ClampsPageSizeTo200()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/auditLogs/signIns", EmptyJson)
            .SetupGet("/auditLogs/directoryAudits", EmptyJson);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { PageSize = 5000 });

        page.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task ListAsync_BothSourcesForbidden_DegradesToEmpty()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/signIns", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "x")
            .SetupError(HttpMethod.Get, "/auditLogs/directoryAudits", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "x");
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter());

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_SignInsForbidden_DirectoryOk_StillReturnsDirectory()
    {
        // Per-source degradation: one source 403ing must not wipe the other.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/signIns", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "x")
            .SetupGet("/auditLogs/directoryAudits", OneDirectoryAuditJson);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter());

        page.Items.Should().ContainSingle().Which.Action.Should().Be("Add user");
    }

    [Fact]
    public async Task ListDistinctActionsAsync_UnionsLoginCodesAndDirectoryActivities()
    {
        const string activities = """
            {
              "value": [
                { "activityDisplayName": "Add user" },
                { "activityDisplayName": "Add member to role" },
                { "activityDisplayName": "Add user" }
              ]
            }
            """;
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/directoryAudits", activities);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var actions = await reader.ListDistinctActionsAsync();

        actions.Should().Contain(AuditActions.LoginSucceeded);
        actions.Should().Contain(AuditActions.LoginFailed);
        actions.Should().Contain("Add user");
        actions.Should().Contain("Add member to role");
        actions.Count(a => a == "Add user").Should().Be(1, "distinct activity names only");
    }

    [Fact]
    public async Task ListDistinctActionsAsync_DirectoryForbidden_FallsBackToLoginCodes()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/directoryAudits", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "x");
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var actions = await reader.ListDistinctActionsAsync();

        actions.Should().BeEquivalentTo(new[] { AuditActions.LoginSucceeded, AuditActions.LoginFailed });
    }

    [Fact]
    public async Task CountByDayAsync_GroupsSignInsByUtcDay()
    {
        const string twoDays = """
            {
              "value": [
                { "createdDateTime": "2026-05-02T10:00:00Z" },
                { "createdDateTime": "2026-05-01T09:00:00Z" }
              ]
            }
            """;
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", twoDays);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(
            AuditActions.LoginSucceeded,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        counts.Should().HaveCount(2);
        counts[0].Day.Should().Be(new DateOnly(2026, 5, 1));
        counts[1].Day.Should().Be(new DateOnly(2026, 5, 2));
    }

    [Fact]
    public async Task CountByDayAsync_FollowsNextLink()
    {
        const string page1 = """
            {
              "value": [ { "createdDateTime": "2026-05-01T01:00:00Z" } ],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/auditLogs/signIns?$skiptoken=PAGE2"
            }
            """;
        const string page2 = """{ "value": [ { "createdDateTime": "2026-05-01T02:00:00Z" } ] }""";
        var handler = new FakeGraphHandler()
            .SetupGet("skiptoken=PAGE2", page2)
            .SetupGet("/auditLogs/signIns", page1);
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(
            AuditActions.LoginSucceeded,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        counts.Should().ContainSingle();
        counts[0].Count.Should().Be(2, "page 1 + page 2 each contributed a sign-in on 2026-05-01");
    }

    [Fact]
    public async Task CountByDayAsync_NonLoginAction_ReturnsEmpty_NoGraphCall()
    {
        var handler = new FakeGraphHandler();
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(AuditActions.UserCreated, default, default);

        counts.Should().BeEmpty();
        handler.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task CountByDayAsync_OnForbidden_ReturnsEmpty()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/signIns", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "x");
        var reader = new EntraAuditReader(BuildGraph(handler), NullLogger<EntraAuditReader>.Instance);

        (await reader.CountByDayAsync(AuditActions.LoginSucceeded, default, default)).Should().BeEmpty();
    }

    private static GraphServiceClient BuildGraph(FakeGraphHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        return new GraphServiceClient(adapter);
    }
}
