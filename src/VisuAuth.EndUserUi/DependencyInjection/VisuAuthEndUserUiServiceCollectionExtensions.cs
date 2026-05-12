using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth.EndUserUi.DependencyInjection;

/// <summary>
/// Registration and mapping helpers for the VisuAuth end-user pages
/// (public login, register, password reset, profile, sign-out).
/// </summary>
public static class VisuAuthEndUserUiServiceCollectionExtensions
{
    /// <summary>
    /// Marker type for assembly-level lookups (ApplicationParts, embedded resources).
    /// </summary>
    public static readonly Type AssemblyMarker = typeof(VisuAuthEndUserUiServiceCollectionExtensions);

    /// <summary>
    /// Registers the end-user UI Razor Pages. Pages from this assembly are
    /// picked up by the same <c>MapRazorPages()</c> call mounted by
    /// <c>AddVisuAuthAdminUi</c> (or by the meta-package's <c>MapVisuAuth</c>)
    /// so there is no second endpoint registration.
    /// </summary>
    public static IServiceCollection AddVisuAuthEndUserUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddRazorPages()
            .AddApplicationPart(AssemblyMarker.Assembly);

        return services;
    }

    /// <summary>
    /// Reserved for future mobile / JWT REST endpoints. Pages routed via
    /// Razor are mapped by <c>AddVisuAuthAdminUi.MapVisuAuthAdminUi</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthEndUserUi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/visuauth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        // No-op for now — the mobile/JWT REST routes will land here in
        // `feat/mobile-rest-api-and-jwt`. Razor pages from this assembly
        // are auto-picked-up by the existing MapRazorPages call.
        _ = prefix;
        return endpoints;
    }
}
