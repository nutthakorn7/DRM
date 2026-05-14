using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminAuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit", GetAuditEventsAsync);
        endpoints.MapGet("/api/admin/audit.csv", GetAuditEventsCsvAsync);

        return endpoints;
    }

    private static async Task<IReadOnlyList<AuditEventResponse>> GetAuditEventsAsync(
        Guid tenantId,
        string? eventType,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await FilterAuditEvents(dbContext, tenantId, eventType)
            .OrderByDescending(auditEvent => auditEvent.Id)
            .Take(500)
            .Select(auditEvent => new AuditEventResponse(
                auditEvent.Id,
                auditEvent.TenantId,
                auditEvent.FileId,
                auditEvent.UserId,
                auditEvent.EventType,
                auditEvent.ReasonCode,
                auditEvent.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> GetAuditEventsCsvAsync(
        Guid tenantId,
        string? eventType,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var auditEvents = await FilterAuditEvents(dbContext, tenantId, eventType)
            .OrderByDescending(auditEvent => auditEvent.Id)
            .Take(5000)
            .Select(auditEvent => new AuditEventCsvRow(
                auditEvent.CreatedAtUtc,
                auditEvent.TenantId,
                auditEvent.FileId,
                auditEvent.UserId,
                auditEvent.EventType,
                auditEvent.ReasonCode))
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.Append("createdAtUtc,tenantId,fileId,userId,eventType,reasonCode\r\n");

        foreach (var auditEvent in auditEvents)
        {
            csv.Append(EscapeCsv(auditEvent.CreatedAtUtc.ToString("O")));
            csv.Append(',');
            csv.Append(EscapeCsv(auditEvent.TenantId.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(auditEvent.FileId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(auditEvent.UserId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(auditEvent.EventType));
            csv.Append(',');
            csv.Append(EscapeCsv(auditEvent.ReasonCode));
            csv.Append("\r\n");
        }

        return Results.Text(csv.ToString(), "text/csv");
    }

    private static IQueryable<AuditEventEntity> FilterAuditEvents(
        AppDbContext dbContext,
        Guid tenantId,
        string? eventType)
    {
        var query = dbContext.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(auditEvent => auditEvent.EventType == eventType);
        }

        return query;
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record AuditEventCsvRow(
        DateTimeOffset CreatedAtUtc,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode);
}
