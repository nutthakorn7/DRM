using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class DataRetentionWorker(IServiceScopeFactory scopeFactory, ILogger<DataRetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "DataRetentionWorker error"); }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var policies = await dbContext.TenantRetentionPolicies.AsNoTracking()
            .Where(p => p.Enabled)
            .ToListAsync(ct);

        var active = policies.Where(p => p.FileRetentionDays.HasValue).ToList();
        foreach (var policy in active)
        {
            var (deleted, _) = await DataRetentionService.ApplyTenantRetentionAsync(
                dbContext, policy.TenantId, policy.FileRetentionDays!.Value, false, ct);
            if (deleted > 0)
                logger.LogInformation("Data retention: deleted {Count} files for tenant {TenantId}",
                    deleted, policy.TenantId);
        }
    }
}
