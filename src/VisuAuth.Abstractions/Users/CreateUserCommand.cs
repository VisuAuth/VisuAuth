namespace VisuAuth.Abstractions.Users;

/// <summary>Inputs for creating a new user through <see cref="IUserStore.CreateAsync"/>.</summary>
public sealed record CreateUserCommand
{
    /// <summary>Email address for the new user.</summary>
    public required string Email { get; init; }

    /// <summary>Login user name; defaults to the email when left blank.</summary>
    public string? UserName { get; init; }

    /// <summary>Initial password; when blank the adapter generates a temporary one and returns it in the result metadata.</summary>
    public string? Password { get; init; }

    /// <summary>Phone number to set on the new user, when provided.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Tenant the user belongs to. Required when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>Skip email verification and mark the user as confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>Roles to assign on creation.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>Inputs for updating a user's profile through <see cref="IUserStore.UpdateAsync"/>; <see langword="null"/> fields are left unchanged.</summary>
public sealed record UpdateUserCommand
{
    /// <summary>New email address, or <see langword="null"/> to leave it unchanged.</summary>
    public string? Email { get; init; }

    /// <summary>New user name, or <see langword="null"/> to leave it unchanged.</summary>
    public string? UserName { get; init; }

    /// <summary>New phone number, or <see langword="null"/> to leave it unchanged.</summary>
    public string? PhoneNumber { get; init; }
}
