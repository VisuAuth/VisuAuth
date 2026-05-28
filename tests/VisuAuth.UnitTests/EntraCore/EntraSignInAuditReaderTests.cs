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
/// Graph-touching paths of <see cref="EntraSignInAuditReader"/>, mocked
/// via the shared <see cref="FakeGraphHandler"/>. Confirms the
/// signIns → AuditEntryView projection over the wire, the graceful 403
/// degradation, and the day-rollup (including next-link paging).
/// </summary>
public sealed class EntraSignInAuditReaderTests
{
    private const string TwoSignInsJson = """
        {
          "value": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "createdDateTime": "2026-05-02T10:00:00Z",
              "userPrincipalName": "alice@contoso.com",
              "userId": "user-1",
              "ipAddress": "203.0.113.5",
              "appDisplayName": "VisuAuth",
              "status": { "errorCode": 0 }
            },
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "createdDateTime": "2026-05-01T09:00:00Z",
              "userPrincipalName": "bob@contoso.com",
              "userId": "user-2",
              "status": { "errorCode": 50126, "failureReason": "Invalid username or password." }
            }
          ]
        }
        """;

    [Fact]
    public void Ctor_NullGraph_Throws()
    {
        var act = () => new EntraSignInAuditReader(null!, NullLogger<EntraSignInAuditReader>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("graphClient");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new EntraSignInAuditReader(BuildGraph(new FakeGraphHandler()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ListAsync_MapsSignInsToAuditEntries()
    {
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", TwoSignInsJson);
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { PageSize = 50 });

        page.Items.Should().HaveCount(2);
        page.Items[0].Action.Should().Be(AuditActions.LoginSucceeded);
        page.Items[0].ActorEmail.Should().Be("alice@contoso.com");
        page.Items[0].ActorIpAddress.Should().Be("203.0.113.5");
        page.Items[1].Action.Should().Be(AuditActions.LoginFailed);
        page.Items[1].Outcome.Should().Be(AuditOutcome.Failure);
        page.Items[1].FailureReason.Should().Be("Invalid username or password.");
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task ListAsync_PushesFilterAndPageSizeToGraph()
    {
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", """{ "value": [] }""");
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        await reader.ListAsync(new AuditFilter { ActorSearch = "alice", Outcome = AuditOutcome.Failure, PageSize = 25 });

        var query = Uri.UnescapeDataString(handler.RecordedRequests.Single().RequestUri!.Query);
        query.Should().Contain("$top=25");
        query.Should().Contain("$filter=");
        query.Should().Contain("startswith(userPrincipalName,'alice')");
        query.Should().Contain("status/errorCode ne 0");
    }

    [Fact]
    public async Task ListAsync_ClampsPageSizeTo200()
    {
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", """{ "value": [] }""");
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter { PageSize = 5000 });

        page.PageSize.Should().Be(200);
        Uri.UnescapeDataString(handler.RecordedRequests.Single().RequestUri!.Query).Should().Contain("$top=200");
    }

    [Fact]
    public async Task ListAsync_OnForbidden_DegradesToEmptyPage()
    {
        // Missing AuditLog.Read.All / no P1 licence → 403. Must not 500;
        // the admin page renders empty instead.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/signIns", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "Insufficient privileges.");
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var page = await reader.ListAsync(new AuditFilter());

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListDistinctActionsAsync_ReturnsTheTwoLoginCodes()
    {
        var reader = new EntraSignInAuditReader(BuildGraph(new FakeGraphHandler()), NullLogger<EntraSignInAuditReader>.Instance);

        var actions = await reader.ListDistinctActionsAsync();

        actions.Should().BeEquivalentTo(new[] { AuditActions.LoginSucceeded, AuditActions.LoginFailed });
    }

    [Fact]
    public async Task CountByDayAsync_GroupsSignInsByUtcDay()
    {
        var handler = new FakeGraphHandler().SetupGet("/auditLogs/signIns", TwoSignInsJson);
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(
            AuditActions.LoginSucceeded,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        // Two sign-ins on different days → two buckets, ordered ascending.
        counts.Should().HaveCount(2);
        counts[0].Day.Should().Be(new DateOnly(2026, 5, 1));
        counts[0].Count.Should().Be(1);
        counts[1].Day.Should().Be(new DateOnly(2026, 5, 2));
        counts[1].Count.Should().Be(1);
    }

    [Fact]
    public async Task CountByDayAsync_FollowsNextLink()
    {
        // First page carries a nextLink whose skiptoken substring routes to
        // a second (terminal) page. Register the more specific skiptoken
        // route FIRST so the follow-up request matches it before the
        // general /auditLogs/signIns route.
        const string page1 = """
            {
              "value": [ { "id": "a1", "createdDateTime": "2026-05-01T01:00:00Z", "status": { "errorCode": 0 } } ],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/auditLogs/signIns?$skiptoken=PAGE2"
            }
            """;
        const string page2 = """
            { "value": [ { "id": "b1", "createdDateTime": "2026-05-01T02:00:00Z", "status": { "errorCode": 0 } } ] }
            """;
        var handler = new FakeGraphHandler()
            .SetupGet("skiptoken=PAGE2", page2)
            .SetupGet("/auditLogs/signIns", page1);
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(
            AuditActions.LoginSucceeded,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        counts.Should().ContainSingle("both sign-ins land on 2026-05-01");
        counts[0].Day.Should().Be(new DateOnly(2026, 5, 1));
        counts[0].Count.Should().Be(2, "page 1 + page 2 each contributed one sign-in");
    }

    [Fact]
    public async Task CountByDayAsync_NonLoginAction_ReturnsEmpty_NoGraphCall()
    {
        var handler = new FakeGraphHandler();
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        var counts = await reader.CountByDayAsync(AuditActions.UserCreated, default, default);

        counts.Should().BeEmpty();
        handler.RecordedRequests.Should().BeEmpty("a non-sign-in action short-circuits before touching Graph");
    }

    [Fact]
    public async Task CountByDayAsync_OnForbidden_ReturnsEmpty()
    {
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/auditLogs/signIns", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "x");
        var reader = new EntraSignInAuditReader(BuildGraph(handler), NullLogger<EntraSignInAuditReader>.Instance);

        (await reader.CountByDayAsync(AuditActions.LoginSucceeded, default, default)).Should().BeEmpty();
    }

    private static GraphServiceClient BuildGraph(FakeGraphHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        return new GraphServiceClient(adapter);
    }
}
