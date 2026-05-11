namespace VisuAuth.Abstractions.Tenancy;

/// <summary>
/// Ambient tenant context for the current request, resolved by middleware
/// (subdomain, header, or JWT claim).
/// </summary>
public interface ITenantContext
{
    /// <summary>True when multi-tenancy is enabled in the host configuration.</summary>
    bool IsMultiTenancyEnabled { get; }

    /// <summary>Identifier of the current tenant, or <c>null</c> when not resolved.</summary>
    string? CurrentTenantId { get; }

    /// <summary>Human-readable display name of the current tenant, when available.</summary>
    string? CurrentTenantDisplayName { get; }
}
