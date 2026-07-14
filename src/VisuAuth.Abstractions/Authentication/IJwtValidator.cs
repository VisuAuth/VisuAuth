namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Backend-agnostic validator for VisuAuth-issued JWTs. Complements
/// <see cref="IJwtIssuer"/>: where the issuer mints tokens, the validator
/// authenticates a token the client presents back — verifying the signature,
/// issuer, and audience so a forged token can never be trusted.
/// </summary>
/// <remarks>
/// The refresh endpoint (<c>/visuauth/api/auth/refresh</c>) accepts an
/// <em>expired</em> token on purpose, so this contract deliberately does not
/// validate the token lifetime. It still validates every other parameter —
/// crucially the cryptographic signature — so a caller cannot forge a
/// <c>sub</c> and have a fresh token minted for an arbitrary user.
/// </remarks>
public interface IJwtValidator
{
    /// <summary>
    /// Validates the token's signature, issuer, and audience (but not its
    /// lifetime) and returns the <c>sub</c> claim when the token is
    /// authentic. Returns <c>null</c> for any token that fails validation,
    /// cannot be read, or carries no subject.
    /// </summary>
    /// <param name="token">The raw JWT, without the <c>Bearer</c> prefix.</param>
    string? ValidateSignatureAndReadSubject(string token);
}
