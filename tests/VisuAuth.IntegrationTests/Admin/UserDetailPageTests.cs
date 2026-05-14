using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Smoke tests for the read-only user detail page at <c>/visuauth/admin/users/{id}</c>.
/// </summary>
public sealed class UserDetailPageTests : IClassFixture<VisuAuthTestFactory>
{
    private readonly VisuAuthTestFactory _factory;

    public UserDetailPageTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WithExistingUserId_RendersAllSections()
    {
        var userId = await ResolveSeededUserIdAsync("alice.silva@example.com");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri($"/visuauth/admin/users/{userId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("alice.silva@example.com", "page heading must show the user's email");
        body.Should().Contain("‹ Back to users", "back link must take admins to the list");
        body.Should().Contain(">Profile<", "profile section must render");
        body.Should().Contain(">Security<", "security section must render");
        body.Should().Contain(">Roles ", "roles section must render when role management is supported");
        body.Should().Contain(">Claims ", "claims section must render when custom claims are supported");
        body.Should().Contain(">External logins ", "external logins section must render when supported");
    }

    [Fact]
    public async Task Get_WithUnknownId_Returns404()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users/does-not-exist", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UsersListRow_DefaultRender_LinksEmailToDetailPage()
    {
        var userId = await ResolveSeededUserIdAsync("alice.silva@example.com");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users?q=alice", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"href=\"/visuauth/admin/users/{userId}\"",
            "the users table must link each email to the detail page");
    }

    private async Task<string> ResolveSeededUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull($"the seeded user '{email}' must exist for this test");
        return user!.Id;
    }
}
