namespace VisuAuth.Abstractions.Users;

/// <summary>
/// A single claim attached to a user. Maps to <c>AspNetUserClaims</c> rows for
/// the ASP.NET Identity adapter; other adapters project their native shape into it.
/// </summary>
public sealed record UserClaim
{
    public required string Type { get; init; }

    public required string Value { get; init; }

    public string? Issuer { get; init; }
}
