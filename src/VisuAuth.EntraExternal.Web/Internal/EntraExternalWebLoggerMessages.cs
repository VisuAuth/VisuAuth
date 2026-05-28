using Microsoft.Extensions.Logging;

namespace VisuAuth.EntraExternal.Web.Internal;

/// <summary>
/// LoggerMessage source-generated delegates for the Entra External Web
/// (OIDC sign-in) package. Centralised so the CA1848 analyzer is happy
/// and operators get a stable, searchable EventId set. The 72xx range is
/// allocated to this package — Workforce uses 70xx, EntraExternal (CRUD)
/// uses 71xx.
/// </summary>
internal static partial class EntraExternalWebLoggerMessages
{
    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Warning,
        Message = "Entra External profile sync failed for user {userId}: {message}")]
    public static partial void ProfileSyncFailed(
        this ILogger logger, Exception ex, string userId, string? message);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Warning,
        Message = "Entra External profile sync skipped unsupported Graph property '{graphProperty}' — not in the allow-list (givenName, surname, displayName, jobTitle, department, companyName, city, state, country, postalCode, streetAddress)")]
    public static partial void ProfileSyncUnknownProperty(this ILogger logger, string graphProperty);
}
