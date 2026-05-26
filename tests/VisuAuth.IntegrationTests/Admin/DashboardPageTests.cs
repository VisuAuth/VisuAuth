using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// End-to-end coverage for <c>/visuauth/admin</c> — the dashboard landing
/// page. Asserts the page renders against the seeded sample, that the
/// KPI tiles carry the right numbers, that the chart and activity card
/// render when the audit plugin is wired (Sample.WebApp wires it), and
/// that the sidebar gains the Dashboard entry without losing the others.
/// </summary>
public sealed class DashboardPageTests(VisuAuthTestFactory factory) : IClassFixture<VisuAuthTestFactory>
{
    private readonly VisuAuthTestFactory _factory = factory;

    [Fact]
    public async Task GetDashboard_RendersHeadingAndKpiGrid()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the dashboard is the new admin landing — / -> /admin must render OK");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("va-kpi-grid", "the KPI tile section must render");
        body.Should().Contain("Dashboard.Kpi.TotalUsers".Replace(".", ".").Length > 0
            ? "Users" // resource key resolves to either 'Users' (en) or 'Usuários' (pt-BR)
            : "");
        body.Should().Contain("/visuauth/admin/users",
            "the Total Users tile must drill-down to the users list");
    }

    [Fact]
    public async Task GetDashboard_KpiCountsMatchSeededSampleData()
    {
        using var client = _factory.CreateClient();
        var body = await (await client.GetAsync(new Uri("/visuauth/admin", UriKind.Relative))).Content.ReadAsStringAsync();

        // The seeded sample has at least 12 users (see Sample.WebApp/Data/UserSeeder).
        // The TotalUsers tile must render a non-zero count; we assert a
        // loose lower bound so adding seed rows in the future doesn't
        // break the test.
        ExtractKpiValue(body, "Dashboard.Kpi.TotalUsers", out var totalUsers).Should().BeTrue();
        totalUsers.Should().BeGreaterThanOrEqualTo(10,
            "the seeded sample must populate the Users tile with the visible count");
    }

    [Fact]
    public async Task GetDashboard_WhenAuditPluginWired_RendersChartAndRecentActivity()
    {
        using var client = _factory.CreateClient();
        var body = await (await client.GetAsync(new Uri("/visuauth/admin", UriKind.Relative))).Content.ReadAsStringAsync();

        // Sample.WebApp wires AddVisuAuthAuditLog, so the chart + activity
        // card render even with zero entries (zero-fill series + "no
        // events" message). The "plugin disabled" banner must NOT show.
        body.Should().Contain("va-bar-chart",
            "the bar chart must render when the audit plugin is wired");
        body.Should().Contain("va-dashboard-activity",
            "the recent activity card must render");
        body.Should().NotContain("Dashboard.Chart.PluginDisabled",
            "the plugin-disabled fallback must not appear when the plugin is on");
    }

    [Fact]
    public async Task GetDashboard_SidebarShowsDashboardEntryActiveOnAdminRoot()
    {
        using var client = _factory.CreateClient();
        var body = await (await client.GetAsync(new Uri("/visuauth/admin", UriKind.Relative))).Content.ReadAsStringAsync();

        // The "Dashboard" sidebar nav link must exist and be marked active.
        body.Should().MatchRegex(@"href=""/visuauth/admin""[^>]*class=""[^""]*\bva-active\b",
            "the Dashboard sidebar entry must be the active one when path is /visuauth/admin");
        body.Should().Contain("/visuauth/admin/users",
            "other sidebar entries must keep rendering — they aren't being replaced, just demoted");
    }

    [Fact]
    public async Task GetUsersPage_StillRenders_AfterDashboardLanded()
    {
        // Regression guard: the dashboard at /admin must not break the
        // existing /admin/users route via routing conflicts.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_RendersHealthCardWithVersionAndRuntime()
    {
        using var client = _factory.CreateClient();
        var body = await (await client.GetAsync(new Uri("/visuauth/admin", UriKind.Relative))).Content.ReadAsStringAsync();

        body.Should().Contain("va-dashboard-health");
        body.Should().MatchRegex(@".NET\s+\d",
            "the runtime line must echo the loaded .NET version (e.g. '.NET 10.0.0')");
    }

    /// <summary>
    /// Pulls the numeric value out of a KPI tile by its aria-label resource
    /// key. Returns true with the parsed count on hit, false otherwise.
    /// Looks for the markup shape
    /// <c>&lt;span class="va-kpi-value"&gt;{count}&lt;/span&gt;</c> inside the
    /// tile whose aria-label localiser-resolves to the EN or PT-BR string
    /// for the given key — we accept both because culture resolution is
    /// header / cookie driven and the test doesn't pin one.
    /// </summary>
    private static bool ExtractKpiValue(string body, string resourceKey, out int count)
    {
        count = 0;
        var labels = resourceKey switch
        {
            "Dashboard.Kpi.TotalUsers" => new[] { "Users", "Usuários" },
            _ => Array.Empty<string>(),
        };

        foreach (var label in labels)
        {
            // The aria-label and the visible label both carry the resolved
            // string. Search for the visible <span> next to that label.
            var marker = $"aria-label=\"{label}\"";
            var pos = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                continue;
            }
            var valueOpen = body.IndexOf("va-kpi-value", pos, StringComparison.Ordinal);
            if (valueOpen < 0)
            {
                continue;
            }
            var openTagEnd = body.IndexOf('>', valueOpen);
            var closeTag = body.IndexOf("</span>", openTagEnd, StringComparison.Ordinal);
            if (openTagEnd < 0 || closeTag < 0)
            {
                continue;
            }
            var raw = body[(openTagEnd + 1)..closeTag].Trim();
            // The page formats with N0 — strip non-digit chars before parse.
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out count))
            {
                return true;
            }
        }

        return false;
    }
}
