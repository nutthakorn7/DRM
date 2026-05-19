using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminAccessRequestEndpoints
{
    public static IEndpointRouteBuilder MapAdminAccessRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/access-requests",              ListAsync);
        endpoints.MapPatch("/api/admin/access-requests/{id:guid}/approve", ApproveAsync);
        endpoints.MapPatch("/api/admin/access-requests/{id:guid}/reject",  RejectAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid? tenantId,
        string? status,
        int? limit,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var parsedStatus = status is null
            ? (AccessRequestStatus?)null
            : Enum.TryParse<AccessRequestStatus>(status, ignoreCase: true, out var s) ? s : null;

        var all = await dbContext.FileAccessRequests
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        IEnumerable<FileAccessRequestEntity> q = all;
        if (tenantId.HasValue)
            q = q.Where(r => r.TenantId == tenantId.Value);
        if (parsedStatus.HasValue)
            q = q.Where(r => r.Status == parsedStatus.Value);

        var cap = Math.Min(limit ?? 100, 500);
        var rows = q.OrderByDescending(r => r.RequestedAtUtc).Take(cap)
            .Select(AccessRequestResponse.From).ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (result, _) = await ReviewAsync(id, AccessRequestStatus.Approved, httpContext, dbContext, cancellationToken);
        return result;
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (result, _) = await ReviewAsync(id, AccessRequestStatus.Rejected, httpContext, dbContext, cancellationToken);
        return result;
    }

    private static async Task<(IResult, FileAccessRequestEntity?)> ReviewAsync(
        Guid id,
        AccessRequestStatus newStatus,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return (fail!, null);

        var req = await dbContext.FileAccessRequests.FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (req is null) return (Results.NotFound(), null);
        if (req.Status != AccessRequestStatus.Pending)
            return (Results.BadRequest(new { reasonCode = "already_reviewed" }), null);

        var adminId = AdminIdentityContext.Current(httpContext)?.AdminUserId;
        req.Status = newStatus;
        req.ReviewedByAdminId = adminId;
        req.ReviewedAtUtc = DateTimeOffset.UtcNow;

        if (newStatus == AccessRequestStatus.Approved)
        {
            // Resolve requester email → tenant user ID so we can create a FileGrant
            var user = await dbContext.TenantUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == req.TenantId && u.Email == req.RequesterEmail,
                    cancellationToken);

            if (user is not null)
            {
                var alreadyGranted = await dbContext.FileGrants.AnyAsync(
                    g => g.TenantId == req.TenantId && g.FileId == req.FileId
                      && g.SubjectType == "user" && g.SubjectId == user.UserId,
                    cancellationToken);

                if (!alreadyGranted)
                {
                    dbContext.FileGrants.Add(new FileGrantEntity
                    {
                        TenantId = req.TenantId,
                        FileId = req.FileId,
                        SubjectType = "user",
                        SubjectId = user.UserId,
                        Permissions = "view",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (Results.Ok(AccessRequestResponse.From(req)), req);
    }

    private sealed record AccessRequestResponse(
        Guid RequestId,
        Guid TenantId,
        Guid FileId,
        string RequesterEmail,
        string Message,
        string Status,
        Guid? ReviewedByAdminId,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? ReviewedAtUtc)
    {
        public static AccessRequestResponse From(FileAccessRequestEntity r) => new(
            r.RequestId, r.TenantId, r.FileId, r.RequesterEmail, r.Message,
            r.Status.ToString().ToLowerInvariant(), r.ReviewedByAdminId,
            r.RequestedAtUtc, r.ReviewedAtUtc);
    }
}
