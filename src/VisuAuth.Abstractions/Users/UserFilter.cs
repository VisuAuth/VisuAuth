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

    /// <summary>If set, returns only users in this role (matched by name).</summary>
    public string? Role { get; init; }

    /// <summary>If set, filters by email confirmation flag.</summary>
    public bool? EmailConfirmed { get; init; }

    /// <summary>If set, filters by two-factor enrolment.</summary>
    public bool? TwoFactorEnabled { get; init; }

    /// <summary>
    /// Opaque forward cursor from a previous <see cref="Common.PagedResult{T}.NextCursor"/>.
    /// Null/empty requests the first page. Treat as a black box — never build
    /// it by hand.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>Maximum number of users to return per page. Defaults to 25.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Field the results are ordered by. Defaults to <see cref="UserSortBy.CreatedAt"/>.</summary>
    public UserSortBy SortBy { get; init; } = UserSortBy.CreatedAt;

    /// <summary>Order direction; <c>true</c> (default) sorts descending.</summary>
    public bool Descending { get; init; } = true;
}

/// <summary>Sort key for a <see cref="UserFilter"/> query.</summary>
public enum UserSortBy
{
    /// <summary>Order by account creation time.</summary>
    CreatedAt,

    /// <summary>Order by email address.</summary>
    Email,

    /// <summary>Order by login user name.</summary>
    UserName,

    /// <summary>Order by most recent successful sign-in.</summary>
    LastSignInAt,
}
