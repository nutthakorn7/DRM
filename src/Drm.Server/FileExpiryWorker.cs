using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Runs every hour, marks expired non-revoked files as Revoked, writes audit events,
/// and fires the "file_expired" billing webhook event.
/// </summary>
public sealed class FileExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FileExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger startup by 3 min to avoid colliding with initial app boot
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireFilesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ExpireFilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTimeOffset.UtcNow;
            var expired = await db.ProtectedFiles
                .Where(f => !f.Revoked && f.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0) return;

            var auditEvents = new List<AuditEventEntity>(expired.Count);
            foreach (var file in expired)
            {
                file.Revoked = true;
                auditEvents.Add(new AuditEventEntity
                {
                    TenantId = file.TenantId,
                    FileId = file.Id,
                    UserId = file.OwnerUserId,
                    EventType = "file.expired",
                    ReasonCode = "ttl_expired",
                    CreatedAtUtc = now
                });
            }

            db.AuditEvents.AddRange(auditEvents);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("FileExpiryWorker: expired {Count} file(s)", expired.Count);

            // Fire billing webhook per tenant for expired files
            var byTenant = expired.GroupBy(f => f.TenantId);
            foreach (var group in byTenant)
            {
                BillingWebhooks.FireAndForget(scopeFactory, group.Key, "file_expired",
                    new { tenantId = group.Key, expiredCount = group.Count(), expiredAt = now });
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "FileExpiryWorker: error during expiry pass");
        }
    }
}
