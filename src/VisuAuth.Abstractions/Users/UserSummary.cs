namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Minimal projection of a user suitable for lists and search results.
/// </summary>
public sealed record UserSummary
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public string? UserName { get; init; }

    public string? PhoneNumber { get; init; }

    public bool IsEnabled { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool TwoFactorEnabled { get; init; }

    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>Identifier of the tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastSignInAt { get; init; }
}
