namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Backend-agnostic JWT issuer. The Identity adapter ships an HS256
/// implementation that pulls claims from <c>UserManager</c> + the current
/// tenant context; other adapters can substitute their own (e.g. an Entra
/// pass-through that re-wraps Microsoft Graph tokens).
/// </summary>
public interface IJwtIssuer
{
    /// <summary>
    /// Issues a JWT for the user identified by <paramref name="userId"/>.
    /// Returns <c>null</c> when the user no longer exists or is otherwise
    /// not eligible (locked out, disabled).
    /// </summary>
    /// <remarks>
    /// For a fresh sign-in, where the caller has just proven possession of the
    /// credentials. To mint a token from a previously-issued one, use
    /// <see cref="ReissueAsync"/> — it additionally enforces revocation.
    /// </remarks>
    Task<JwtTokenResult?> IssueAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-issues a JWT from a previously-issued one (the refresh flow). Behaves
    /// like <see cref="IssueAsync"/> but additionally requires
    /// <paramref name="presentedSecurityStamp"/> to match the user's current
    /// security stamp, returning <c>null</c> when it does not.
    /// <para>
    /// This is what makes revocation stick: rotating the stamp ("revoke
    /// sessions", lockout, a password change) must not merely block the old
    /// token at protected endpoints — it must also stop that token being
    /// exchanged here for a fresh one. The comparison <b>fails closed</b>: a
    /// token carrying no stamp at all is rejected too.
    /// </para>
    /// </summary>
    Task<JwtTokenResult?> ReissueAsync(
        string userId,
        string? presentedSecurityStamp,
        CancellationToken cancellationToken = default);
}

/// <summary>Issued-token payload returned to API callers.</summary>
public sealed record JwtTokenResult
{
    /// <summary>The signed JWT access token.</summary>
    public required string AccessToken { get; init; }

    /// <summary>When the token expires (its <c>exp</c> claim).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>User id baked into the <c>sub</c> claim — handy for the client.</summary>
    public required string UserId { get; init; }

    /// <summary>Email baked into the <c>email</c> claim — also handy for the client.</summary>
    public required string Email { get; init; }

    /// <summary>Tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }
}
