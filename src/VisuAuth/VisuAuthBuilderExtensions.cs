using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.AdminUi.Localization;
using VisuAuth.EndUserUi.DependencyInjection;

namespace VisuAuth;

/// <summary>
/// Entry points for wiring VisuAuth into a host's <see cref="IServiceCollection"/>
/// and request pipeline. Offers two flavours:
///
/// <list type="bullet">
///   <item>
///     <description>
///     A fluent root, <see cref="AddVisuAuth(IServiceCollection)"/>, that
///     returns an <see cref="IVisuAuthBuilder"/> for explicit composition
///     (<c>.UseAspNetIdentity&lt;TUser&gt;().AddAdminUi().AddEndUserUi()</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     One-liner overloads <see cref="AddVisuAuth{TUser}(IServiceCollection)"/>
///     and <see cref="AddVisuAuth{TUser, TRole}(IServiceCollection)"/> that
///     register the full stack in a single call — the drop-in promise from
///     CLAUDE.md §2.1.
///     </description>
///   </item>
/// </list>
/// </summary>
public static class VisuAuthBuilderExtensions
{
    /// <summary>
    /// Begins fluent composition of the VisuAuth stack. The returned builder
    /// already has shared services (localization) registered; chain
    /// <c>UseAspNetIdentity</c>, <c>EnableMultiTenant</c>, <c>AddAdminUi</c>,
    /// and <c>AddEndUserUi</c> to opt into the rest.
    /// </summary>
    public static IVisuAuthBuilder AddVisuAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Localization must register before the UI packages so the JSON
        // factory wins over the default ResourceManager one that
        // AddViewLocalization() pins via TryAdd.
        services.AddVisuAuthLocalization();
        return new VisuAuthBuilder(services);
    }

    /// <summary>
    /// One-call install of the full VisuAuth stack using the supplied
    /// Identity user and role types — the drop-in form recommended for most
    /// consumers. Equivalent to
    /// <c>services.AddVisuAuth().UseAspNetIdentity&lt;TUser, TRole&gt;().AddAdminUi().AddEndUserUi()</c>.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    /// <typeparam name="TRole">The Identity role type used by the consumer.</typeparam>
    public static IServiceCollection AddVisuAuth<TUser, TRole>(this IServiceCollection services)
        where TUser : IdentityUser
        where TRole : IdentityRole, new()
    {
        services.AddVisuAuth()
            .UseAspNetIdentity<TUser, TRole>()
            .AddAdminUi()
            .AddEndUserUi();
        return services;
    }

    /// <summary>
    /// One-call install of the full VisuAuth stack with the default
    /// <see cref="IdentityRole"/>.
    /// </summary>
    public static IServiceCollection AddVisuAuth<TUser>(this IServiceCollection services)
        where TUser : IdentityUser
        => services.AddVisuAuth<TUser, IdentityRole>();

    /// <summary>
    /// Maps both the admin dashboard (default prefix <c>/visuauth/admin</c>)
    /// and the end-user authentication pages (default prefix <c>/visuauth</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapVisuAuthEndUserUi();
        endpoints.MapVisuAuthAdminUi();
        endpoints.MapVisuAuthCultureSwitch();
        return endpoints;
    }
}
