using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using My.Extensions.Localization.Json;

namespace VisuAuth.AdminUi.Localization;

/// <summary>
/// Wires VisuAuth's localization pipeline: JSON-backed
/// <c>IStringLocalizer</c> + ASP.NET Core's request-localization
/// middleware with three providers (query string, cookie,
/// Accept-Language).
/// </summary>
public static class VisuAuthLocalizationExtensions
{
    /// <summary>
    /// Registers JSON localization (<c>My.Extensions.Localization.Json</c>)
    /// rooted at <c>Resources/</c> and configures the request-localization
    /// pipeline. Idempotent — safe to call from both the meta-package
    /// and a consumer that opts in explicitly.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configure">Optional hook to widen the supported
    /// culture set, change the cookie name, etc.</param>
    public static IServiceCollection AddVisuAuthLocalization(
        this IServiceCollection services,
        Action<VisuAuthLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var bound = services.AddOptions<VisuAuthLocalizationOptions>();
        if (configure is not null)
        {
            bound.Configure(configure);
        }

        // JSON localization. TypeBased mode + ResourcesPath = "Resources"
        // produces:
        //   {bin}/Resources/AdminSharedResources.{culture}.json
        //   {bin}/Resources/EndUserSharedResources.{culture}.json
        services.AddJsonLocalization(options =>
        {
            options.ResourcesPath = ["Resources"];
            options.ResourcesType = ResourcesType.TypeBased;
        });

        // ASP.NET Core's default HtmlEncoder only leaves Basic-Latin (ASCII)
        // characters alone — everything else (including the chevrons in
        // "‹ Back" and the accents in "Usuários") gets serialised as
        // numeric character references like &#x2039;. Widening the safe
        // list to all Unicode keeps localized output readable in page
        // source (and lets integration tests grep for the literal text).
        // Single-byte-encoded ASCII control / unsafe chars stay encoded.
        services.AddSingleton<HtmlEncoder>(HtmlEncoder.Create(UnicodeRanges.All));

        // Pull the first SupportedCulture as the default and feed both
        // configs (default and supported list) to RequestLocalization.
        services
            .AddOptions<RequestLocalizationOptions>()
            .Configure<IOptions<VisuAuthLocalizationOptions>>((req, vau) =>
            {
                var settings = vau.Value;
                var supported = settings.SupportedCultures.ToList();
                if (supported.Count == 0)
                {
                    supported.Add(new CultureInfo("en"));
                }

                req.DefaultRequestCulture = new RequestCulture(supported[0]);
                req.SupportedCultures = supported;
                req.SupportedUICultures = supported;

                // Provider order matters: explicit query wins so a deep
                // link `?culture=pt-BR` always works, then the persisted
                // cookie, then the browser's Accept-Language header.
                req.RequestCultureProviders =
                [
                    new QueryStringRequestCultureProvider { QueryStringKey = settings.FormFieldName },
                    new CookieRequestCultureProvider { CookieName = settings.CookieName },
                    new AcceptLanguageHeaderRequestCultureProvider(),
                ];
            });

        return services;
    }

    /// <summary>
    /// Inserts <c>UseRequestLocalization</c> into the pipeline using the
    /// options configured by <see cref="AddVisuAuthLocalization"/>. Must
    /// be called before any localized response is rendered — i.e. before
    /// <c>UseRouting</c> / endpoint mappings that hit VisuAuth pages.
    /// </summary>
    public static IApplicationBuilder UseVisuAuthLocalization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseRequestLocalization();
    }
}
