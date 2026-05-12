using System.Net;
using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace VisuAuth.AdminUi.Tests;

/// <summary>
/// Integration tests for <c>/visuauth/login</c> and <c>/visuauth/logout</c>.
/// Exercises the full authentication pipeline (cookie auth, antiforgery,
/// return-url sanitisation) end to end via the sample app.
/// </summary>
public sealed class LoginPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public LoginPageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_page_renders_form_with_required_fields()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Sign in</h1>", "the login heading must render");
        body.Should().Contain("name=\"Form.Email\"");
        body.Should().Contain("name=\"Form.Password\"");
        body.Should().Contain("name=\"Form.RememberMe\"");
        body.Should().Contain("__RequestVerificationToken",
            "the form must auto-inject the antiforgery token");
    }

    [Fact]
    public async Task Successful_sign_in_sets_auth_cookie_and_redirects_to_return_url()
    {
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login?returnUrl=%2Fvisuauth%2Fadmin%2Fusers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/visuauth/admin/users",
            ["Form.Email"] = "admin@visuauth.dev",
            ["Form.Password"] = "Pa$$w0rd!",
            ["Form.RememberMe"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users");

        // The Identity cookie must be present in the Set-Cookie response.
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wrong_password_renders_generic_error_and_does_not_authenticate()
    {
        using var client = CreateClient();

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = "admin@visuauth.dev",
            ["Form.Password"] = "wrong-password",
            ["Form.RememberMe"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue("a failed sign-in re-renders the form, not a redirect");
        body.Should().Contain("Email or password is incorrect.",
            "wrong password must surface a generic, email-agnostic error");

        // No identity cookie must be set on failure.
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? Array.Empty<string>())
            .Should().NotContain(c => c.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal),
                "no auth cookie should be set on a failed attempt");
    }

    [Fact]
    public async Task Unknown_email_returns_the_same_generic_error()
    {
        using var client = CreateClient();

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = $"unknown.{Guid.NewGuid():N}@example.com",
            ["Form.Password"] = "Whatever!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Email or password is incorrect.",
            "unknown-email response must not leak whether the email exists");
    }

    [Fact]
    public async Task Login_rejects_non_local_returnUrl_after_success()
    {
        using var client = CreateClient(allowRedirects: false);

        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "https://attacker.example/phish",
            ["Form.Email"] = "alice.silva@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });

        var response = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/",
            "non-local return URLs must fall back to a safe local default");
    }

    [Fact]
    public async Task Logout_clears_the_cookie_and_redirects()
    {
        using var client = CreateClient(allowRedirects: false);

        // Sign in first. isabela.jorge is one of the seeded users that no other
        // test mutates (no password reset, no role / lockout / profile changes),
        // so her seeded password is always good.
        var loginToken = await GetTokenAsync(client, "/visuauth/login");
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["Form.Email"] = "isabela.jorge@example.com",
            ["Form.Password"] = "Pa$$w0rd!",
        });
        var loginResponse = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), loginForm);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect, "preconditions: sign-in must succeed");

        // Then log out.
        var logoutToken = await GetTokenAsync(client, "/visuauth/logout");
        var logoutForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken,
        });
        var logoutResponse = await client.PostAsync(new Uri("/visuauth/logout", UriKind.Relative), logoutForm);

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logoutResponse.Headers.Location!.ToString().Should().Be("/");

        // The Set-Cookie must clear the identity cookie (empty value, expires in the past).
        logoutResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c =>
            c.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal) &&
            (c.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal) ||
             c.Contains("=;", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Logout_GET_renders_a_confirmation_page_without_signing_out()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/logout", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Sign out</h1>");
        body.Should().Contain("<form", "GET must show a POST form, not perform the sign-out itself");
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
}
