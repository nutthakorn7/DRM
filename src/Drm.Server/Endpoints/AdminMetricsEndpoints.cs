using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminMetricsEndpoints
{
    private static readonly int[] ValidPeriods = [7, 30, 90];

    public static IEndpointRouteBuilder MapAdminMetricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/metrics", GetMetricsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetMetricsAsync(
        int? period,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AuditRead, out var fail))
            return fail!;

        if (period.HasValue && !ValidPeriods.Contains(period.Value))
            return Results.BadRequest(new { reasonCode = "invalid_period", valid = ValidPeriods });
        var days = period ?? 30;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        // Tenant summary counts
        var tenantCount = await dbContext.Tenants.CountAsync(cancellationToken);
        var activeTenantCount = await dbContext.Tenants
            .CountAsync(t => t.Status == TenantStatus.Active, cancellationToken);
        var totalUsers = await dbContext.TenantUsers.CountAsync(cancellationToken);
        var totalFiles = await dbContext.ProtectedFiles.CountAsync(cancellationToken);

        // Materialize all events then filter client-side — SQLite silently ignores
        // DateTimeOffset comparisons in WHERE clauses
        var auditRows = (await dbContext.AuditEvents
            .AsNoTracking()
            .Select(e => new { e.EventType, e.CreatedAtUtc })
            .ToListAsync(cancellationToken))
            .Where(e => e.CreatedAtUtc >= cutoff)
            .ToList();

        var now = DateTimeOffset.UtcNow;

        // Build per-day buckets (index 0 = oldest)
        var buckets = new DayBucket[days];
        for (var i = 0; i < days; i++)
        {
            buckets[i] = new DayBucket(now.AddDays(-(days - 1 - i)).Date);
        }

        foreach (var row in auditRows)
        {
            var eventDate = row.CreatedAtUtc.UtcDateTime.Date;
            var idx = (int)(now.Date - eventDate).TotalDays;
            var bucketIdx = days - 1 - idx;
            if (bucketIdx < 0 || bucketIdx >= days) continue;

            buckets[bucketIdx].TotalEvents++;
            if (row.EventType == "file.protect" || row.EventType == "file.upload")
                buckets[bucketIdx].FilesProtected++;
            if (row.EventType == "file.decrypt" || row.EventType == "file.open")
                buckets[bucketIdx].Decryptions++;
            if (row.EventType == "user.create")
                buckets[bucketIdx].NewUsers++;
        }

        // Materialize registrations then filter client-side — SQLite DateTimeOffset WHERE workaround
        var allRegs = await dbContext.TenantRegistrations
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var newRegistrations = allRegs.Count(r => r.RequestedAtUtc >= cutoff);
        var approvedRegistrations = allRegs.Count(r =>
            r.Status == RegistrationStatus.Approved &&
            r.ReviewedAtUtc != null &&
            r.ReviewedAtUtc.Value >= cutoff);

        var result = new MetricsResponse(
            PeriodDays: days,
            Summary: new MetricsSummary(tenantCount, activeTenantCount, totalUsers, totalFiles,
                newRegistrations, approvedRegistrations),
            Buckets: buckets.Select(b => new MetricsBucket(
                b.Date.ToString("yyyy-MM-dd"),
                b.TotalEvents,
                b.FilesProtected,
                b.Decryptions,
                b.NewUsers)).ToArray());

        return Results.Ok(result);
    }

    private sealed class DayBucket(DateTime date)
    {
        public DateTime Date { get; } = date;
        public int TotalEvents { get; set; }
        public int FilesProtected { get; set; }
        public int Decryptions { get; set; }
        public int NewUsers { get; set; }
    }

    private sealed record MetricsResponse(
        int PeriodDays,
        MetricsSummary Summary,
        MetricsBucket[] Buckets);

    private sealed record MetricsSummary(
        int TotalTenants,
        int ActiveTenants,
        int TotalUsers,
        int TotalFiles,
        int NewRegistrations,
        int ApprovedRegistrations);

    private sealed record MetricsBucket(
        string Date,
        int TotalEvents,
        int FilesProtected,
        int Decryptions,
        int NewUsers);
}
