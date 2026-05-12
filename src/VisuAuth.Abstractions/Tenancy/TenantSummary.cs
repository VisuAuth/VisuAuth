namespace VisuAuth.Abstractions.Tenancy;

/// <summary>
/// Lightweight projection of a tenant for catalogue listings and the sidebar
/// switcher. Counts are filled by the adapter when feasible — backends without
/// a cheap member-count query may leave <see cref="MemberCount"/> at 0.
/// </summary>
public sealed record TenantSummary
{
    /// <summary>Stable identifier used as the routing key (URL slug, header, cookie).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable label, typically shown in the sidebar switcher.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Count of users currently assigned to this tenant.</summary>
    public int MemberCount { get; init; }

    /// <summary>Tenant creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
