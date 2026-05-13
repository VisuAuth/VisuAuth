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
    /// the HS256 minimum.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

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
}
