using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit",     GetAuditEventsAsync);
        endpoints.MapGet("/api/audit.csv", GetAuditEventsCsvAsync);
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
        if (!httpContext.MatchesHeader(tenantId))
            return TypedResults.BadRequest(new ErrorResponse("tenant_mismatch"));

        var take = Math.Clamp(limit ?? 100, 1, 1000);
        var rows = await BuildQuery(dbContext, tenantId, eventType, from, to)
            .OrderByDescending(e => e.Id)
            .Take(take)
            .Select(e => new AuditEventResponse(e.Id, e.TenantId, e.FileId, e.UserId,
                e.EventType, e.ReasonCode, e.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<AuditEventResponse>>(rows);
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
        if (!httpContext.MatchesHeader(tenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var take = Math.Clamp(limit ?? 5000, 1, 50000);
        var rows = await BuildQuery(dbContext, tenantId, eventType, from, to)
            .OrderByDescending(e => e.Id)
            .Take(take)
            .Select(e => new AuditEventResponse(e.Id, e.TenantId, e.FileId, e.UserId,
                e.EventType, e.ReasonCode, e.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var csv = BuildCsv(rows);
        var filename = $"audit-{tenantId:N}-{DateTime.UtcNow:yyyyMMdd}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }

    private static IQueryable<AuditEventEntity> BuildQuery(
        AppDbContext dbContext, Guid tenantId, string? eventType, string? from, string? to)
    {
        var q = dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(eventType))
            q = q.Where(e => e.EventType == eventType);
        if (DateTimeOffset.TryParse(from?.Replace(' ', '+'), out var fromDt))
            q = q.Where(e => e.CreatedAtUtc >= fromDt);
        if (DateTimeOffset.TryParse(to?.Replace(' ', '+'), out var toDt))
            q = q.Where(e => e.CreatedAtUtc <= toDt);
        return q;
    }

    internal static string BuildCsv(IEnumerable<AuditEventResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("id,createdAtUtc,tenantId,fileId,userId,eventType,reasonCode\r\n");
        foreach (var e in rows)
        {
            sb.Append(e.Id).Append(',');
            sb.Append(CsvEscape(e.CreatedAtUtc.ToString("O"))).Append(',');
            sb.Append(e.TenantId).Append(',');
            sb.Append(CsvEscape(e.FileId?.ToString() ?? "")).Append(',');
            sb.Append(CsvEscape(e.UserId?.ToString() ?? "")).Append(',');
            sb.Append(CsvEscape(e.EventType)).Append(',');
            sb.Append(CsvEscape(e.ReasonCode)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvEscape(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\r') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : v;

    internal sealed record AuditEventResponse(
        long Id, Guid TenantId, Guid? FileId, Guid? UserId,
        string EventType, string ReasonCode, DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
