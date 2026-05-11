using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth.AdminUi.DependencyInjection;

/// <summary>
/// Registration and mapping helpers for the VisuAuth admin dashboard.
/// </summary>
public static class VisuAuthAdminUiServiceCollectionExtensions
{
    /// <summary>
    /// Marker type for assembly-level lookups (ApplicationParts, embedded resources).
    /// </summary>
    public static readonly Type AssemblyMarker = typeof(VisuAuthAdminUiServiceCollectionExtensions);

    /// <summary>
    /// Registers the admin UI Razor Pages and their services. Pages discovered
    /// from this assembly via <c>AddApplicationPart</c> become routable in the host.
    /// </summary>
    public static IServiceCollection AddVisuAuthAdminUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddRazorPages()
            .AddApplicationPart(AssemblyMarker.Assembly);

        return services;
    }

    /// <summary>
    /// Maps the Razor Pages declared by the admin UI library. Page routes are
    /// fixed by the <c>@page</c> directive (e.g. <c>/visuauth/admin/users</c>);
    /// this call simply hooks them into the host's endpoint pipeline.
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthAdminUi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapRazorPages();
        return endpoints;
    }
}
