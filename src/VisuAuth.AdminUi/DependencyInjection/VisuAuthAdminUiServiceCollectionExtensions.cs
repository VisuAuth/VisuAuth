using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
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
    /// Name of the authorization policy guarding every admin page under
    /// <c>/visuauth/admin</c>. Registered as "require an authenticated user"
    /// by default; register your own policy with this name (for example
    /// <c>RequireRole("Admin")</c>) to tighten it, or call
    /// <see cref="AllowAnonymousVisuAuthAdmin"/> to remove the gate when you
    /// front the dashboard with your own middleware.
    /// </summary>
    public const string AdminAuthorizationPolicy = "VisuAuth.Admin";

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

        // Theming layer 4 (per-tenant). Default to a no-op so single-tenant
        // deployments and consumers who never opt in pay nothing. TryAdd
        // means a consumer's own AddSingleton<ITenantThemeResolver, …>()
        // wins regardless of registration order.
        services.TryAddSingleton<ITenantThemeResolver, NoOpTenantThemeResolver>();
        // Companion to the layer-3 expander: a per-tenant override root
        // resolved at request time. Same TryAdd no-op default so the
        // single-tenant pipeline is unchanged.
        services.TryAddSingleton<ITenantViewOverrideResolver, NoOpTenantViewOverrideResolver>();

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
                // Secure the admin dashboard by default (CLAUDE.md §12): every
                // page under /visuauth/admin requires the AdminAuthorizationPolicy.
                // Guarded by the same first-registration check so a transitive
                // duplicate AddVisuAuthAdminUi() does not stack authorize filters.
                options.Conventions.AuthorizeFolder("/Admin", AdminAuthorizationPolicy);
            }
        });

        // Provide a safe default for the admin policy: an authenticated user.
        // PostConfigure runs after every Configure, so a consumer that defined
        // their own "VisuAuth.Admin" policy (e.g. RequireRole("Admin")) wins and
        // we leave it untouched. AllowAnonymousVisuAuthAdmin() overrides it back
        // to a permissive policy for consumers fronting the UI themselves.
        services.AddAuthorization();
        services.PostConfigure<AuthorizationOptions>(options =>
        {
            if (options.GetPolicy(AdminAuthorizationPolicy) is null)
            {
                options.AddPolicy(
                    AdminAuthorizationPolicy,
                    policy => policy.RequireAuthenticatedUser());
            }
        });

        // Catch "secured admin with nothing to sign in with" at startup rather
        // than as a 500 on the first admin request in production.
        // TryAddEnumerable so a duplicate AddVisuAuthAdminUi() doesn't stack it.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupFilter, VisuAuthAdminAuthenticationStartupCheck>());

        // <va-language-switcher /> needs the current request + an
        // antiforgery token; both come through IHttpContextAccessor.
        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Removes the default authorization gate on <c>/visuauth/admin</c> by
    /// registering the <see cref="AdminAuthorizationPolicy"/> as an
    /// always-allow policy. Use this only when the admin dashboard is already
    /// protected by other means (an upstream gateway, network isolation, or
    /// host-level middleware). Without this call — and without a consumer
    /// policy of the same name — the admin pages require an authenticated user.
    /// </summary>
    public static IServiceCollection AllowAnonymousVisuAuthAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.PostConfigure<AuthorizationOptions>(options =>
            options.AddPolicy(
                AdminAuthorizationPolicy,
                policy => policy.RequireAssertion(_ => true)));

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
