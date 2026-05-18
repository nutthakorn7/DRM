using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", GetAuditEventsAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<AuditEventResponse>>, BadRequest<ErrorResponse>>> GetAuditEventsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.MatchesHeader(tenantId))
        {
            return TypedResults.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        var rows = await dbContext.AuditEvents
            .Where(auditEvent => auditEvent.TenantId == tenantId)
            .OrderByDescending(auditEvent => auditEvent.Id)
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
        return TypedResults.Ok<IReadOnlyList<AuditEventResponse>>(rows);
    }

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
