using Microsoft.Extensions.Logging;

namespace VisuAuth.EntraExternal.Internal;

/// <summary>
/// LoggerMessage source-generated delegates for the Entra External ID
/// adapter. Mirrors the Workforce adapter's structure (centralised so the
/// CA1848 analyzer is happy AND operators see a stable, searchable set of
/// EventIds in production logs). The 71xx range is allocated to this
/// adapter — Workforce uses 70xx.
/// </summary>
internal static partial class EntraExternalLoggerMessages
{
    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "Microsoft Graph users list failed: {message}")]
    public static partial void GraphListFailed(this ILogger logger, Exception ex, string? message);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "Failed to resolve roles for user {userId}: {message}")]
    public static partial void GraphRoleResolutionFailed(
        this ILogger logger, Exception ex, string userId, string? message);
}
