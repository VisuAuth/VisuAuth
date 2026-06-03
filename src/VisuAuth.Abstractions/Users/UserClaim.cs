namespace VisuAuth.Abstractions.Users;

/// <summary>
/// A single claim attached to a user. Maps to <c>AspNetUserClaims</c> rows for
/// the ASP.NET Identity adapter; other adapters project their native shape into it.
/// </summary>
public sealed record UserClaim
{
    /// <summary>Claim type (e.g. a URI or short name like <c>"department"</c>).</summary>
    public required string Type { get; init; }

    /// <summary>Claim value.</summary>
    public required string Value { get; init; }

    /// <summary>Issuer of the claim, when the backend records one.</summary>
    public string? Issuer { get; init; }
}
