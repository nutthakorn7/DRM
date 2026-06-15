using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Nightly background job that DEPERSONALIZES audit events older than
/// <see cref="AuditSettings.RetentionDays"/> (nulls the personal fields, keeps the
/// row + hash-chain entry — see <see cref="AuditRetentionService"/>).
/// Set RetentionDays to 0 to disable.
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
            // Depersonalize, don't delete: removes the personal data (PDPA) while
            // keeping the row + its chain entry so /audit/chain/verify still passes.
            // Deleting events here broke chain verification on every run.
            var scrubbed = await AuditRetentionService.DepersonalizeAsync(db, cutoff, cancellationToken);
            if (scrubbed > 0)
                logger.LogInformation("Audit retention: depersonalized {Count} events older than {Days}d (chain preserved)", scrubbed, settings.RetentionDays);
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
