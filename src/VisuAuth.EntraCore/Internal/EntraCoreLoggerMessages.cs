using Microsoft.Extensions.Logging;

namespace VisuAuth.EntraCore.Internal;

/// <summary>
/// LoggerMessage source-generated delegates for VisuAuth.EntraCore.
/// Centralised for the CA1848 analyzer + a stable EventId set. 73xx range
/// is EntraCore's (70xx Workforce, 71xx EntraExternal CRUD, 72xx
/// EntraExternal.Web).
/// </summary>
internal static partial class EntraCoreLoggerMessages
{
    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Warning,
        Message = "Entra sign-in audit query failed: {message}. The app likely lacks AuditLog.Read.All or the tenant lacks an Entra ID P1 licence — surfacing an empty audit view.")]
    public static partial void EntraAuditQueryFailed(this ILogger logger, Exception ex, string? message);
}
