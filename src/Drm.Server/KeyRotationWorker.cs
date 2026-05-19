using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class KeyRotationWorker(IServiceScopeFactory scopeFactory, ILogger<KeyRotationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "KeyRotationWorker error"); }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var all = await dbContext.TenantKeyRotationConfigs.AsNoTracking()
            .Where(c => c.Enabled)
            .ToListAsync(ct);

        var due = all.Where(c => c.NextRotationDueUtc.HasValue && c.NextRotationDueUtc.Value <= now).ToList();
        foreach (var config in due)
        {
            var count = await KeyRotationService.RotateTenantKeysAsync(dbContext, config.TenantId, "schedule", ct);
            logger.LogInformation("Rotated {Count} keys for tenant {TenantId}", count, config.TenantId);
        }
    }
}
