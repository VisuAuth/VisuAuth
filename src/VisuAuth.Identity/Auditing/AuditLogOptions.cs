namespace VisuAuth.Identity.Auditing;

/// <summary>
/// Options for the audit log plugin, set through the second-argument
/// lambda of <c>AddVisuAuthAuditLog</c>.
/// </summary>
public sealed class AuditLogOptions
{
    /// <summary>
    /// How many days entries are kept before the background retention
    /// service purges them. Default is 90 days — long enough for the
    /// typical compliance window, short enough that the table doesn't
    /// grow unbounded on long-lived deployments. Set to 0 (or negative)
    /// to disable retention entirely; the background service exits at
    /// startup in that mode.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
