namespace VisuAuth.Abstractions.Users;

/// <summary>
/// An external identity provider login linked to a user (Google, Microsoft, Apple, etc.).
/// </summary>
public sealed record ExternalLogin
{
    /// <summary>The login provider's stable key (e.g. <c>"Google"</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>The provider-issued identifier for the user.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Human-readable provider name, when supplied by the backend.</summary>
    public string? DisplayName { get; init; }
}
