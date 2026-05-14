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

    private static async Task<Results<Ok<PolicyDecisionResponse>, NotFound<PolicyDecisionResponse>>> DecideAsync(
        DecidePolicyRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
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
                Permission.None,
                "file_not_found",
                null));
        }

        var policy = new FilePolicy(
            new TenantId(file.TenantId),
            new ProtectedFileId(file.Id),
            file.ExpiresAtUtc,
            file.Revoked,
            [new FileGrant(new UserId(file.OwnerUserId), file.Permissions)],
            file.WatermarkTemplate);

        var decision = PolicyEvaluator.Evaluate(policy, new PolicyRequest(
            new TenantId(request.TenantId),
            new ProtectedFileId(request.FileId),
            new UserId(request.UserId),
            new DeviceId(request.DeviceId),
            request.RequestedPermission,
            request.AtUtc));

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
            decision.AllowedPermissions,
            decision.ReasonCode,
            decision.WatermarkTemplate));
    }

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        Permission RequestedPermission,
        DateTimeOffset AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        Permission AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
