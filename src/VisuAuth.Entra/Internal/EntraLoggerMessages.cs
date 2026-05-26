using Microsoft.Extensions.Logging;

namespace VisuAuth.Entra.Internal;

/// <summary>
/// LoggerMessage source-generated delegates for the Entra adapter.
/// Centralised so a) the analyzer (CA1848) is happy and b) operators
/// see a stable, searchable set of EventIds in the production logs.
/// </summary>
internal static partial class EntraLoggerMessages
{
    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "Microsoft Graph users list failed: {message}")]
    public static partial void GraphListFailed(this ILogger logger, Exception ex, string? message);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "Failed to resolve roles for user {userId}: {message}")]
    public static partial void GraphRoleResolutionFailed(
        this ILogger logger, Exception ex, string userId, string? message);
}
