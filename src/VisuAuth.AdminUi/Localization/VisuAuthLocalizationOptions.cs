using System.Globalization;

namespace VisuAuth.AdminUi.Localization;

/// <summary>
/// Knobs for VisuAuth's request-localization pipeline (CLAUDE.md §13 v0.1).
/// Consumers can extend the supported culture set, change the default
/// culture, or rename the persistence cookie.
/// </summary>
public sealed class VisuAuthLocalizationOptions
{
    /// <summary>
    /// Cookie name used by <c>CookieRequestCultureProvider</c> to persist
    /// the chosen culture across requests. Matches the ASP.NET Core
    /// default so existing apps with their own selector keep working.
    /// </summary>
    public string CookieName { get; set; } = ".AspNetCore.Culture";

    /// <summary>
    /// Form / query field consulted by the culture switch endpoint at
    /// <c>POST /visuauth/culture</c>.
    /// </summary>
    public string FormFieldName { get; set; } = "culture";

    /// <summary>
    /// Cultures the package ships translations for. The first entry
    /// becomes the default UI culture; the request-localization
    /// middleware falls back to it when no provider returns a match.
    /// Extra cultures added here MUST have matching
    /// <c>AdminSharedResources.{culture}.json</c> and
    /// <c>EndUserSharedResources.{culture}.json</c> files, or users will
    /// see English fallbacks.
    /// </summary>
    public IList<CultureInfo> SupportedCultures { get; } =
    [
        new CultureInfo("en"),
        new CultureInfo("pt-BR"),
    ];
}
