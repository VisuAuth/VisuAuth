using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// HS256 <see cref="IJwtValidator"/> backed by the same
/// <see cref="TokenValidationParameters"/> the JWT bearer scheme uses, so a
/// token that would authenticate a protected endpoint validates here too —
/// with the single deliberate exception that the lifetime is not checked
/// (the refresh flow accepts expired tokens). The signature, issuer, and
/// audience are always enforced.
/// </summary>
public sealed class AspNetIdentityJwtValidator : IJwtValidator
{
    private readonly TokenValidationParameters _parameters;
    private readonly JwtSecurityTokenHandler _handler;

    /// <param name="parameters">
    /// Validation parameters mirroring the bearer scheme. The constructor
    /// clones them and forces <see cref="TokenValidationParameters.ValidateLifetime"/>
    /// off so callers cannot accidentally enable a lifetime check that would
    /// break the refresh contract.
    /// </param>
    public AspNetIdentityJwtValidator(TokenValidationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _parameters = parameters.Clone();
        _parameters.ValidateLifetime = false;

        // The issuer writes the subject under the raw "sub" claim and the
        // bearer scheme sets MapInboundClaims = false; mirror that here so
        // ValidateSignatureAndReadSubject reads the same claim type back.
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    /// <inheritdoc />
    public string? ValidateSignatureAndReadSubject(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_handler.CanReadToken(token))
        {
            return null;
        }

        try
        {
            var principal = _handler.ValidateToken(token, _parameters, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return string.IsNullOrEmpty(sub) ? null : sub;
        }
        catch (SecurityTokenException)
        {
            // Signature / issuer / audience mismatch, or a structurally
            // invalid token. Any of these means "not authentic" — surface it
            // as a null subject so the caller returns 401.
            return null;
        }
    }
}
