using Microsoft.Extensions.Logging;

namespace VisuAuth.Identity.Auditing;

/// <summary>
/// Source-generated <see cref="LoggerMessage"/> delegates for the audit
/// log plugin. Centralised here so adding a new log line is one entry
/// and the call sites stay free of <c>{Placeholder}</c> string-format
/// boilerplate. The generator emits a zero-alloc <c>IsEnabled</c> guard
/// for every message, satisfying both CA1848 and CA1873.
/// </summary>
internal static partial class AuditLoggerMessages
{
    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "Failed to persist audit event {Action} on {TargetType} {TargetId}")]
    internal static partial void AuditPersistFailed(
        this ILogger logger,
        Exception ex,
        string action,
        string targetType,
        string? targetId);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Information,
        Message = "Audit log retention is disabled (RetentionDays = {Days}); retention service is exiting")]
    internal static partial void AuditRetentionDisabled(this ILogger logger, int days);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Information,
        Message = "Audit log retention sweep deleted {Count} entries older than {Cutoff:O}")]
    internal static partial void AuditRetentionSwept(
        this ILogger logger,
        int count,
        DateTimeOffset cutoff);

    [LoggerMessage(
        EventId = 7004,
        Level = LogLevel.Warning,
        Message = "Audit log retention sweep failed; will retry on next interval")]
    internal static partial void AuditRetentionSweepFailed(this ILogger logger, Exception ex);
}
