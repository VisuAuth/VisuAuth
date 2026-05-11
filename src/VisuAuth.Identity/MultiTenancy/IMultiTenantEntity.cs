namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Marker interface for entities that participate in multi-tenancy. The Identity
/// adapter applies a global EF Core query filter on <see cref="TenantId"/> and
/// auto-populates it on INSERT through a SaveChanges interceptor.
/// </summary>
public interface IMultiTenantEntity
{
    string? TenantId { get; set; }
}
