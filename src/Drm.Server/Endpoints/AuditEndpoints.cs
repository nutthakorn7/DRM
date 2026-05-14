using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", GetAuditEventsAsync);

        return endpoints;
    }

    private static async Task<IReadOnlyList<AuditEventResponse>> GetAuditEventsAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.AuditEvents
            .Where(auditEvent => auditEvent.TenantId == tenantId)
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(100)
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

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);
}
