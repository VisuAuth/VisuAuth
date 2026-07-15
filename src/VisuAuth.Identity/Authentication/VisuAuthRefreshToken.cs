using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// A persisted refresh token. Only the <see cref="TokenHash"/> is stored — the
/// raw value exists once, in the response to the client. A database leak
/// therefore does not hand out usable tokens.
/// </summary>
/// <remarks>
/// Rows are retained after being redeemed (with <see cref="RevokedAt"/> set)
/// rather than deleted, because a replay of a retired token is exactly the
/// signal that it leaked — see <see cref="FamilyId"/>.
/// </remarks>
public sealed class VisuAuthRefreshToken : IMultiTenantEntity
{
    /// <summary>Surrogate key.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// SHA-256 of the raw token, hex-encoded. Unique: lookup happens by
    /// hashing what the client presented.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Groups a token with its rotation ancestors. Every redemption issues a
    /// replacement in the same family, so if a retired token is replayed we can
    /// revoke the whole lineage — the attacker and the legitimate client both
    /// hold tokens from it, and there is no way to tell which is which.
    /// </summary>
    public string FamilyId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user this token authenticates.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <summary>When the token was minted.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the token stops being redeemable.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Set when the token is redeemed (rotated) or revoked. A non-null value
    /// means the token must never be accepted again.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// The hash of the token that replaced this one on rotation. Non-null only
    /// for tokens retired by a successful redemption, which distinguishes
    /// "rotated normally" from "revoked outright".
    /// </summary>
    public string? ReplacedByHash { get; set; }
}
