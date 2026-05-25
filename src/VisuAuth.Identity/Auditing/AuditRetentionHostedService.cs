using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Auditing;

/// <summary>
/// Background loop that prunes audit entries older than
/// <see cref="AuditLogOptions.RetentionDays"/>. Runs once on startup
/// (after a short warm-up delay so the app is fully serving) and then
/// on a daily cadence. Skipped entirely when retention is set to 0 or
/// negative — that's the "keep forever" mode.
/// </summary>
/// <remarks>
/// Uses <see cref="TimeProvider"/> for both the "now" boundary and the
/// inter-iteration delay so tests can drive the loop deterministically
/// with <c>FakeTimeProvider</c>.
/// </remarks>
internal sealed class AuditRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditLogOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditRetentionHostedService> logger) : BackgroundService
{
    /// <summary>How long to wait before the first sweep so the app's
    /// own startup doesn't compete with a delete.</summary>
    private static readonly TimeSpan WarmupDelay = TimeSpan.FromMinutes(1);

    /// <summary>Cadence between sweeps. 24h is dense enough to keep the
    /// table small without putting noticeable load on the DB.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory =
        scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly AuditLogOptions _options =
        (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<AuditRetentionHostedService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            // Explicit opt-out: keep entries forever. Exit cleanly so the
            // host doesn't keep an idle timer in memory.
            _logger.AuditRetentionDisabled(_options.RetentionDays);
            return;
        }

        try
        {
            await Task.Delay(WarmupDelay, _timeProvider, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepAsync(stoppingToken);
                await Task.Delay(SweepInterval, _timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown — nothing to log.
        }
    }

    /// <summary>
    /// Exposed as internal so unit tests can drive a single sweep without
    /// fighting the BackgroundService loop / FakeTimeProvider interaction
    /// (which is hard to drive deterministically).
    /// </summary>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IVisuAuthMetadataDbContext>();

            // ExecuteDeleteAsync skips the change tracker — important
            // because a sweep can touch thousands of rows.
            var deleted = await db.VisuAuthAuditLog
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
            {
                _logger.AuditRetentionSwept(deleted, cutoff);
            }
        }
#pragma warning disable CA1031 // Retention sweep is best-effort — log + retry on the next interval.
        catch (Exception ex)
        {
            _logger.AuditRetentionSweepFailed(ex);
        }
#pragma warning restore CA1031
    }
}
