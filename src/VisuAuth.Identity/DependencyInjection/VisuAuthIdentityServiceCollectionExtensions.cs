using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.MultiTenancy;
using VisuAuth.Identity.Roles;
using VisuAuth.Identity.Users;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the ASP.NET Core Identity adapter.
/// </summary>
public static class VisuAuthIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wires VisuAuth's user and role stores to ASP.NET Core Identity using the
    /// supplied user and role types. Call after <c>AddIdentity&lt;TUser, TRole&gt;()</c>
    /// (or the EF Core variants) so <c>UserManager</c> / <c>RoleManager</c> are
    /// resolvable.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    /// <typeparam name="TRole">The Identity role type used by the consumer.</typeparam>
    public static IServiceCollection AddVisuAuthIdentityAdapter<TUser, TRole>(this IServiceCollection services)
        where TUser : IdentityUser
        where TRole : IdentityRole, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        // Default tenant context — `EnableVisuAuthTenancy` replaces this with
        // the HTTP-aware implementation when the consumer opts into multi-tenancy.
        services.TryAddScoped<ITenantContext, NoOpTenantContext>();
        services.AddScoped<IUserStore, AspNetIdentityUserStore<TUser>>();
        services.AddScoped<IRoleStore, AspNetIdentityRoleStore<TUser, TRole>>();
        services.AddScoped<IAuthenticationFlow, AspNetIdentitySignInFlow<TUser>>();

        return services;
    }

    /// <summary>
    /// Backward-compatible overload that defaults the role type to
    /// <see cref="IdentityRole"/> — matches the most common consumer setup.
    /// </summary>
    public static IServiceCollection AddVisuAuthIdentityAdapter<TUser>(this IServiceCollection services)
        where TUser : IdentityUser
        => services.AddVisuAuthIdentityAdapter<TUser, IdentityRole>();
}
