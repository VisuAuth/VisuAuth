using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Exercises the real admin authorization gate (the default
/// <c>VisuAuth.Admin</c> policy = "require an authenticated user"). Uses a
/// factory that does <b>not</b> relax the policy, unlike the shared
/// <see cref="VisuAuthTestFactory"/> the behaviour tests use.
/// </summary>
public sealed class VisuAuthAdminAuthorizationTests
    : IClassFixture<VisuAuthAdminAuthorizationTests.SecureAdminFactory>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Uri AdminUsersUri = new("/visuauth/admin/users", UriKind.Relative);

    private readonly SecureAdminFactory _factory;

    public VisuAuthAdminAuthorizationTests(SecureAdminFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAdminUsers_Anonymous_RedirectsToLogin()
    {
        using var client = CreateClient(allowRedirects: false);

        var response = await client.GetAsync(AdminUsersUri);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/visuauth/login",
            "the default admin policy must challenge anonymous callers");
    }

    [Fact]
    public async Task GetAdminUsers_AfterSignIn_Returns200()
    {
        using var client = CreateClient(allowRedirects: false);

        // Real cookie sign-in through the end-user login page.
        var token = await GetTokenAsync(client, "/visuauth/login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = "admin@visuauth.dev",
            ["Form.Password"] = "Pa$$w0rd!",
            ["Form.RememberMe"] = "true",
        });
        var login = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), form);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // The auth cookie now rides on the client; the admin page must load.
        var response = await client.GetAsync(AdminUsersUri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient(bool allowRedirects) => _factory.CreateClient(
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
        match.Success.Should().BeTrue($"{url} must render an antiforgery-protected form");
        return match.Groups[1].Value;
    }

    /// <summary>Keeps the real admin policy in place (no relaxation).</summary>
    public sealed class SecureAdminFactory : VisuAuthTestFactory
    {
        protected override bool RelaxAdminAuthorization => false;
    }
}
