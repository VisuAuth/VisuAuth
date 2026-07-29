using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VisuAuth.IntegrationTests.Security;

/// <summary>
/// A consumer hardening their app with a global
/// <see cref="AuthorizationOptions.FallbackPolicy"/> of "require an
/// authenticated user" must not thereby lock everyone out of signing in.
/// </summary>
/// <remarks>
/// A fallback policy applies to every endpoint with no authorization metadata of
/// its own. Sign-in pages carry none by default, so before the fix the fallback
/// caught them too and the result was a deadlock: <c>/visuauth/login</c>
/// challenged, which redirected to <c>/visuauth/login</c>, which challenged
/// again. <c>POST /visuauth/api/auth/login</c> simply answered 401, leaving no
/// way to obtain a token at all.
/// </remarks>
public sealed class FallbackPolicyTests(FallbackPolicyTests.RequireAuthEverywhereFactory factory)
    : IClassFixture<FallbackPolicyTests.RequireAuthEverywhereFactory>
{
    private readonly RequireAuthEverywhereFactory _factory = factory;

    [Theory]
    [InlineData("/visuauth/login")]
    [InlineData("/visuauth/register")]
    [InlineData("/visuauth/forgot-password")]
    [InlineData("/visuauth/reset-password")]
    [InlineData("/visuauth/confirm-email")]
    [InlineData("/visuauth/two-factor/verify")]
    public async Task GetAuthPage_UnderAGlobalFallbackPolicy_StaysReachableAnonymously(string url)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Found,
            $"{url} is part of authenticating, so a fallback policy must not redirect it to sign-in");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostApiLogin_UnderAGlobalFallbackPolicy_StillIssuesAToken()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/visuauth/api/auth/login", UriKind.Relative),
            new { email = "laura.matos@example.com", password = "Pa$$w0rd!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the login endpoint is how a caller obtains credentials — a fallback policy cannot gate it");
    }

    [Fact]
    public async Task GetAdmin_UnderAGlobalFallbackPolicy_IsStillGated()
    {
        // The other half of the contract: pinning the sign-in pages anonymous
        // must not have loosened the admin surface.
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Found,
            "the admin dashboard stays behind its policy");
    }

    [Fact]
    public async Task GetTwoFactorSetup_UnderAGlobalFallbackPolicy_IsStillGated()
    {
        // Pages that declare [Authorize] keep it — the convention only pins the
        // level a page already had.
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/two-factor/setup", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Found,
            "two-factor setup requires a signed-in user by design");
    }

    private HttpClient CreateClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    /// <summary>
    /// Boots the sample with the global "everything needs auth" fallback a
    /// security-conscious consumer would add. The admin gate is left at its real
    /// strength so this fixture also proves the admin stays closed.
    /// </summary>
    public sealed class RequireAuthEverywhereFactory : VisuAuthTestFactory
    {
        protected override bool RelaxAdminAuthorization => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
                services.Configure<AuthorizationOptions>(options =>
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build()));
        }
    }
}
