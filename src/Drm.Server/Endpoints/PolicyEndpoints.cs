using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/policy/decide", DecideAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<PolicyDecisionResponse>, NotFound<PolicyDecisionResponse>, BadRequest<PolicyDecisionResponse>>> DecideAsync(
        DecidePolicyRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!PermissionParser.TryParse(request.RequestedPermission, out var requestedPermission))
        {
            return TypedResults.BadRequest(new PolicyDecisionResponse(
                false,
                Permission.None.ToString(),
                "invalid_permissions",
                null));
        }

        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == request.FileId, cancellationToken);

        if (file is null)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = request.FileId,
                UserId = request.UserId,
                EventType = "access_denied",
                ReasonCode = "file_not_found",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return TypedResults.NotFound(new PolicyDecisionResponse(
                false,
                Permission.None.ToString(),
                "file_not_found",
                null));
        }

        var groupIds = await dbContext.GroupMembers
            .AsNoTracking()
            .Where(member => member.TenantId == request.TenantId && member.UserId == request.UserId)
            .Select(member => member.GroupId)
            .ToListAsync(cancellationToken);

        var grantRows = await dbContext.FileGrants
            .AsNoTracking()
            .Where(grant =>
                grant.TenantId == request.TenantId &&
                grant.FileId == request.FileId &&
                ((grant.SubjectType == GrantSubjectType.User.ToString() && grant.SubjectId == request.UserId) ||
                    (grant.SubjectType == GrantSubjectType.Group.ToString() && groupIds.Contains(grant.SubjectId))))
            .ToListAsync(cancellationToken);

        var effectivePermissions = Permission.None;
        foreach (var grant in grantRows)
        {
            if (PermissionParser.TryParse(grant.Permissions, out var grantPermissions))
            {
                effectivePermissions |= grantPermissions;
            }
        }

        var hasUserGrant = grantRows.Any(grant => grant.SubjectType == GrantSubjectType.User.ToString());
        if (!hasUserGrant && file.OwnerUserId == request.UserId && file.Permissions != Permission.None)
        {
            effectivePermissions |= file.Permissions;
        }

        var grants = new List<FileGrant>();
        if (effectivePermissions != Permission.None)
        {
            grants.Add(new FileGrant(new UserId(request.UserId), effectivePermissions));
        }

        var policy = new FilePolicy(
            new TenantId(file.TenantId),
            new ProtectedFileId(file.Id),
            file.ExpiresAtUtc,
            file.Revoked,
            grants,
            file.WatermarkTemplate);

        var decision = PolicyEvaluator.Evaluate(policy, new PolicyRequest(
            new TenantId(request.TenantId),
            new ProtectedFileId(request.FileId),
            new UserId(request.UserId),
            new DeviceId(request.DeviceId),
            requestedPermission,
            DateTimeOffset.UtcNow));

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = request.FileId,
            UserId = request.UserId,
            EventType = decision.Allowed ? "access_allowed" : "access_denied",
            ReasonCode = decision.ReasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new PolicyDecisionResponse(
            decision.Allowed,
            decision.AllowedPermissions.ToString(),
            decision.ReasonCode,
            decision.WatermarkTemplate));
    }

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DateTimeOffset? AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
