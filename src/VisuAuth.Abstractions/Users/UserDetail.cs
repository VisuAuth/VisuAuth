namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Full projection of a user for the admin detail page. Carries everything
/// <see cref="UserSummary"/> exposes plus the user's claims, roles, external
/// logins, and the auxiliary state required to render lockout and 2FA panels.
/// </summary>
public sealed record UserDetail
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public string? UserName { get; init; }

    public string? PhoneNumber { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool PhoneNumberConfirmed { get; init; }

    /// <summary>True when the user is not currently locked out.</summary>
    public bool IsEnabled { get; init; }

    public bool TwoFactorEnabled { get; init; }

    /// <summary>Whether the backend will enforce lockout for this user after failed attempts.</summary>
    public bool LockoutEnabled { get; init; }

    /// <summary>End of the current lockout window, when one is active.</summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>Number of consecutive failed sign-in attempts since the last success.</summary>
    public int AccessFailedCount { get; init; }

    /// <summary>Identifier of the tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastSignInAt { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<UserClaim> Claims { get; init; } = [];

    public IReadOnlyList<ExternalLogin> ExternalLogins { get; init; } = [];
}
