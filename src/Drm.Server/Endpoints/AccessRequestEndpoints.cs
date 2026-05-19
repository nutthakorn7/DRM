using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AccessRequestEndpoints
{
    public static IEndpointRouteBuilder MapAccessRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/files/{fileId:guid}/request-access", RequestAccessAsync);
        return endpoints;
    }

    private static async Task<IResult> RequestAccessAsync(
        Guid fileId,
        Guid tenantId,
        AccessRequestBody body,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.MatchesHeader(tenantId))
            return Results.BadRequest(new { reasonCode = "tenant_mismatch" });

        if (string.IsNullOrWhiteSpace(body.RequesterEmail))
            return Results.BadRequest(new { reasonCode = "email_required" });

        var file = await dbContext.ProtectedFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == fileId, cancellationToken);
        if (file is null || file.Revoked)
            return Results.NotFound();

        // Deduplicate — one pending request per file+email
        var existing = await dbContext.FileAccessRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.FileId == fileId
                && r.RequesterEmail == body.RequesterEmail.Trim().ToLowerInvariant()
                && r.Status == AccessRequestStatus.Pending, cancellationToken);
        if (existing is not null)
            return Results.Conflict(new { reasonCode = "request_already_pending", requestId = existing.RequestId });

        var req = new FileAccessRequestEntity
        {
            RequestId = Guid.NewGuid(),
            TenantId = tenantId,
            FileId = fileId,
            RequesterEmail = body.RequesterEmail.Trim().ToLowerInvariant(),
            Message = body.Message?.Trim() ?? string.Empty,
            Status = AccessRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.FileAccessRequests.Add(req);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/files/{fileId}/request-access/{req.RequestId}",
            new { requestId = req.RequestId });
    }

    private sealed record AccessRequestBody(string? RequesterEmail, string? Message);
}
