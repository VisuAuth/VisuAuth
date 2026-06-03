namespace VisuAuth.Abstractions.Users;

public sealed record CreateUserCommand
{
    public required string Email { get; init; }

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public string? PhoneNumber { get; init; }

    /// <summary>Tenant the user belongs to. Required when multi-tenancy is enabled.</summary>
    public string? TenantId { get; init; }

    /// <summary>Skip email verification and mark the user as confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>Roles to assign on creation.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record UpdateUserCommand
{
    public string? Email { get; init; }

    public string? UserName { get; init; }

    public string? PhoneNumber { get; init; }
}
