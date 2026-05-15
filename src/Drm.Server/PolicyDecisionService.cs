using Drm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed record ServerPolicyDecision(
    bool Allowed,
    Permission AllowedPermissions,
    string ReasonCode,
    string? WatermarkTemplate,
    DateTimeOffset? OfflineLeaseExpiresAtUtc,
    bool FileFound,
    bool InvalidPermission);

public sealed class PolicyDecisionService(AppDbContext dbContext)
{
    private static readonly TimeSpan DefaultOfflineLeaseDuration = TimeSpan.FromMinutes(15);

    public async Task<ServerPolicyDecision> DecideAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermissionText,
        CancellationToken cancellationToken)
    {
        if (!PermissionParser.TryParse(requestedPermissionText, out var requestedPermission))
        {
            return new ServerPolicyDecision(
                false,
                Permission.None,
                "invalid_permissions",
                null,
                null,
                FileFound: false,
                InvalidPermission: true);
        }

        var decisionTime = DateTimeOffset.UtcNow;
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = tenantId,
                FileId = fileId,
                UserId = userId,
                EventType = "access_denied",
                ReasonCode = "file_not_found",
                CreatedAtUtc = decisionTime
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return new ServerPolicyDecision(
                false,
                Permission.None,
                "file_not_found",
                null,
                null,
                FileFound: false,
                InvalidPermission: false);
        }

        var groupIds = await dbContext.GroupMembers
            .AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.UserId == userId)
            .Select(member => member.GroupId)
            .ToListAsync(cancellationToken);

        var grantRows = await dbContext.FileGrants
            .AsNoTracking()
            .Where(grant =>
                grant.TenantId == tenantId &&
                grant.FileId == fileId &&
                ((grant.SubjectType == GrantSubjectType.User.ToString() && grant.SubjectId == userId) ||
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
        if (!hasUserGrant && file.OwnerUserId == userId && file.Permissions != Permission.None)
        {
            effectivePermissions |= file.Permissions;
        }

        var grants = new List<FileGrant>();
        if (effectivePermissions != Permission.None)
        {
            grants.Add(new FileGrant(new UserId(userId), effectivePermissions));
        }

        var policy = new FilePolicy(
            new TenantId(file.TenantId),
            new ProtectedFileId(file.Id),
            file.ExpiresAtUtc,
            file.Revoked,
            grants,
            file.WatermarkTemplate);

        var decision = PolicyEvaluator.Evaluate(policy, new PolicyRequest(
            new TenantId(tenantId),
            new ProtectedFileId(fileId),
            new UserId(userId),
            new DeviceId(deviceId),
            requestedPermission,
            decisionTime));

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = decision.Allowed ? "access_allowed" : "access_denied",
            ReasonCode = decision.ReasonCode,
            CreatedAtUtc = decisionTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ServerPolicyDecision(
            decision.Allowed,
            decision.AllowedPermissions,
            decision.ReasonCode,
            decision.WatermarkTemplate,
            decision.Allowed ? decisionTime.Add(DefaultOfflineLeaseDuration) : null,
            FileFound: true,
            InvalidPermission: false);
    }
}
