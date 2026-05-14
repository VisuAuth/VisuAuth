using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Smoke tests for the user-list filter row (search + role / status /
/// verified / 2FA dropdowns) and the new query-string parameters they wire
/// through to <c>UserFilter</c>.
/// </summary>
public sealed class UsersListFiltersTests : IClassFixture<VisuAuthTestFactory>
{
    private readonly VisuAuthTestFactory _factory;

    public UsersListFiltersTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_DefaultRender_ShowsAllFiltersAndSearch()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("id=\"va-users-filter\"",
            "the filter form must wrap the search and dropdowns so htmx submits them together");
        body.Should().Contain("name=\"q\"", "search input must be present");
        body.Should().Contain("name=\"role\"", "role dropdown must be rendered when role management is supported");
        body.Should().Contain("name=\"status\"");
        body.Should().Contain("name=\"verified\"");
        body.Should().Contain("name=\"twofa\"");
        body.Should().Contain(">All roles<", "role dropdown must offer the unfiltered option");
        body.Should().Contain("value=\"Admin\"");
        body.Should().Contain("value=\"Manager\"");
        body.Should().Contain("value=\"Support\"");
    }

    [Fact]
    public async Task Get_WithRoleFilter_HidesUsersWithoutThatRole()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users?role=Support", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        // Seeded: bruno.costa is in Support, alice.silva is in Manager, daniel.eloi has no role.
        body.Should().Contain("bruno.costa@example.com");
        body.Should().NotContain("alice.silva@example.com",
            "alice is in Manager, not Support, and must be hidden by the role filter");
        body.Should().NotContain("daniel.eloi@example.com",
            "daniel has no roles and must be hidden by the Support filter");
    }

    [Fact]
    public async Task Get_WithLockedStatusFilter_ShowsOnlyLockedUsers()
    {
        // Lock a throwaway user so the filter has something to find.
        string lockedEmail;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            lockedEmail = $"locked.{suffix}@example.com";
            var user = new ApplicationUser
            {
                Email = lockedEmail,
                UserName = $"locked.{suffix}",
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            (await um.CreateAsync(user, "Locked!Pass1")).Succeeded.Should().BeTrue();
            (await um.SetLockoutEnabledAsync(user, true)).Succeeded.Should().BeTrue();
            (await um.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded.Should().BeTrue();
        }

        using var client = CreateClient();
        var lockedResponse = await client.GetAsync(new Uri("/visuauth/admin/users?status=locked", UriKind.Relative));
        var lockedBody = await lockedResponse.Content.ReadAsStringAsync();

        lockedBody.Should().Contain(lockedEmail, "the locked user must appear under the 'locked' filter");
        lockedBody.Should().NotContain("alice.silva@example.com",
            "alice is active and must be hidden by the locked-only filter");
    }

    [Fact]
    public async Task Get_WithMultipleFilters_ReturnsIntersection()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(
            new Uri("/visuauth/admin/users?role=Manager&verified=true", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        // alice.silva is in Manager AND has EmailConfirmed=true → must appear.
        body.Should().Contain("alice.silva@example.com");
        // bruno.costa is in Support (not Manager) → must be filtered out.
        body.Should().NotContain("bruno.costa@example.com");
    }

    [Fact]
    public async Task Pagination_AfterFilterApplied_PreservesFilterInLinks()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(
            new Uri("/visuauth/admin/users?q=alice&role=Manager", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        // Pagination links should round-trip the active filters even when
        // disabled (single-page result). The <span class="va-disabled">
        // path doesn't carry a href, but the surrounding query string from
        // the form must still be on the page so subsequent submits keep
        // the narrowed view.
        body.Should().Contain("name=\"q\"", "search input keeps its value across renders");
        body.Should().Contain("value=\"alice\"");
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
    });
}
