namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Issues and redeems opaque refresh tokens for the mobile / native channel.
/// Opt-in: without <c>AddVisuAuthRefreshTokens()</c> a no-op implementation
/// reports <see cref="IsEnabled"/> = <c>false</c> and the API keeps reissuing
/// from the access token, so callers never have to branch on configuration.
/// </summary>
/// <remarks>
/// <para>
/// A refresh token is a high-entropy random string that means nothing on its
/// own — the server stores only its hash. That is what makes it revocable
/// individually, unlike an access token, which is self-contained and valid
/// wherever it is presented until it expires.
/// </para>
/// <para>
/// Tokens are <b>single-use</b>: redeeming one rotates it, returning a
/// replacement and retiring the old value. Presenting an already-redeemed
/// token is evidence that it leaked (the legitimate client and an attacker
/// both hold it), so the whole token family is revoked and the user must sign
/// in again.
/// </para>
/// </remarks>
public interface IRefreshTokenService
{
    /// <summary>
    /// Whether refresh tokens are wired. When <c>false</c>, <see cref="IssueAsync"/>
    /// returns <c>null</c> and <see cref="RedeemAsync"/> always fails.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Mints a new refresh token for the user, starting a new family. Returns
    /// the raw token to hand to the client — it is not recoverable afterwards,
    /// since only its hash is stored. Returns <c>null</c> when disabled.
    /// </summary>
    Task<string?> IssueAsync(string userId, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a token: validates it, rotates it, and reports who it belonged
    /// to. Any failure — unknown, expired, already revoked, or replayed — is
    /// reported as <see cref="RefreshRedemption.Failed"/> so callers cannot
    /// distinguish the cases and probe for valid tokens.
    /// </summary>
    Task<RefreshRedemption> RedeemAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every outstanding token for a user — the "sign out everywhere"
    /// companion to rotating their security stamp. No-op when disabled.
    /// </summary>
    Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of redeeming a refresh token.</summary>
public sealed record RefreshRedemption
{
    /// <summary>Whether the presented token was valid and has been rotated.</summary>
    public bool Succeeded { get; init; }

    /// <summary>The user the token belonged to. Only set on success.</summary>
    public string? UserId { get; init; }

    /// <summary>The tenant recorded when the token was issued. Only set on success.</summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// The replacement token to hand back to the client. Only set on success —
    /// the presented one is now retired.
    /// </summary>
    public string? RotatedToken { get; init; }

    /// <summary>The single failure result. Deliberately carries no reason.</summary>
    public static RefreshRedemption Failed { get; } = new() { Succeeded = false };

    /// <summary>Builds a successful redemption.</summary>
    public static RefreshRedemption Success(string userId, string? tenantId, string rotatedToken) => new()
    {
        Succeeded = true,
        UserId = userId,
        TenantId = tenantId,
        RotatedToken = rotatedToken,
    };
}

/// <summary>Configuration for the opt-in refresh-token plugin.</summary>
public sealed class RefreshTokenOptions
{
    /// <summary>
    /// How long a refresh token stays valid. Rotation makes this a sliding
    /// window in practice: each redemption issues a replacement with a fresh
    /// lifetime, so an active client stays signed in while an idle one is cut
    /// off after this long. Defaults to 30 days.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);
}
