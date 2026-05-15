using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Integration tests for the create-user form and the delete action on the
/// detail page.
/// </summary>
public sealed class UserCreateDeleteTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly VisuAuthTestFactory _factory;

    public UserCreateDeleteTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNew_DefaultRender_ShowsAllFormFields()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("New user", "page heading must render");
        body.Should().Contain("name=\"Form.Email\"");
        body.Should().Contain("name=\"Form.UserName\"");
        body.Should().Contain("name=\"Form.Password\"");
        body.Should().Contain("name=\"Form.EmailConfirmed\"");
    }

    [Fact]
    public async Task GetIndex_DefaultRender_HasNewUserButton()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("href=\"/visuauth/admin/users/new\"");
        body.Should().Contain(">New user<");
    }

    [Fact]
    public async Task PostNew_WithExplicitPassword_RedirectsToDetailAndPersists()
    {
        using var client = CreateClient(allowRedirects: false);
        var email = UniqueEmail();
        var token = await GetTokenAsync(client, "/visuauth/admin/users/new");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.UserName"] = "",
            ["Form.Password"] = "Created!Pass1",
            ["Form.PhoneNumber"] = "",
            ["Form.EmailConfirmed"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "supplying a password skips the temp-password panel and sends the admin to detail");
        response.Headers.Location!.ToString().Should().StartWith("/visuauth/admin/users/");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull("the user must be persisted in Identity");
        (await userManager.CheckPasswordAsync(user!, "Created!Pass1")).Should().BeTrue(
            "the explicit password the admin entered must authenticate the user");
    }

    [Fact]
    public async Task PostNew_WithBlankPassword_StaysOnPageAndSurfacesTempPassword()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        var token = await GetTokenAsync(client, "/visuauth/admin/users/new");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.UserName"] = "",
            ["Form.Password"] = "",
            ["Form.PhoneNumber"] = "",
            ["Form.EmailConfirmed"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Temporary password");
        body.Should().Contain("class=\"va-temp-password\"");
        body.Should().Contain("data-va-copy-source",
            "the temp password must be wired to the copy-to-clipboard widget");
        body.Should().Contain("data-va-copy",
            "the copy button must be rendered next to the temp password");

        var match = Regex.Match(body, @"<code class=""va-temp-password""[^>]*>([^<]+)</code>");
        match.Success.Should().BeTrue("the temp password must render inline");
        var temp = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user!, temp)).Should().BeTrue(
            "the autogenerated password surfaced on the page must authenticate the user");
    }

    [Fact]
    public async Task PostNew_WithDuplicateEmail_ShowsIdentityValidationError()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/users/new");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = "alice.silva@example.com", // already seeded
            ["Form.UserName"] = "",
            ["Form.Password"] = "Whatever!Pass1",
            ["Form.PhoneNumber"] = "",
            ["Form.EmailConfirmed"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Could not create the user.");
        body.Should().MatchRegex("already taken|already (in )?use|Duplicate",
            "Identity must surface the duplicate-email error to the admin");
    }

    [Fact]
    public async Task GetNew_WithRoleManagementSupported_RendersRoleCheckboxes()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("name=\"Form.SelectedRoles\"",
            "the form must expose role checkboxes when the backend supports role management");
        body.Should().Contain("value=\"Admin\"");
        body.Should().Contain("value=\"Manager\"");
        body.Should().Contain("value=\"Support\"");
    }

    [Fact]
    public async Task PostNew_WithSelectedRoles_AssignsUserToThoseRolesOnly()
    {
        using var client = CreateClient(allowRedirects: false);
        var email = UniqueEmail("withroles");
        var token = await GetTokenAsync(client, "/visuauth/admin/users/new");

        // Multiple values for the same name — model binder collects them into IList<string>.
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Form.Email", email),
            new("Form.UserName", ""),
            new("Form.Password", "WithRoles!Pass1"),
            new("Form.PhoneNumber", ""),
            new("Form.EmailConfirmed", "true"),
            new("Form.SelectedRoles", "Manager"),
            new("Form.SelectedRoles", "Support"),
        };

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/users/new", UriKind.Relative),
            new FormUrlEncodedContent(pairs));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "selecting roles with an explicit password still redirects to the detail page when all assignments succeed");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        user.Should().NotBeNull();
        (await um.IsInRoleAsync(user!, "Manager")).Should().BeTrue();
        (await um.IsInRoleAsync(user!, "Support")).Should().BeTrue();
        (await um.IsInRoleAsync(user!, "Admin")).Should().BeFalse("only the picked roles must be assigned");
    }

    [Fact]
    public async Task PostDelete_OnExistingUser_RemovesAndRedirectsToList()
    {
        // Provision a throwaway user so the delete does not knock out a seeded one.
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = UniqueEmail("delete"),
                UserName = $"delete.{Guid.NewGuid():N}"[..15],
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            (await um.CreateAsync(user, "Delete!Pass1")).Succeeded.Should().BeTrue();
            userId = user.Id;
        }

        using var client = CreateClient(allowRedirects: false);
        var detailUrl = $"/visuauth/admin/users/{userId}";
        var token = await GetTokenAsync(client, detailUrl);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync(new Uri($"{detailUrl}?handler=Delete", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users");

        using var verify = _factory.Services.CreateScope();
        var verifyManager = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var gone = await verifyManager.FindByIdAsync(userId);
        gone.Should().BeNull("the user must no longer exist after delete");
    }

    private HttpClient CreateClient(bool allowRedirects = true) => _factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = allowRedirects,
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

    private static string UniqueEmail(string prefix = "new") =>
        $"{prefix}.{Guid.NewGuid():N}@example.com";
}
