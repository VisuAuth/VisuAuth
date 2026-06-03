namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Full projection of a user for the admin detail page. Carries everything
/// <see cref="UserSummary"/> exposes plus the user's claims, roles, external
/// logins, and the auxiliary state required to render lockout and 2FA panels.
/// </summary>
public sealed record UserDetail
{
    /// <summary>Stable backend identifier for the user.</summary>
    public required string Id { get; init; }

    /// <summary>The user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Login user name, when the backend tracks one distinct from the email.</summary>
    public string? UserName { get; init; }

    /// <summary>Phone number on file, when present.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>True when the user's email address has been confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>True when the user's phone number has been confirmed.</summary>
    public bool PhoneNumberConfirmed { get; init; }

    /// <summary>True when the user is not currently locked out.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>True when two-factor authentication is enabled for the user.</summary>
    public bool TwoFactorEnabled { get; init; }

    /// <summary>Whether the backend will enforce lockout for this user after failed attempts.</summary>
    public bool LockoutEnabled { get; init; }

    /// <summary>End of the current lockout window, when one is active.</summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>Number of consecutive failed sign-in attempts since the last success.</summary>
    public int AccessFailedCount { get; init; }

    /// <summary>Identifier of the tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>When the user account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Timestamp of the user's most recent successful sign-in, when known.</summary>
    public DateTimeOffset? LastSignInAt { get; init; }

    /// <summary>Names of the roles assigned to the user.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Custom claims attached to the user.</summary>
    public IReadOnlyList<UserClaim> Claims { get; init; } = [];

    /// <summary>External identity-provider logins linked to the user.</summary>
    public IReadOnlyList<ExternalLogin> ExternalLogins { get; init; } = [];
}
