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
    public ValidatedJwt? ValidateSignatureAndRead(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_handler.CanReadToken(token))
        {
            return null;
        }

        try
        {
            var principal = _handler.ValidateToken(token, _parameters, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(sub))
            {
                return null;
            }

            return new ValidatedJwt
            {
                Subject = sub,
                SecurityStamp = principal.FindFirst(VisuAuthClaimTypes.SecurityStamp)?.Value,
            };
        }
#pragma warning disable CA1031 // Deliberately broad — see below.
        catch (Exception)
        {
            // Fail closed on ANYTHING the validation pipeline throws.
            //
            // The obvious catch here is SecurityTokenException (signature /
            // issuer / audience mismatch). That is too narrow: IdentityModel's
            // behaviour depends on process-wide state, and once
            // Microsoft.Identity.Web is loaded in the same process — as it is
            // whenever the Entra sign-in package is wired — a rejected token
            // can surface as a different exception type. Anything that escaped
            // this catch would leave the endpoint answering 500 instead of 401
            // on a token it correctly refused to trust: an invalid token
            // reported as a server fault.
            //
            // There is no exception from this method that should mean
            // "authentic". Rejecting is always the safe answer, so the catch is
            // deliberately total.
            return null;
        }
#pragma warning restore CA1031
    }
}
