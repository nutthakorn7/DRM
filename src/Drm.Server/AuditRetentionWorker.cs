using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Nightly background job that deletes audit events older than <see cref="AuditSettings.RetentionDays"/>.
/// Set RetentionDays to 0 to disable purging.
/// </summary>
public sealed class AuditRetentionWorker(
    IServiceScopeFactory scopeFactory,
    AuditSettings settings,
    ILogger<AuditRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.RetentionDays <= 0)
        {
            logger.LogInformation("Audit retention disabled (RetentionDays={Days})", settings.RetentionDays);
            return;
        }

        // Stagger startup so it doesn't compete with initial request handling
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PurgeAsync(stoppingToken);
            // Run once per day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = await db.AuditEvents
                .Where(e => e.CreatedAtUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted > 0)
                logger.LogInformation("Audit retention: purged {Count} events older than {Days}d", deleted, settings.RetentionDays);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Audit retention purge failed");
        }
    }
}

public sealed class AuditSettings
{
    /// <summary>Days to retain audit events. 0 = keep forever.</summary>
    public int RetentionDays { get; set; } = 0;
}
