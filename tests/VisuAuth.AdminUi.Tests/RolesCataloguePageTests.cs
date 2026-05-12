using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace VisuAuth.AdminUi.Tests;

/// <summary>
/// Integration tests for <c>/visuauth/admin/roles</c> — the role catalogue
/// page with inline create / delete and member counts.
/// </summary>
public sealed class RolesCataloguePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public RolesCataloguePageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Catalogue_renders_seeded_roles_with_member_counts()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/roles", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Admin<", "Admin role seeded by the sample app must render");
        body.Should().Contain(">Manager<");
        body.Should().Contain(">Support<");

        // The seeded sample app puts admin@visuauth.dev and alice.silva in
        // "Manager", so the count is at least 2. Other tests may push more users
        // into the role within the same fixture lifetime, so match any digit
        // rather than a specific value.
        body.Should().MatchRegex(@"<strong>Manager</strong>[\s\S]*?<span class=""va-badge"">\d+</span>",
            "Manager must show a numeric member count badge");
    }

    [Fact]
    public async Task Catalogue_create_handler_adds_a_role_and_refreshes_the_table()
    {
        using var client = CreateClient();
        var unique = $"Role{Guid.NewGuid():N}"[..15];

        var token = await GetTokenAsync(client, "/visuauth/admin/roles");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["NewRoleName"] = unique,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/roles?handler=Create", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"Role &#x27;{unique}&#x27; created.");
        body.Should().Contain($">{unique}<", "the new role must appear in the table");

        using var scope = _factory.Services.CreateScope();
        var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        (await rm.RoleExistsAsync(unique)).Should().BeTrue();
    }

    [Fact]
    public async Task Catalogue_create_with_blank_name_surfaces_validation_error()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/roles");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["NewRoleName"] = "   ",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/roles?handler=Create", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Role name is required.");
    }

    [Fact]
    public async Task Catalogue_delete_handler_removes_the_role()
    {
        // Provision a throwaway role so deleting it does not affect other tests.
        string roleId;
        var name = $"Throwaway{Guid.NewGuid():N}"[..15];
        using (var scope = _factory.Services.CreateScope())
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(name);
            (await rm.CreateAsync(role)).Succeeded.Should().BeTrue();
            roleId = role.Id;
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/roles");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = roleId,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/roles?handler=Delete", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"Role &#x27;{name}&#x27; deleted.");

        using var verify = _factory.Services.CreateScope();
        var rm2 = verify.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        (await rm2.RoleExistsAsync(name)).Should().BeFalse();
    }

    [Fact]
    public async Task Edit_role_GET_returns_catalogue_with_row_in_edit_mode()
    {
        // Provision a role we can edit without affecting the seeded ones.
        var name = $"Rename{Guid.NewGuid():N}"[..15];
        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(name);
            (await rm.CreateAsync(role)).Succeeded.Should().BeTrue();
            roleId = role.Id;
        }

        using var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/visuauth/admin/roles?handler=EditRole&id={roleId}");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("<!doctype html>", "edit handler must return only the partial");
        body.Should().Contain("name=\"RenamedRoleName\"",
            "the edit form for the targeted row must be rendered");
        body.Should().Contain($"value=\"{name}\"",
            "the rename input must be prefilled with the current name");
    }

    [Fact]
    public async Task Rename_handler_changes_the_role_name_and_clears_edit_mode()
    {
        // Provision a throwaway role.
        var original = $"Old{Guid.NewGuid():N}"[..12];
        var renamed = $"New{Guid.NewGuid():N}"[..12];
        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(original);
            (await rm.CreateAsync(role)).Succeeded.Should().BeTrue();
            roleId = role.Id;
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/roles");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = roleId,
            ["RenamedRoleName"] = renamed,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/roles?handler=Rename", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"Role renamed to &#x27;{renamed}&#x27;.");
        body.Should().Contain($">{renamed}<", "the row must render the new name in view mode");
        body.Should().NotContain($">{original}<", "the old name must be gone from the page");

        using var verify = _factory.Services.CreateScope();
        var rm2 = verify.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        (await rm2.RoleExistsAsync(renamed)).Should().BeTrue();
        (await rm2.RoleExistsAsync(original)).Should().BeFalse();
    }

    [Fact]
    public async Task Rename_with_blank_name_keeps_row_in_edit_mode_and_surfaces_error()
    {
        var name = $"Stays{Guid.NewGuid():N}"[..12];
        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(name);
            (await rm.CreateAsync(role)).Succeeded.Should().BeTrue();
            roleId = role.Id;
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/roles");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = roleId,
            ["RenamedRoleName"] = "   ",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/roles?handler=Rename", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Role name is required.");
        body.Should().Contain("name=\"RenamedRoleName\"",
            "the failing row must stay in edit mode so the admin can fix the input");

        using var verify = _factory.Services.CreateScope();
        var rm2 = verify.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        (await rm2.RoleExistsAsync(name)).Should().BeTrue("the original role must still exist after a failed rename");
    }

    [Fact]
    public async Task Sidebar_promotes_roles_link()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("href=\"/visuauth/admin/roles\"",
            "the sidebar Roles entry must link to the catalogue");
        body.Should().NotContain("Roles (soon)",
            "the placeholder text must be gone now that the page exists");
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
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
