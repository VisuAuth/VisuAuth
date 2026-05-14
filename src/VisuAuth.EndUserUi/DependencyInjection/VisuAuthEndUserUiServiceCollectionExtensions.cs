using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.AdminUi.Theming;
using VisuAuth.EndUserUi.Api;

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

        // Default options instances so consumers that never call Configure
        // still resolve non-null `IOptions<...>` for the LoginModel chain.
        services.AddOptions<EndUserUiOptions>();
        services.AddOptions<WebViewCallbackOptions>();

        // Theming layer 3 — let consumers override end-user pages with
        // their own Razor Page at the same @page route. The expander +
        // options are wired by AddVisuAuthAdminUi (which AddVisuAuth calls
        // before this method); we only contribute the demotion convention
        // for THIS assembly's pages so consumer routes win.
        services.Configure<RazorPagesOptions>(options =>
        {
            if (!options.Conventions.OfType<DemoteVisuAuthPagesConvention>()
                    .Any(c => c.OwnsAssembly(AssemblyMarker.Assembly)))
            {
                options.Conventions.Add(new DemoteVisuAuthPagesConvention(AssemblyMarker.Assembly));
            }
        });

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

        // Razor pages auto-picked-up by the existing MapRazorPages call from
        // AdminUi. The mobile / native REST endpoints under `/api/auth` need
        // explicit registration via minimal APIs.
        endpoints.MapVisuAuthAuthApi($"{prefix.TrimEnd('/')}/api/auth");
        return endpoints;
    }
}
