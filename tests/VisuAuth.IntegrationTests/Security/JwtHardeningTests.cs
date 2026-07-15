using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace VisuAuth.IntegrationTests.Security;

/// <summary>
/// Adversarial coverage for the JWT refresh endpoint — classic token attacks
/// that must be rejected. Complements the happy-path and forged-signature
/// cases in <c>AuthApiTests</c>.
/// </summary>
public sealed class JwtHardeningTests : IClassFixture<LegacyRefreshTokenFactory>
{
    private static readonly Uri RefreshUri = new("/visuauth/api/auth/refresh", UriKind.Relative);

    private const string Issuer = "VisuAuth.Sample";
    private const string PrimaryKey = "sample-dev-signing-key-do-not-use-in-production-or-anywhere-else";

    private readonly LegacyRefreshTokenFactory _factory;

    public JwtHardeningTests(LegacyRefreshTokenFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostRefresh_WithUnsignedAlgNoneToken_Returns401()
    {
        // A token with no signature ("alg":"none"). Signed tokens are required,
        // so an algorithm-stripping attack must be rejected.
        var handler = new JwtSecurityTokenHandler();
        var unsigned = handler.WriteToken(new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: null));

        var response = await RefreshAsync(unsigned);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_WithAuthenticTokenMissingSubject_Returns401()
    {
        // Correctly signed, but no `sub` — there is no user to reissue for.
        var token = BuildToken(claims: []);

        var response = await RefreshAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> RefreshAsync(string token)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private static string BuildToken(Claim[] claims)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PrimaryKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
