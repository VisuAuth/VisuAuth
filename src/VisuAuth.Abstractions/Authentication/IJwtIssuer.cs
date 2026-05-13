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
    Task<JwtTokenResult?> IssueAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Issued-token payload returned to API callers.</summary>
public sealed record JwtTokenResult
{
    public required string AccessToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>User id baked into the <c>sub</c> claim — handy for the client.</summary>
    public required string UserId { get; init; }

    /// <summary>Email baked into the <c>email</c> claim — also handy for the client.</summary>
    public required string Email { get; init; }

    /// <summary>Tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }
}
