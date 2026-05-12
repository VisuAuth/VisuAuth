namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// VisuAuth-owned metadata row representing a tenant. Stored in the
/// <c>VisuAuthTenants</c> table by the <see cref="MultiTenantIdentityDbContext{TUser}"/>.
/// </summary>
/// <remarks>
/// This is the only VisuAuth-owned table at v0.1. Uninstalling VisuAuth never
/// destroys consumer data — drop the table manually if you want to roll back.
/// </remarks>
public sealed class VisuAuthTenant
{
    /// <summary>Stable id used as the routing key (URL slug, header value, cookie value).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in the catalogue and sidebar switcher.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
