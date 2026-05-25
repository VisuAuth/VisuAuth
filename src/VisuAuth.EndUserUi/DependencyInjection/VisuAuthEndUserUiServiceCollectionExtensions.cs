using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.AdminUi.Theming;
using VisuAuth.EndUserUi.Api;
using VisuAuth.EndUserUi.Authentication;
using VisuAuth.EndUserUi.TwoFactor;

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
        // First-time strategy for external-login: defaults to AutoCreate so
        // a consumer who wires a provider (Google, Microsoft, …) without
        // touching ExternalLoginOptions still gets a working sign-in flow.
        services.AddOptions<ExternalLoginOptions>();

        // QR-code renderer used by the TOTP setup page. TryAddSingleton lets
        // a consumer swap the implementation (e.g. PNG output) without
        // having to suppress the default registration first.
        services.TryAddSingleton<IQrCodeSvgRenderer, QrCodeSvgRenderer>();

        // Sign-in flow collaborators (Option C refactor) — the emitter is
        // scoped because IAuditWriter is scoped. The two response mappers
        // are pure functions (`static Map(...)`) called directly via the
        // type — no DI registration needed for them. Consumed by both the
        // Razor login page and the minimal-API JWT login endpoint so the
        // audit shape + HTTP/page responses stay consistent across channels.
        services.TryAddScoped<SignInAuditEmitter>();

        // ASP.NET Identity defaults the cookie LoginPath to "/Account/Login"
        // — which 404s when the consumer relies on VisuAuth's pages instead.
        // PostConfigure (rather than Configure / ConfigureApplicationCookie)
        // because AddIdentity registers its own defaults later in the chain
        // when the consumer wires it after AddVisuAuth (the canonical sample
        // order); a regular Configure would be overwritten by those defaults.
        // Hardcoded scheme name ("Identity.Application") keeps EndUserUi free
        // of a direct Microsoft.AspNetCore.Identity package reference.
        services.PostConfigure<CookieAuthenticationOptions>("Identity.Application", options =>
        {
            options.LoginPath = "/visuauth/login";
            options.LogoutPath = "/visuauth/logout";
            options.AccessDeniedPath = "/visuauth/login";
        });

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
