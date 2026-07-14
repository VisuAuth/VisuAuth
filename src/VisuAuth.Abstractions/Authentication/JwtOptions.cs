namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Configuration for the JWT issuer that powers the mobile / native API
/// channel (<c>/visuauth/api/auth</c>). HS256 only at v0.1 — no JWKS, no
/// rotation, no asymmetric signing. Consumers needing OIDC pair VisuAuth
/// with a real OIDC server like Duende IdentityServer.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Symmetric signing key. Must be at least 32 UTF-8 bytes long for
    /// HS256. Load from configuration / secret store — never check into
    /// source. The issuer throws at startup if it sees a key shorter than
    /// the HS256 minimum. This is the <em>only</em> key used to <em>sign</em>
    /// newly-issued tokens.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Extra keys accepted when <em>validating</em> a token, but never used to
    /// sign one. This is the seam for rotating <see cref="SigningKey"/> without
    /// a big-bang: when you rotate, set the new secret as <see cref="SigningKey"/>
    /// and move the old one here for at least one token <see cref="LifetimeMinutes"/>
    /// so tokens already in flight keep validating, then drop it. Each key must
    /// also be at least 32 UTF-8 bytes for HS256. Empty by default (no rotation
    /// in progress).
    /// </summary>
    public IList<string> AdditionalValidationKeys { get; set; } = new List<string>();

    /// <summary>Token issuer claim (<c>iss</c>). Defaults to <c>VisuAuth</c>.</summary>
    public string Issuer { get; set; } = "VisuAuth";

    /// <summary>Token audience claim (<c>aud</c>). Defaults to <c>VisuAuth</c>.</summary>
    public string Audience { get; set; } = "VisuAuth";

    /// <summary>How long each issued token stays valid. Defaults to 60 minutes.</summary>
    public int LifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Skew allowed when validating <c>nbf</c> / <c>exp</c>. Five minutes is
    /// the canonical Microsoft default — covers typical clock drift between
    /// the API server and a mobile client.
    /// </summary>
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>
    /// When <c>true</c> (default), the bearer scheme validates the token's
    /// <c>visuauth_stamp</c> claim against the user's current security stamp
    /// on every authenticated request. Admin actions that rotate the stamp —
    /// lockout, "revoke sessions", a password change — then invalidate
    /// already-issued tokens immediately instead of leaving them valid until
    /// <c>exp</c>. Costs one user lookup per authenticated request; set to
    /// <c>false</c> to trade immediate revocation for that saved lookup
    /// (tokens then stay valid until they expire).
    /// </summary>
    public bool ValidateSecurityStamp { get; set; } = true;
}
