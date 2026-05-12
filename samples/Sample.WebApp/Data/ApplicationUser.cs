using VisuAuth.Identity.MultiTenancy;

namespace Sample.WebApp.Data;

/// <summary>
/// Sample application's user entity. Extends <see cref="MultiTenantIdentityUser"/>
/// so the sample can demonstrate the per-tenant isolation flow. Single-tenant
/// consumers can extend <c>IdentityUser</c> directly instead.
/// </summary>
public sealed class ApplicationUser : MultiTenantIdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
