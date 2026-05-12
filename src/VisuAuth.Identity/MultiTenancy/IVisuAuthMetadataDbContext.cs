using Microsoft.EntityFrameworkCore;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Marker interface a consumer's DbContext satisfies (automatically when it
/// extends <see cref="MultiTenantIdentityDbContext{TUser}"/>) so VisuAuth's
/// tenant store can reach the <c>VisuAuthTenants</c> table without knowing
/// the concrete DbContext type.
/// </summary>
/// <remarks>
/// Register the consumer DbContext under this interface in DI:
/// <code>
/// services.AddScoped&lt;IVisuAuthMetadataDbContext&gt;(sp =&gt;
///     sp.GetRequiredService&lt;AppDbContext&gt;());
/// </code>
/// The <c>EnableVisuAuthTenancy&lt;TDbContext&gt;()</c> overload does this for
/// you.
/// </remarks>
public interface IVisuAuthMetadataDbContext
{
    DbSet<VisuAuthTenant> VisuAuthTenants { get; }

    /// <summary>Save pending changes for the metadata tables.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
