using System.Net;
using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sample.WebApp.Data;

using Xunit;

namespace VisuAuth.AdminUi.Tests;

/// <summary>
/// Integration tests for the create-user form and the delete action on the
/// detail page.
/// </summary>
public sealed class UserCreateDeleteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public UserCreateDeleteTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task New_user_form_renders()
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
    public async Task Users_index_has_new_user_button()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("href=\"/visuauth/admin/users/new\"");
        body.Should().Contain(">New user<");
    }

    [Fact]
    public async Task Create_with_password_redirects_to_detail_and_persists_user()
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
    public async Task Create_without_password_keeps_admin_on_page_and_surfaces_temporary_password()
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
    public async Task Create_with_duplicate_email_surfaces_validation_error()
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
    public async Task Delete_action_removes_the_user_and_redirects_to_the_list()
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
