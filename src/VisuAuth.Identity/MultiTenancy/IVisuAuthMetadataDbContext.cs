using Microsoft.EntityFrameworkCore;
using VisuAuth.Identity.Auditing;

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

    /// <summary>
    /// External-provider credentials managed by the admin UI
    /// (<c>/visuauth/admin/external-providers</c>). The
    /// <c>EncryptedClientSecret</c> column carries DataProtection
    /// ciphertext — never read directly, go through the store.
    /// </summary>
    DbSet<VisuAuthExternalProviderConfig> VisuAuthExternalProviderConfigs { get; }

    /// <summary>
    /// Audit log entries persisted by the opt-in audit plugin. Always
    /// present in the schema so consumer migrations stay consistent across
    /// deployments; rows are only written when the consumer calls
    /// <c>AddVisuAuthAuditLog</c>. Indexed on Timestamp DESC for the admin
    /// page's default ordering.
    /// </summary>
    DbSet<VisuAuthAuditLogEntry> VisuAuthAuditLog { get; }

    /// <summary>Save pending changes for the metadata tables.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
