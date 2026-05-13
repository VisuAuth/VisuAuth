using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Smoke tests that exercise the full pipeline: sample app boots, ASP.NET Identity
/// stores are wired, seeder runs, the admin page renders, and htmx partial mode
/// returns only the inner fragment.
/// </summary>
public sealed class UsersPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UsersPageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_DefaultRequest_RendersSeededUsersInsideLayout()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("<!doctype html>", "the layout must wrap the page");
        body.Should().Contain("class=\"va-shell\"", "sidebar layout must render");
        body.Should().Contain("alice.silva@example.com", "seeded users must appear in the table");
        body.Should().Contain("Users <span", "page header must render with total");
    }

    [Fact]
    public async Task Get_WithSearchTerm_FiltersToMatchingUsers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users?q=alice", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("alice.silva@example.com");
        body.Should().NotContain("bruno.costa@example.com",
            "search filter must exclude non-matching users");
    }

    [Fact]
    public async Task Get_WithHxRequestHeader_ReturnsPartialWithoutLayout()
    {
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/visuauth/admin/users?q=alice");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("<!doctype html>",
            "htmx mode must return only the partial — no full layout");
        body.Should().Contain("va-users-table", "partial id must be present for hx-swap target");
        body.Should().Contain("alice.silva@example.com");
    }
}
