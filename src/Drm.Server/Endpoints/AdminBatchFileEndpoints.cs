using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminBatchFileEndpoints
{
    public static IEndpointRouteBuilder MapAdminBatchFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/files/batch-revoke", BatchRevokeAsync);
        endpoints.MapPost("/api/admin/files/batch-expiry", BatchExpiryAsync);
        return endpoints;
    }

    private static async Task<IResult> BatchRevokeAsync(
        BatchRevokeRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        ISiemDispatcher siemDispatcher,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        if (request.FileIds is null || request.FileIds.Count == 0)
            return Results.BadRequest(new { reasonCode = "no_file_ids" });

        var capped = request.FileIds.Distinct().Take(100).ToList();

        var files = await dbContext.ProtectedFiles
            .Where(f => f.TenantId == request.TenantId && capped.Contains(f.Id) && !f.Revoked)
            .ToListAsync(ct);

        var revokedSet = files.Select(f => f.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var auditEvents = new List<AuditEventEntity>();

        foreach (var file in files)
        {
            file.Revoked = true;
            var evt = new AuditEventEntity
            {
                TenantId = file.TenantId,
                FileId = file.Id,
                UserId = file.OwnerUserId,
                EventType = "file_revoked",
                ReasonCode = "batch_revoke",
                CreatedAtUtc = now
            };
            auditEvents.Add(evt);
            dbContext.AuditEvents.Add(evt);
        }

        await dbContext.SaveChangesAsync(ct);

        foreach (var evt in auditEvents)
            await siemDispatcher.DispatchAsync(evt, ct);

        var results = capped
            .Select(id => new BatchFileResult(id, revokedSet.Contains(id) ? "revoked" : "not_found"))
            .ToList();
        return Results.Ok(new { Results = results });
    }

    private static async Task<IResult> BatchExpiryAsync(
        BatchExpiryRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        if (request.FileIds is null || request.FileIds.Count == 0)
            return Results.BadRequest(new { reasonCode = "no_file_ids" });

        var capped = request.FileIds.Distinct().Take(100).ToList();

        var files = await dbContext.ProtectedFiles
            .Where(f => f.TenantId == request.TenantId && capped.Contains(f.Id))
            .ToListAsync(ct);

        var updatedSet = files.Select(f => f.Id).ToHashSet();
        foreach (var file in files)
            file.ExpiresAtUtc = request.ExpiresAtUtc;

        await dbContext.SaveChangesAsync(ct);

        var results = capped
            .Select(id => new BatchFileResult(id, updatedSet.Contains(id) ? "updated" : "not_found"))
            .ToList();
        return Results.Ok(new { Results = results });
    }

    private sealed record BatchRevokeRequest(Guid TenantId, List<Guid>? FileIds);
    private sealed record BatchExpiryRequest(Guid TenantId, List<Guid>? FileIds, DateTimeOffset ExpiresAtUtc);
    private sealed record BatchFileResult(Guid FileId, string Status);
}
