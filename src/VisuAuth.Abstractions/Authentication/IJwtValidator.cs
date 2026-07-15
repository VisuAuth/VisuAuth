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
    /// lifetime) and returns the claims the refresh flow needs. Returns
    /// <c>null</c> for any token that fails validation, cannot be read, or
    /// carries no subject.
    /// </summary>
    /// <param name="token">The raw JWT, without the <c>Bearer</c> prefix.</param>
    ValidatedJwt? ValidateSignatureAndRead(string token);
}

/// <summary>
/// The authenticated contents of a presented token that the refresh flow acts
/// on. Only what refresh needs — deliberately not a general claims bag.
/// </summary>
public sealed record ValidatedJwt
{
    /// <summary>The <c>sub</c> claim: the user the token was minted for.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The <c>visuauth_stamp</c> claim: the user's security stamp as it stood
    /// when the token was issued. <c>null</c> when the token carries no stamp,
    /// which the refresh flow must treat as a mismatch rather than a pass.
    /// </summary>
    public string? SecurityStamp { get; init; }
}
