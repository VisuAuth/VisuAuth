namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Query parameters for the user listing endpoint.
/// </summary>
public sealed record UserFilter
{
    /// <summary>Free-text search across email, username, and phone.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>Restrict to a specific tenant. Ignored when multi-tenancy is disabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>If set, filters on the user's enabled state.</summary>
    public bool? IsEnabled { get; init; }

    /// <summary>If set, filters by lockout state.</summary>
    public bool? IsLockedOut { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public UserSortBy SortBy { get; init; } = UserSortBy.CreatedAt;

    public bool Descending { get; init; } = true;
}

public enum UserSortBy
{
    CreatedAt,
    Email,
    UserName,
    LastSignInAt,
}
