using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Api;

/// <summary>
/// Verifies the bearer scheme validates the <c>visuauth_stamp</c> claim, so
/// rotating a user's security stamp ("revoke sessions", lockout, password
/// change) invalidates already-issued JWTs on their next use instead of
/// leaving them valid until <c>exp</c>. Exercised through the sample app's
/// <c>/api/me</c> bearer-protected endpoint.
/// </summary>
public sealed class JwtSecurityStampTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Uri LoginUri = new("/visuauth/api/auth/login", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/me", UriKind.Relative);

    private const string Email = "laura.matos@example.com";
    private const string Password = "Pa$$w0rd!";

    private readonly VisuAuthTestFactory _factory;

    public JwtSecurityStampTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProtected_WithFreshToken_Returns200()
    {
        using var client = _factory.CreateClient();
        var token = await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, MeUri);
        request.Headers.Add("Authorization", $"Bearer {token}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProtected_AfterSecurityStampRotation_Returns401()
    {
        using var client = _factory.CreateClient();
        var token = await SignInAsync(client);

        // Simulate an admin "revoke sessions" / password change by rotating
        // the user's security stamp out from under the already-issued token.
        await RotateSecurityStampAsync(Email);

        using var request = new HttpRequestMessage(HttpMethod.Get, MeUri);
        request.Headers.Add("Authorization", $"Bearer {token}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(LoginUri, new { email = Email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task RotateSecurityStampAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        (await userManager.UpdateSecurityStampAsync(user!)).Succeeded.Should().BeTrue();
    }
}
