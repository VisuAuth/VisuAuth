using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.Api;

/// <summary>
/// Verifies JWT signing-key rotation end to end: a token signed with a key
/// that lives in <c>JwtOptions.AdditionalValidationKeys</c> (a rotated-out
/// key) still authenticates a bearer-protected endpoint, while a token signed
/// with an unconfigured key does not.
/// </summary>
public sealed class JwtKeyRotationTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Uri MeUri = new("/api/me", UriKind.Relative);

    private const string Issuer = "VisuAuth.Sample";
    private const string PrimaryKey = "sample-dev-signing-key-do-not-use-in-production-or-anywhere-else";
    private const string RotatedOutKey = "sample-rotated-out-key-kept-for-validation-only-32b+";
    private const string UnconfiguredKey = "a-key-the-sample-never-configured-32-bytes-minimum!!";
    private const string SecurityStampClaimType = "visuauth_stamp";

    private readonly VisuAuthTestFactory _factory;

    public JwtKeyRotationTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProtected_WithTokenSignedByRotatedOutKey_Returns200()
    {
        var token = await BuildUserTokenAsync("laura.matos@example.com", RotatedOutKey);

        var response = await SendWithBearerAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProtected_WithTokenSignedByUnconfiguredKey_Returns401()
    {
        var token = await BuildUserTokenAsync("laura.matos@example.com", UnconfiguredKey);

        var response = await SendWithBearerAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(string token)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUri);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<string> BuildUserTokenAsync(string email, string signingKey)
    {
        // A token accepted by /api/me must also carry the user's current
        // security stamp (bearer validates it), so read both from the store.
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        var stamp = await userManager.GetSecurityStampAsync(user!);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user!.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(SecurityStampClaimType, stamp),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
