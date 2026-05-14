using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace VisuAuth.AdminUi.Localization;

/// <summary>
/// Maps <c>POST /visuauth/culture</c> — the endpoint the sidebar /
/// end-user language selector posts to. Writes the
/// <c>CookieRequestCultureProvider</c> cookie with the requested
/// culture (when supported) and redirects to <c>returnUrl</c>.
/// </summary>
/// <remarks>
/// Same open-redirect posture as the tenant switcher: <c>returnUrl</c>
/// is only honoured when <see cref="HttpRequest.PathBase"/>-relative.
/// Anything pointing off-site falls back to <c>/</c>.
/// </remarks>
public static class CultureSwitchEndpoint
{
    /// <summary>
    /// Endpoint route. <c>MapVisuAuth</c> calls this; consumers shouldn't
    /// need to invoke it directly unless they're cherry-picking pieces.
    /// </summary>
    public const string Route = "/visuauth/culture";

    /// <summary>
    /// Adds <c>POST /visuauth/culture</c> to the endpoint pipeline.
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthCultureSwitch(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(Route, HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOptions<VisuAuthLocalizationOptions> vauOptions,
        IOptions<RequestLocalizationOptions> reqOptions)
    {
        var form = await httpContext.Request.ReadFormAsync();

        var settings = vauOptions.Value;
        var raw = form[settings.FormFieldName].ToString();
        var requested = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        // Validate against the configured allow-list so a crafted form
        // can't ask the browser to persist some arbitrary culture name.
        var allowed = reqOptions.Value.SupportedUICultures ?? [];
        var match = allowed.FirstOrDefault(c =>
            string.Equals(c.Name, requested, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            httpContext.Response.Cookies.Append(
                settings.CookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(match)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                });
        }
        else if (requested is null)
        {
            // Empty submission means "reset to default" → drop the cookie.
            httpContext.Response.Cookies.Delete(settings.CookieName);
        }
        // Unknown culture name? Silently ignore — same posture as the
        // tenant switcher when handed an unknown tenant id.

        var returnUrl = form["returnUrl"].ToString();
        if (string.IsNullOrEmpty(returnUrl) || !IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }

        return Results.LocalRedirect(returnUrl);
    }

    // Mirrors the open-redirect guard used by LoginModel and the tenant
    // switcher: only accept paths that start with '/' and not '//' or
    // '/\' (which would let a browser interpret them as scheme-relative).
    private static bool IsLocalUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }
        if (url[0] == '/')
        {
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        }
        return false;
    }
}
