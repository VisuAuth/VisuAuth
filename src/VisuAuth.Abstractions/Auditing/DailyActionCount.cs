namespace VisuAuth.Abstractions.Auditing;

/// <summary>
/// One bucket in a day-by-day rollup of a single action code. Produced by
/// <see cref="IAuditReader.CountByDayAsync"/> for charting on the admin
/// dashboard ("logins per day for the last 7 days", etc.).
/// </summary>
/// <param name="Day">
/// Calendar day in UTC. The reader groups by the date portion of
/// <c>Timestamp.UtcDateTime</c> so callers in any time zone see a stable
/// rollup that doesn't shift around midnight local time.
/// </param>
/// <param name="Count">Number of audit entries whose timestamp landed on <paramref name="Day"/>.</param>
public sealed record DailyActionCount(DateOnly Day, int Count);
