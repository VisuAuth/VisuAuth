namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Minimal projection of a user suitable for lists and search results.
/// </summary>
public sealed record UserSummary
{
    /// <summary>Stable backend identifier for the user.</summary>
    public required string Id { get; init; }

    /// <summary>The user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Login user name, when the backend tracks one distinct from the email.</summary>
    public string? UserName { get; init; }

    /// <summary>Phone number on file, when present.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>True when the user can currently sign in (not locked out / disabled).</summary>
    public bool IsEnabled { get; init; }

    /// <summary>True when the user's email address has been confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>True when two-factor authentication is enabled for the user.</summary>
    public bool TwoFactorEnabled { get; init; }

    /// <summary>End of the current lockout window, when one is active; otherwise <see langword="null"/>.</summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>Identifier of the tenant the user belongs to, when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>When the user account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Timestamp of the user's most recent successful sign-in, when known.</summary>
    public DateTimeOffset? LastSignInAt { get; init; }
}
