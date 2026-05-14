using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Integration tests for the register / forgot-password / reset-password /
/// confirm-email end-user pages.
/// </summary>
public sealed class RegisterAndResetTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex ResetLinkRegex = new(
        @"href=""(/visuauth/reset-password\?email=[^""]+&amp;token=[^""]+)""",
        RegexOptions.Compiled);

    private readonly VisuAuthTestFactory _factory;

    public RegisterAndResetTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRegister_DefaultRender_ShowsFormWithRequiredFields()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/register", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Create account</h1>");
        body.Should().Contain("name=\"Form.Email\"");
        body.Should().Contain("name=\"Form.Password\"");
        body.Should().Contain("name=\"Form.ConfirmPassword\"");
        body.Should().Contain("__RequestVerificationToken");
    }

    [Fact]
    public async Task PostRegister_WithMatchingPasswords_CreatesUser()
    {
        using var client = CreateClient();
        var email = $"new.{Guid.NewGuid():N}@example.com";

        var token = await GetTokenAsync(client, "/visuauth/register");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.Password"] = "NewUser!Pass1",
            ["Form.ConfirmPassword"] = "NewUser!Pass1",
        });

        var response = await client.PostAsync(new Uri("/visuauth/register", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Account created.",
            "the success banner must render after a clean registration");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await um.FindByEmailAsync(email)).Should().NotBeNull("the user must be persisted in Identity");
    }

    [Fact]
    public async Task PostRegister_WithMismatchedPasswords_ShowsValidationError()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/register");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = $"mismatch.{Guid.NewGuid():N}@example.com",
            ["Form.Password"] = "PasswordA1!",
            ["Form.ConfirmPassword"] = "PasswordB1!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/register", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Passwords do not match.");
    }

    [Fact]
    public async Task PostRegister_WithDuplicateEmail_ShowsIdentityError()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/register");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = "joao.kruger@example.com", // seeded
            ["Form.Password"] = "Whatever!Pass1",
            ["Form.ConfirmPassword"] = "Whatever!Pass1",
        });

        var response = await client.PostAsync(new Uri("/visuauth/register", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("We could not create your account.");
        body.Should().MatchRegex("already taken|already (in )?use|Duplicate");
    }

    [Fact]
    public async Task PostForgotPassword_WithKnownEmailInDevMode_ShowsResetLink()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/forgot-password");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = "laura.matos@example.com",
        });

        var response = await client.PostAsync(new Uri("/visuauth/forgot-password", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If an account exists for that email",
            "the generic anti-enumeration message must render regardless of the email being known");
        body.Should().Contain("Development mode:");

        var linkMatch = ResetLinkRegex.Match(body);
        linkMatch.Success.Should().BeTrue("dev mode must surface the reset link inline");
        linkMatch.Groups[1].Value.Should().StartWith("/visuauth/reset-password?email=");
    }

    [Fact]
    public async Task PostForgotPassword_WithUnknownEmail_ShowsSameGenericResponseWithoutLink()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/forgot-password");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = $"unknown.{Guid.NewGuid():N}@example.com",
        });

        var response = await client.PostAsync(new Uri("/visuauth/forgot-password", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("If an account exists for that email");
        ResetLinkRegex.IsMatch(body).Should().BeFalse(
            "no reset link must leak for an unknown email, even in dev mode");
    }

    [Fact]
    public async Task PostResetPassword_WithValidToken_UpdatesPassword()
    {
        // Provision a fresh user so other tests do not collide.
        string email = $"reset.{Guid.NewGuid():N}@example.com";
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            (await um.CreateAsync(user, "Original!Pass1")).Succeeded.Should().BeTrue();
            userId = user.Id;
        }

        // Step 1: kick off forgot-password to land the dev-mode reset link
        // on the page so we can parse it.
        using var client = CreateClient();
        var forgotToken = await GetTokenAsync(client, "/visuauth/forgot-password");
        var forgotForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = forgotToken,
            ["Form.Email"] = email,
        });
        var forgotResponse = await client.PostAsync(new Uri("/visuauth/forgot-password", UriKind.Relative), forgotForm);
        var forgotBody = await forgotResponse.Content.ReadAsStringAsync();

        var linkMatch = ResetLinkRegex.Match(forgotBody);
        linkMatch.Success.Should().BeTrue("the reset link must be on the page in dev mode");
        var resetUrl = WebUtility.HtmlDecode(linkMatch.Groups[1].Value);

        // Step 2: GET the reset page so we have a fresh antiforgery token.
        var resetGet = await client.GetAsync(new Uri(resetUrl, UriKind.Relative));
        var resetGetBody = await resetGet.Content.ReadAsStringAsync();
        var resetToken = TokenRegex.Match(resetGetBody).Groups[1].Value;
        resetToken.Should().NotBeNullOrEmpty();

        // Parse out the email + token from the reset URL so we can post them.
        var resetUri = new Uri("http://x" + resetUrl);
        var query = System.Web.HttpUtility.ParseQueryString(resetUri.Query);
        var passwordToken = query["token"];
        var resetEmail = query["email"];

        // Step 3: POST the new password.
        var resetForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = resetToken,
            ["email"] = resetEmail!,
            ["token"] = passwordToken!,
            ["Form.NewPassword"] = "Brand!New1",
            ["Form.ConfirmPassword"] = "Brand!New1",
        });
        var resetResponse = await client.PostAsync(new Uri("/visuauth/reset-password", UriKind.Relative), resetForm);
        var resetBody = await resetResponse.Content.ReadAsStringAsync();

        resetResponse.IsSuccessStatusCode.Should().BeTrue();
        resetBody.Should().Contain("Password updated.");

        using var verify = _factory.Services.CreateScope();
        var um2 = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user2 = await um2.FindByIdAsync(userId);
        (await um2.CheckPasswordAsync(user2!, "Brand!New1")).Should().BeTrue(
            "the new password must authenticate the user");
        (await um2.CheckPasswordAsync(user2!, "Original!Pass1")).Should().BeFalse(
            "the previous password must no longer work");
    }

    [Fact]
    public async Task PostResetPassword_WithGarbageToken_ShowsError()
    {
        using var client = CreateClient();
        var resetGet = await client.GetAsync(new Uri(
            "/visuauth/reset-password?email=laura.matos@example.com&token=not-a-real-token",
            UriKind.Relative));
        var resetGetBody = await resetGet.Content.ReadAsStringAsync();
        var token = TokenRegex.Match(resetGetBody).Groups[1].Value;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = "laura.matos@example.com",
            ["token"] = "not-a-real-token",
            ["Form.NewPassword"] = "DoesNot!Matter1",
            ["Form.ConfirmPassword"] = "DoesNot!Matter1",
        });
        var response = await client.PostAsync(new Uri("/visuauth/reset-password", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("Password updated.");
        body.Should().MatchRegex("Invalid token|Failed to reset password");
    }

    [Fact]
    public async Task GetConfirmEmail_WithValidToken_MarksUserConfirmed()
    {
        // Provision a user with EmailConfirmed=false so we have something to confirm.
        var email = $"confirm.{Guid.NewGuid():N}@example.com";
        string userId;
        string confirmToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                EmailConfirmed = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            (await um.CreateAsync(user, "Confirm!Pass1")).Succeeded.Should().BeTrue();
            userId = user.Id;
            confirmToken = await um.GenerateEmailConfirmationTokenAsync(user);
        }

        using var client = CreateClient();
        var url = $"/visuauth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(confirmToken)}";
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Email confirmed.");

        using var verify = _factory.Services.CreateScope();
        var um2 = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user2 = await um2.FindByIdAsync(userId);
        user2!.EmailConfirmed.Should().BeTrue("ConfirmEmailAsync must flip the flag");
    }

    [Fact]
    public async Task GetConfirmEmail_WithGarbageToken_ShowsError()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/visuauth/confirm-email?userId={Guid.NewGuid()}&token=garbage",
            UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("We could not confirm your email.");
    }

    [Theory]
    [InlineData("/visuauth/login")]
    [InlineData("/visuauth/register")]
    [InlineData("/visuauth/forgot-password")]
    [InlineData("/visuauth/confirm-email?userId=anything&token=anything")]
    public async Task EndUserPages_AnyOf_RenderWithEndUserLayoutNotAdmin(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        // The centred-card layout uses these classes; admin sidebar pages
        // do not.
        body.Should().Contain("va-enduser-shell",
            $"{url} must render with the end-user layout");
        body.Should().Contain("va-enduser-card");

        // Admin layout markers that must NOT bleed into the end-user pages.
        body.Should().NotContain("class=\"va-sidebar\"",
            $"{url} must not be wrapped in the admin sidebar layout");
        body.Should().NotContain("/visuauth/admin/users\" class=\"va-active\"",
            $"{url} must not borrow the admin sidebar nav");
    }

    [Fact]
    public async Task PostRegister_InDevelopmentMode_SurfacesRealConfirmEmailLink()
    {
        using var client = CreateClient();
        var email = $"with-confirm.{Guid.NewGuid():N}@example.com";

        var token = await GetTokenAsync(client, "/visuauth/register");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.Password"] = "Confirm!Pass1",
            ["Form.ConfirmPassword"] = "Confirm!Pass1",
        });

        var response = await client.PostAsync(new Uri("/visuauth/register", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        var confirmLinkMatch = Regex.Match(
            body,
            @"href=""(/visuauth/confirm-email\?userId=[^""]+&amp;token=[^""]+)""");
        confirmLinkMatch.Success.Should().BeTrue(
            "dev mode must surface a clickable confirm-email link with both userId and token");

        // Following the surfaced link should actually confirm the email.
        var confirmUrl = WebUtility.HtmlDecode(confirmLinkMatch.Groups[1].Value);
        var confirmResponse = await client.GetAsync(new Uri(confirmUrl, UriKind.Relative));
        var confirmBody = await confirmResponse.Content.ReadAsStringAsync();
        confirmBody.Should().Contain("Email confirmed.",
            "the inline link must drive a successful confirmation");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        user!.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task LoginFooter_DefaultRender_LinksRegisterAndForgotPassword()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("href=\"/visuauth/forgot-password\"");
        body.Should().Contain("href=\"/visuauth/register\"");
        body.Should().NotContain("(soon)",
            "the placeholder labels must be gone now that the pages exist");
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
