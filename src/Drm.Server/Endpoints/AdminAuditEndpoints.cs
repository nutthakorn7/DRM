using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminAuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit",     GetAuditEventsAsync);
        endpoints.MapGet("/api/admin/audit.csv", GetAuditEventsCsvAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAuditEventsAsync(
        Guid tenantId,
        string? eventType,
        string? from,
        string? to,
        int? limit,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.AuditRead, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var take = Math.Clamp(limit ?? 500, 1, 5000);
        var rows = await BuildAndFilterAsync(dbContext, tenantId, eventType, from, to, take, cancellationToken);
        return Results.Ok<IReadOnlyList<AuditEventResponse>>(rows);
    }

    private static async Task<IResult> GetAuditEventsCsvAsync(
        Guid tenantId,
        string? eventType,
        string? from,
        string? to,
        int? limit,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.AuditExport, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var take = Math.Clamp(limit ?? 50000, 1, 500000);
        var rows = await BuildAndFilterAsync(dbContext, tenantId, eventType, from, to, take, cancellationToken);

        var csv = BuildCsv(rows);
        var filename = $"audit-{tenantId:N}-{DateTime.UtcNow:yyyyMMdd}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }

    private static async Task<List<AuditEventResponse>> BuildAndFilterAsync(
        AppDbContext dbContext, Guid tenantId, string? eventType,
        string? from, string? to, int take, CancellationToken ct)
    {
        var q = dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(eventType))
            q = q.Where(e => e.EventType == eventType);

        var all = await q
            .Select(e => new AuditEventResponse(e.Id, e.TenantId, e.FileId, e.UserId,
                e.EventType, e.ReasonCode, e.CreatedAtUtc))
            .ToListAsync(ct);

        IEnumerable<AuditEventResponse> filtered = all;
        // '+' in ISO 8601 offsets is decoded as space by query string parsers; restore it
        if (DateTimeOffset.TryParse(from?.Replace(' ', '+'), out var fromDt))
            filtered = filtered.Where(e => e.CreatedAtUtc >= fromDt);
        if (DateTimeOffset.TryParse(to?.Replace(' ', '+'), out var toDt))
            filtered = filtered.Where(e => e.CreatedAtUtc <= toDt);

        return filtered
            .OrderByDescending(e => e.Id)
            .Take(take)
            .ToList();
    }

    private static string BuildCsv(IEnumerable<AuditEventResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("createdAtUtc,tenantId,fileId,userId,eventType,reasonCode\r\n");
        foreach (var r in rows)
        {
            sb.Append(CsvEscape(r.CreatedAtUtc.ToString("O"))).Append(',');
            sb.Append(r.TenantId).Append(',');
            sb.Append(CsvEscape(r.FileId?.ToString() ?? "")).Append(',');
            sb.Append(CsvEscape(r.UserId?.ToString() ?? "")).Append(',');
            sb.Append(CsvEscape(r.EventType)).Append(',');
            sb.Append(CsvEscape(r.ReasonCode)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvEscape(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\r') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : v;

    private sealed record AuditEventResponse(
        long Id, Guid TenantId, Guid? FileId, Guid? UserId,
        string EventType, string ReasonCode, DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
