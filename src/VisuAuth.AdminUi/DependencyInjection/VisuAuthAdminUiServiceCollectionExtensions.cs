using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VisuAuth.AdminUi.Theming;

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
            .AddApplicationPart(AssemblyMarker.Assembly)
            // ViewLocalization unlocks `IHtmlLocalizer<T>` and the
            // `@inject IViewLocalizer Loc` pattern in Razor views; the
            // backing IStringLocalizer comes from AddVisuAuthLocalization.
            .AddViewLocalization()
            .AddDataAnnotationsLocalization();

        // Ensure IOptions<VisuAuthTheme> resolves to an empty bag when the
        // consumer skips Configure<VisuAuthTheme>(…) — the layout's
        // <va-theme-style /> tag helper then suppresses itself.
        services.AddOptions<VisuAuthTheme>();

        // Theming layer 3 (CLAUDE.md §8.4) — view + page overrides.
        // Default root /Views/VisuAuth applies until the consumer calls
        // Configure<VisuAuthViewOverrideOptions>(...).
        services.AddOptions<VisuAuthViewOverrideOptions>();
        // View-location expander wires up via IConfigureOptions<RazorViewEngineOptions>.
        // TryAddEnumerable: calling AddVisuAuthAdminUi twice (e.g. through a
        // transitive registration chain) must not stack two expanders.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<RazorViewEngineOptions>, VisuAuthViewLocationConfigure>());
        // Page-demotion convention has to live inside RazorPagesOptions.Conventions
        // because CompiledPageRouteModelProvider reads that exact list — registering
        // an IPageRouteModelConvention in plain DI is silently ignored. The OwnsAssembly
        // guard makes the call idempotent across duplicate AddVisuAuthAdminUi() invocations.
        services.Configure<RazorPagesOptions>(options =>
        {
            if (!options.Conventions.OfType<DemoteVisuAuthPagesConvention>()
                    .Any(c => c.OwnsAssembly(AssemblyMarker.Assembly)))
            {
                options.Conventions.Add(new DemoteVisuAuthPagesConvention(AssemblyMarker.Assembly));
            }
        });

        // <va-language-switcher /> needs the current request + an
        // antiforgery token; both come through IHttpContextAccessor.
        services.AddHttpContextAccessor();

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
