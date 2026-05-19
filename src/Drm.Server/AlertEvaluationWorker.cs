using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Runs daily, evaluates enabled OperatorAlertRules against current DB state,
/// writes OperatorAlertsFired records, and optionally fires the "alert_fired" billing webhook.
/// </summary>
public sealed class AlertEvaluationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AlertEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger startup so initial DB seeding completes first
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await EvaluateAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rules = await db.OperatorAlertRules
                .AsNoTracking()
                .Where(r => r.Enabled)
                .ToListAsync(cancellationToken);

            if (rules.Count == 0) return;

            var now = DateTimeOffset.UtcNow;
            var fired = new List<OperatorAlertFiredEntity>();

            foreach (var rule in rules)
            {
                double actual = await ComputeValueAsync(db, rule, now, cancellationToken);
                if (actual > rule.Threshold)
                {
                    fired.Add(new OperatorAlertFiredEntity
                    {
                        RuleId = rule.RuleId,
                        TenantId = rule.TenantId,
                        RuleName = rule.Name,
                        ActualValue = actual,
                        Threshold = rule.Threshold,
                        FiredAtUtc = now
                    });
                }
            }

            if (fired.Count == 0) return;

            db.OperatorAlertsFired.AddRange(fired);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("AlertEvaluationWorker: {Count} alert(s) fired", fired.Count);

            foreach (var alert in fired.Where(a => rules.First(r => r.RuleId == a.RuleId).FireWebhook))
            {
                var tenantId = alert.TenantId ?? Guid.Empty;
                if (tenantId == Guid.Empty) continue;
                BillingWebhooks.FireAndForget(scopeFactory, tenantId, "alert_fired",
                    new { ruleId = alert.RuleId, ruleName = alert.RuleName, actualValue = alert.ActualValue, threshold = alert.Threshold, firedAt = now });
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AlertEvaluationWorker: error during evaluation pass");
        }
    }

    private static async Task<double> ComputeValueAsync(
        AppDbContext db,
        OperatorAlertRuleEntity rule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return rule.Condition switch
        {
            AlertCondition.SeatUsagePctAbove => await ComputeSeatUsagePctAsync(db, rule.TenantId, cancellationToken),
            AlertCondition.FilesProtectedAbove => await ComputeFilesProtectedAsync(db, rule.TenantId, cancellationToken),
            AlertCondition.TenantInactiveDays => await ComputeInactiveDaysAsync(db, rule.TenantId, now, cancellationToken),
            AlertCondition.NewRegistrationsAbove => await ComputeNewRegistrationsAsync(db, cancellationToken),
            _ => 0
        };
    }

    private static async Task<double> ComputeSeatUsagePctAsync(AppDbContext db, Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null) return 0;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant?.MaxEncrypters is null or 0) return 0;
        var used = await db.TenantUsers.CountAsync(u => u.TenantId == tenantId && u.Active, ct);
        return (double)used / tenant.MaxEncrypters.Value * 100.0;
    }

    private static async Task<double> ComputeFilesProtectedAsync(AppDbContext db, Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is not null)
            return await db.ProtectedFiles.CountAsync(f => f.TenantId == tenantId, ct);
        return await db.ProtectedFiles.CountAsync(ct);
    }

    private static async Task<double> ComputeInactiveDaysAsync(AppDbContext db, Guid? tenantId, DateTimeOffset now, CancellationToken ct)
    {
        if (tenantId is null) return 0;
        var cutoffTicks = now.AddDays(-365).Ticks;
        var lastEvent = await db.AuditEvents
            .Where(e => e.TenantId == tenantId && e.CreatedAtUtc.Ticks >= cutoffTicks)
            .OrderByDescending(e => e.CreatedAtUtc.Ticks)
            .FirstOrDefaultAsync(ct);
        if (lastEvent is null) return 365;
        return (now - lastEvent.CreatedAtUtc).TotalDays;
    }

    private static async Task<double> ComputeNewRegistrationsAsync(AppDbContext db, CancellationToken ct)
        => await db.TenantRegistrations.CountAsync(
            r => r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Verified, ct);
}
