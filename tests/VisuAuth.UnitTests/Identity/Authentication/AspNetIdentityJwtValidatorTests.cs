using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Unit tests for <see cref="AspNetIdentityJwtValidator"/> — the refresh-flow
/// validator. Covers signature enforcement and multi-key acceptance (JWT
/// signing-key rotation).
/// </summary>
public sealed class AspNetIdentityJwtValidatorTests
{
    private const string Issuer = "VisuAuth.Test";
    private const string Audience = "VisuAuth.Test";
    private const string PrimaryKey = "primary-signing-key-that-is-at-least-32-bytes-long!!";
    private const string RotatedOutKey = "rotated-out-key-also-at-least-32-bytes-long-really!!";
    private const string StrangerKey = "some-other-key-nobody-configured-32-bytes-minimum!!!";

    [Fact]
    public void ValidateSignatureAndRead_TokenSignedWithPrimaryKey_ReturnsSubject()
    {
        var validator = CreateValidator(PrimaryKey, RotatedOutKey);
        var token = BuildToken("user-1", PrimaryKey, DateTime.UtcNow.AddMinutes(30));

        validator.ValidateSignatureAndRead(token)!.Subject.Should().Be("user-1");
    }

    [Fact]
    public void ValidateSignatureAndRead_TokenSignedWithAdditionalValidationKey_ReturnsSubject()
    {
        // Rotation in progress: the token was signed with the previous key,
        // which now lives in AdditionalValidationKeys. It must still validate.
        var validator = CreateValidator(PrimaryKey, RotatedOutKey);
        var token = BuildToken("user-2", RotatedOutKey, DateTime.UtcNow.AddMinutes(30));

        validator.ValidateSignatureAndRead(token)!.Subject.Should().Be("user-2");
    }

    [Fact]
    public void ValidateSignatureAndRead_ExpiredTokenSignedWithKnownKey_ReturnsSubject()
    {
        // Refresh accepts expired tokens; only the signature must hold.
        var validator = CreateValidator(PrimaryKey, RotatedOutKey);
        var token = BuildToken("user-3", RotatedOutKey, DateTime.UtcNow.AddMinutes(-30));

        validator.ValidateSignatureAndRead(token)!.Subject.Should().Be("user-3");
    }

    [Fact]
    public void ValidateSignatureAndRead_TokenSignedWithUnknownKey_ReturnsNull()
    {
        var validator = CreateValidator(PrimaryKey, RotatedOutKey);
        var token = BuildToken("user-4", StrangerKey, DateTime.UtcNow.AddMinutes(30));

        validator.ValidateSignatureAndRead(token).Should().BeNull();
    }

    [Fact]
    public void ValidateSignatureAndRead_TokenWithWrongIssuer_ReturnsNull()
    {
        var validator = CreateValidator(PrimaryKey, RotatedOutKey);
        var token = BuildToken("user-5", PrimaryKey, DateTime.UtcNow.AddMinutes(30), issuer: "https://evil.example");

        validator.ValidateSignatureAndRead(token).Should().BeNull();
    }

    [Fact]
    public void ValidateSignatureAndRead_TokenWithSecurityStamp_SurfacesTheStamp()
    {
        // The refresh flow compares this against the user's current stamp, so
        // the validator has to hand it back rather than swallow it.
        var validator = CreateValidator(PrimaryKey);
        var token = BuildToken("user-6", PrimaryKey, DateTime.UtcNow.AddMinutes(30), stamp: "STAMP-ABC");

        validator.ValidateSignatureAndRead(token)!.SecurityStamp.Should().Be("STAMP-ABC");
    }

    [Fact]
    public void ValidateSignatureAndRead_TokenWithoutSecurityStamp_ReturnsNullStamp()
    {
        var validator = CreateValidator(PrimaryKey);
        var token = BuildToken("user-7", PrimaryKey, DateTime.UtcNow.AddMinutes(30));

        validator.ValidateSignatureAndRead(token)!.SecurityStamp.Should().BeNull();
    }

    private static AspNetIdentityJwtValidator CreateValidator(params string[] keys)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.Select(k =>
                (SecurityKey)new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k))).ToList(),
            ValidateLifetime = true,
            NameClaimType = "sub",
        };

        return new AspNetIdentityJwtValidator(parameters);
    }

    private static string BuildToken(
        string subject,
        string signingKey,
        DateTime expires,
        string issuer = Issuer,
        string? stamp = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, subject) };
        if (stamp is not null)
        {
            claims.Add(new Claim(VisuAuthClaimTypes.SecurityStamp, stamp));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-60),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
