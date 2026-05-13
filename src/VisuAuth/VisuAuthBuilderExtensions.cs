using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.EndUserUi.DependencyInjection;
using VisuAuth.Identity.DependencyInjection;

namespace VisuAuth;

/// <summary>
/// Single-call entry point for consumers that want to pull every VisuAuth
/// surface in (Identity adapter + admin UI + end-user UI). For finer control,
/// reference the individual <c>VisuAuth.*</c> packages instead.
/// </summary>
public static class VisuAuthBuilderExtensions
{
    /// <summary>
    /// Registers the full VisuAuth stack backed by the supplied Identity user
    /// and role types.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer (e.g. <c>ApplicationUser</c>).</typeparam>
    /// <typeparam name="TRole">The Identity role type used by the consumer (defaults to <see cref="IdentityRole"/>).</typeparam>
    public static IServiceCollection AddVisuAuth<TUser, TRole>(this IServiceCollection services)
        where TUser : IdentityUser
        where TRole : IdentityRole, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddVisuAuthIdentityAdapter<TUser, TRole>();
        services.AddVisuAuthAdminUi();
        services.AddVisuAuthEndUserUi();
        return services;
    }

    /// <summary>
    /// Registers the full VisuAuth stack backed by the supplied Identity user
    /// type and the default <see cref="IdentityRole"/>.
    /// </summary>
    public static IServiceCollection AddVisuAuth<TUser>(this IServiceCollection services)
        where TUser : IdentityUser
        => services.AddVisuAuth<TUser, IdentityRole>();

    /// <summary>
    /// Registers the full VisuAuth stack using the default <see cref="IdentityUser"/>
    /// and <see cref="IdentityRole"/>.
    /// </summary>
    public static IServiceCollection AddVisuAuth(this IServiceCollection services)
        => services.AddVisuAuth<IdentityUser, IdentityRole>();

    /// <summary>
    /// Maps both the admin dashboard (default prefix <c>/visuauth/admin</c>)
    /// and the end-user authentication pages (default prefix <c>/visuauth</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapVisuAuthEndUserUi();
        endpoints.MapVisuAuthAdminUi();
        return endpoints;
    }
}
