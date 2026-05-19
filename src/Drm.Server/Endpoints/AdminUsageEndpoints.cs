using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminUsageEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/usage", GetUsageAsync);
        return endpoints;
    }

    private static async Task<IResult> GetUsageAsync(
        string? format,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var snapshot = DateTimeOffset.UtcNow;

        var tenants = await dbContext.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        if (tenants.Count == 0)
        {
            return format == "csv"
                ? BuildCsvResult([], snapshot)
                : Results.Ok(Array.Empty<TenantUsageRow>());
        }

        var tenantIds = tenants.Select(t => t.TenantId).ToList();

        var userCounts = await dbContext.TenantUsers
            .AsNoTracking()
            .Where(u => tenantIds.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var keyCounts = await dbContext.TenantClientKeys
            .AsNoTracking()
            .Where(k => tenantIds.Contains(k.TenantId) && !k.Revoked)
            .GroupBy(k => k.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var fileCounts = await dbContext.ProtectedFiles
            .AsNoTracking()
            .Where(f => tenantIds.Contains(f.TenantId))
            .GroupBy(f => f.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var rows = tenants.Select(t => new TenantUsageRow(
            t.TenantId,
            t.Name,
            t.DisplayName,
            t.Status,
            userCounts.GetValueOrDefault(t.TenantId, 0),
            t.MaxEncrypters,
            keyCounts.GetValueOrDefault(t.TenantId, 0),
            fileCounts.GetValueOrDefault(t.TenantId, 0),
            snapshot
        )).ToList();

        return format == "csv"
            ? BuildCsvResult(rows, snapshot)
            : Results.Ok(rows);
    }

    private static IResult BuildCsvResult(IEnumerable<TenantUsageRow> rows, DateTimeOffset snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TenantId,Name,DisplayName,Status,UsedSeats,MaxSeats,ActiveKeys,ProtectedFiles,SnapshotAtUtc");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                r.TenantId,
                CsvEscape(r.Name),
                CsvEscape(r.DisplayName),
                r.Status == TenantStatus.Active ? "Active" : "Suspended",
                r.UsedSeats,
                r.MaxSeats?.ToString() ?? "",
                r.ActiveKeys,
                r.ProtectedFiles,
                snapshot.ToString("O")
            ));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var filename = $"drm-usage-{snapshot:yyyyMMdd}.csv";
        return Results.File(bytes, "text/csv", filename);
    }

    private static string CsvEscape(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;

    internal sealed record TenantUsageRow(
        Guid TenantId,
        string Name,
        string DisplayName,
        TenantStatus Status,
        int UsedSeats,
        int? MaxSeats,
        int ActiveKeys,
        int ProtectedFiles,
        DateTimeOffset SnapshotAtUtc);
}
