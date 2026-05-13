using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Integration tests for the assign-role / remove-role actions on the user
/// detail page. Each test provisions its own throwaway user so it stays
/// independent of the seeder's pre-assigned role data.
/// </summary>
public sealed class UserRoleAssignmentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public UserRoleAssignmentTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostAssignRole_WithKnownRole_AssignsAndRefreshes()
    {
        var (userId, detailUrl) = await ProvisionUserAsync();
        using var client = CreateClient();

        var body = await PostAsync(client, detailUrl, "AssignRole", new()
        {
            ["roleName"] = "Manager",
        });

        body.Should().Contain("Role &#x27;Manager&#x27; assigned.");
        body.Should().MatchRegex(@"<li class=""va-chip va-chip-removable"">\s*<span>Manager</span>",
            "the new role chip must appear in the Roles section after assignment");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByIdAsync(userId);
        (await um.IsInRoleAsync(user!, "Manager")).Should().BeTrue();
    }

    [Fact]
    public async Task PostRemoveRole_WithAssignedRole_DropsItAndKeepsOthers()
    {
        var (userId, detailUrl) = await ProvisionUserAsync(initialRoles: ["Manager", "Support"]);
        using var client = CreateClient();

        var body = await PostAsync(client, detailUrl, "RemoveRole", new()
        {
            ["roleName"] = "Manager",
        });

        body.Should().Contain("Role &#x27;Manager&#x27; removed.");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByIdAsync(userId);
        (await um.IsInRoleAsync(user!, "Manager")).Should().BeFalse();
        (await um.IsInRoleAsync(user!, "Support")).Should().BeTrue("only the requested role must be removed");
    }

    [Fact]
    public async Task AssignRoleDropdown_DefaultRender_HidesAlreadyAssignedRoles()
    {
        var (_, detailUrl) = await ProvisionUserAsync(initialRoles: ["Admin"]);
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(detailUrl, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        // The dropdown <select name="roleName"> must include the unassigned roles
        // (Manager, Support) but not the one the user already has (Admin).
        var selectMatch = Regex.Match(
            body,
            @"<select[^>]*name=""roleName""[^>]*>(?<options>[\s\S]*?)</select>");
        selectMatch.Success.Should().BeTrue("the assign-role dropdown must be rendered");

        var options = selectMatch.Groups["options"].Value;
        options.Should().Contain("Manager", "unassigned roles must appear in the dropdown");
        options.Should().Contain("Support");
        options.Should().NotContain(">Admin<",
            "roles the user already has must be filtered out of the dropdown");
    }

    [Fact]
    public async Task PostAssignRole_WithUnknownRole_ShowsValidationError()
    {
        var (_, detailUrl) = await ProvisionUserAsync();
        using var client = CreateClient();

        var body = await PostAsync(client, detailUrl, "AssignRole", new()
        {
            ["roleName"] = "DoesNotExist",
        });

        body.Should().Contain("Action failed.");
        body.Should().Contain("Role &#x27;DoesNotExist&#x27; does not exist.");
    }

    [Fact]
    public async Task PostAssignRole_WithMissingRoleName_ShowsValidationError()
    {
        var (_, detailUrl) = await ProvisionUserAsync();
        using var client = CreateClient();

        var body = await PostAsync(client, detailUrl, "AssignRole", new()
        {
            ["roleName"] = "",
        });

        body.Should().Contain("Pick a role to assign.");
    }

    private async Task<(string UserId, string DetailUrl)> ProvisionUserAsync(IReadOnlyList<string>? initialRoles = null)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var unique = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            Email = $"roles.{unique}@example.com",
            UserName = $"roles.{unique}",
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await um.CreateAsync(user, "Pa$$w0rd!")).Succeeded.Should().BeTrue();

        if (initialRoles is { Count: > 0 })
        {
            foreach (var role in initialRoles)
            {
                (await um.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue(
                    $"the sample app must seed the '{role}' role for this test to use");
            }
        }

        return (user.Id, $"/visuauth/admin/users/{user.Id}");
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
    });

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue();
        return match.Groups[1].Value;
    }

    private static async Task<string> PostAsync(
        HttpClient client,
        string detailUrl,
        string handler,
        Dictionary<string, string> extra)
    {
        var token = await GetTokenAsync(client, detailUrl);
        extra["__RequestVerificationToken"] = token;

        var response = await client.PostAsync(
            new Uri($"{detailUrl}?handler={handler}", UriKind.Relative),
            new FormUrlEncodedContent(extra));

        response.IsSuccessStatusCode.Should().BeTrue($"POST ?handler={handler} must succeed");
        return await response.Content.ReadAsStringAsync();
    }
}
