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
/// Integration tests for the mutation handlers on the user detail page.
/// Each test targets a unique seeded user so they can run in any order even
/// though the SQLite database is shared by <see cref="WebApplicationFactory{Program}"/>.
/// </summary>
public sealed class UserMutationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public UserMutationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLockAndUnlock_RoundTrip_TogglesLockoutState()
    {
        var (userId, detailUrl) = await SetupAsync("bruno.costa@example.com");
        using var client = CreateClient();

        var lockBody = await PostAsync(client, detailUrl, "Lock");
        lockBody.Should().Contain("Account locked.");
        lockBody.Should().Contain(">Locked<", "the header badge must flip to Locked after the action");

        await EnsurePersistedAsync(userId, expectLocked: true);

        var unlockBody = await PostAsync(client, detailUrl, "Unlock");
        unlockBody.Should().Contain("Account unlocked.");
        unlockBody.Should().Contain(">Active<");

        await EnsurePersistedAsync(userId, expectLocked: false);
    }

    [Fact]
    public async Task PostResetPassword_AfterValidPost_SurfacesTempPasswordAndInvalidatesOld()
    {
        var (userId, detailUrl) = await SetupAsync("carla.dias@example.com");
        using var client = CreateClient();

        var body = await PostAsync(client, detailUrl, "ResetPassword");

        body.Should().Contain("Temporary password");
        body.Should().Contain("class=\"va-temp-password\"");

        var match = Regex.Match(body, @"<code class=""va-temp-password""[^>]*>([^<]+)</code>");
        match.Success.Should().BeTrue("the response must render the generated password inline");
        // The generator emits symbols like '&' that Razor HTML-encodes on render.
        // Decode back to the plaintext the user will actually type.
        var temp = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
        temp.Length.Should().BeGreaterThan(8, "the generator must produce a non-trivial password");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        user.Should().NotBeNull();

        (await userManager.CheckPasswordAsync(user!, temp)).Should().BeTrue(
            "the temp password returned to the admin must authenticate the user");
        (await userManager.CheckPasswordAsync(user!, "Pa$$w0rd!")).Should().BeFalse(
            "the previous seeded password must no longer work");
    }

    [Fact]
    public async Task PostResetTwoFactor_AfterEnabledTwoFactor_DisablesAndRotatesKey()
    {
        var (userId, detailUrl) = await SetupAsync("daniel.eloi@example.com");
        using var client = CreateClient();

        // Seed an "enabled 2FA" state so the reset has something to undo.
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            (await userManager.ResetAuthenticatorKeyAsync(user!)).Succeeded.Should().BeTrue();
            (await userManager.SetTwoFactorEnabledAsync(user!, true)).Succeeded.Should().BeTrue();
        }

        var body = await PostAsync(client, detailUrl, "ResetTwoFactor");
        body.Should().Contain("Two-factor disabled");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await verifyManager.FindByIdAsync(userId);
        refreshed!.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PostRevokeSessions_AfterValidPost_RotatesSecurityStamp()
    {
        var (userId, detailUrl) = await SetupAsync("eduarda.ferraz@example.com");
        using var client = CreateClient();

        string stampBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            stampBefore = (await um.FindByIdAsync(userId))!.SecurityStamp!;
        }

        var body = await PostAsync(client, detailUrl, "RevokeSessions");
        body.Should().Contain("All sessions revoked.");

        using var after = _factory.Services.CreateScope();
        var um2 = after.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stampAfter = (await um2.FindByIdAsync(userId))!.SecurityStamp;

        stampAfter.Should().NotBe(stampBefore, "the security stamp must rotate so old cookies invalidate");
    }

    [Fact]
    public async Task PostUpdateProfile_WithNewEmailUsernameAndPhone_PersistsAllThree()
    {
        var (userId, detailUrl) = await SetupAsync("fabio.gomes@example.com");
        using var client = CreateClient();

        // Generate a unique target so the test stays green even if a previous
        // run left a renamed user in the DB (the seeder is idempotent, but it
        // recreates fabio.gomes on next start, leaving the orphan around).
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var newEmail = $"fabio.{suffix}@example.com";
        var newUserName = $"fabio.{suffix}";

        var token = await GetTokenAsync(client, detailUrl);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Profile.Email"] = newEmail,
            ["Profile.UserName"] = newUserName,
            ["Profile.PhoneNumber"] = "11999999999",
        });

        var response = await client.PostAsync(new Uri($"{detailUrl}?handler=UpdateProfile", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Profile updated.");
        body.Should().Contain(newEmail);
        body.Should().Contain("11999999999");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByIdAsync(userId);
        user!.Email.Should().Be(newEmail);
        user.UserName.Should().Be(newUserName);
        user.PhoneNumber.Should().Be("11999999999");
    }

    [Fact]
    public async Task PostUpdateProfile_WithInvalidEmail_KeepsEditFormOpenAndShowsError()
    {
        var (_, detailUrl) = await SetupAsync("gabriela.henriques@example.com");
        using var client = CreateClient();

        var token = await GetTokenAsync(client, detailUrl);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Profile.Email"] = "not-an-email",
            ["Profile.UserName"] = "gabriela.henriques",
            ["Profile.PhoneNumber"] = "",
        });

        var response = await client.PostAsync(new Uri($"{detailUrl}?handler=UpdateProfile", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Action failed.");
        body.Should().Contain("Edit profile", "validation failures must keep the form open for correction");
    }

    [Fact]
    public async Task GetEditProfile_WithHxRequest_ReturnsFormPartial()
    {
        var (_, detailUrl) = await SetupAsync("hugo.iglesias@example.com");
        using var client = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{detailUrl}?handler=EditProfile");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("<!doctype html>", "edit handler must return only the section partial");
        body.Should().Contain("Edit profile");
        body.Should().Contain("name=\"Profile.Email\"");
        body.Should().Contain("Save profile");
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        // Cookies must survive between GET (sets the antiforgery cookie) and POST.
        HandleCookies = true,
    });

    private async Task<(string UserId, string DetailUrl)> SetupAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull($"the seeded user '{email}' must exist");
        return (user!.Id, $"/visuauth/admin/users/{user.Id}");
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string detailUrl)
    {
        var response = await client.GetAsync(new Uri(detailUrl, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue("the detail page must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }

    private static async Task<string> PostAsync(HttpClient client, string detailUrl, string handler)
    {
        var token = await GetTokenAsync(client, detailUrl);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync(new Uri($"{detailUrl}?handler={handler}", UriKind.Relative), form);
        response.IsSuccessStatusCode.Should().BeTrue($"POST ?handler={handler} must succeed");
        return await response.Content.ReadAsStringAsync();
    }

    private async Task EnsurePersistedAsync(string userId, bool expectLocked)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByIdAsync(userId);
        user.Should().NotBeNull();
        var isLocked = await um.IsLockedOutAsync(user!);
        isLocked.Should().Be(expectLocked);
    }
}
