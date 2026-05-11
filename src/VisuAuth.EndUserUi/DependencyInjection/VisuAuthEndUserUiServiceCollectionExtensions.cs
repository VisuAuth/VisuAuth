using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth.EndUserUi.DependencyInjection;

public static class VisuAuthEndUserUiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the end-user UI Razor Pages and JWT issuance services.
    /// </summary>
    public static IServiceCollection AddVisuAuthEndUserUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Razor Pages + JWT token service + theme provider will be wired here
        // once implemented. Pre-alpha placeholder.
        return services;
    }

    /// <summary>
    /// Maps the end-user authentication pages and the mobile REST API.
    /// Defaults to <c>/visuauth</c> for pages and <c>/visuauth/api</c> for the API.
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthEndUserUi(this IEndpointRouteBuilder endpoints, string prefix = "/visuauth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // endpoints.MapRazorPages() + endpoints.MapGroup($"{prefix}/api/auth")... once pages exist.
        return endpoints;
    }
}
