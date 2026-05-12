using Microsoft.AspNetCore.Identity;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Drop-in <see cref="IdentityUser"/> subclass that carries a nullable
/// <see cref="TenantId"/> column and participates in the multi-tenancy
/// machinery. Consumers extend this instead of <see cref="IdentityUser"/>
/// when they opt into multi-tenancy.
/// </summary>
/// <remarks>
/// The column is added to <c>AspNetUsers</c> the moment EF Core sees this
/// type — no manual migration mapping needed in straightforward setups.
/// </remarks>
public abstract class MultiTenantIdentityUser : IdentityUser, IMultiTenantEntity
{
    /// <summary>Identifier of the tenant this user belongs to. Null for global users.</summary>
    public string? TenantId { get; set; }
}
