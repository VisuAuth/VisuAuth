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
    /// Registers the admin UI Razor Pages and their services.
    /// </summary>
    public static IServiceCollection AddVisuAuthAdminUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Razor Pages registration and admin-specific services will be wired
        // here once pages are implemented. Pre-alpha placeholder.
        return services;
    }

    /// <summary>
    /// Maps the admin dashboard routes. Defaults to <c>/visuauth/admin</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthAdminUi(this IEndpointRouteBuilder endpoints, string prefix = "/visuauth/admin")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // endpoints.MapRazorPages() once pages exist. Pre-alpha placeholder.
        return endpoints;
    }
}
