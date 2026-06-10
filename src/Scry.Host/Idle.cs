namespace Scry.Host;

/// <summary>Records the time of the last served RPC, for idle shutdown.</summary>
public sealed class ActivityTracker
{
    private long _lastTicks = DateTime.UtcNow.Ticks;

    public void Touch() => Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);

    public DateTime LastActivityUtc => new(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc);
}

/// <summary>
/// Stops the host after a configurable span with no RPC activity, so abandoned
/// hosts (and the dump file locks they hold) don't linger. A timeout of zero
/// disables it.
/// </summary>
public sealed class IdleShutdownService(
    TimeSpan idleTimeout,
    ActivityTracker activity,
    IHostApplicationLifetime lifetime,
    ILogger<IdleShutdownService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            logger.LogInformation("Idle shutdown disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var idle = DateTime.UtcNow - activity.LastActivityUtc;
                if (idle >= idleTimeout)
                {
                    logger.LogInformation("Idle for {Idle}; shutting down", idle);
                    lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
