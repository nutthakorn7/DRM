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
    bool InvalidPermission,
    int? MaxOpens = null,
    int? OpensRemaining = null);

public sealed class PolicyDecisionService(AppDbContext dbContext, IAdminNotificationService notificationService)
{
    public async Task<ServerPolicyDecision> DecideAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermissionText,
        CancellationToken cancellationToken)
    {
        return await DecideInternalAsync(
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermissionText,
            writeAudit: true,
            cancellationToken);
    }

    public async Task<ServerPolicyDecision> SimulateAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermissionText,
        CancellationToken cancellationToken)
    {
        return await DecideInternalAsync(
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermissionText,
            writeAudit: false,
            cancellationToken);
    }

    private async Task<ServerPolicyDecision> DecideInternalAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermissionText,
        bool writeAudit,
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
            if (writeAudit)
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
                await notificationService.NotifyAsync(
                    tenantId,
                    new AdminNotificationEvent(
                        "access_denied",
                        fileId,
                        userId,
                        null,
                        decisionTime,
                        ReasonCode: "file_not_found"),
                    cancellationToken);
            }

            return new ServerPolicyDecision(
                false,
                Permission.None,
                "file_not_found",
                null,
                null,
                FileFound: false,
                InvalidPermission: false);
        }

        var deviceDisabled = await dbContext.AgentDevices
            .AsNoTracking()
            .AnyAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.DeviceId == deviceId &&
                candidate.DisabledAtUtc != null,
                cancellationToken);

        if (deviceDisabled)
        {
            if (writeAudit)
            {
                dbContext.AuditEvents.Add(new AuditEventEntity
                {
                    TenantId = tenantId,
                    FileId = fileId,
                    UserId = userId,
                    EventType = "access_denied",
                    ReasonCode = "device_disabled",
                    CreatedAtUtc = decisionTime
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                await notificationService.NotifyAsync(
                    tenantId,
                    new AdminNotificationEvent(
                        "access_denied",
                        fileId,
                        userId,
                        null,
                        decisionTime,
                        ReasonCode: "device_disabled"),
                    cancellationToken);
            }

            return new ServerPolicyDecision(
                false,
                Permission.None,
                "device_disabled",
                null,
                null,
                FileFound: true,
                InvalidPermission: false);
        }

        // v1.9: device trust enforcement
        if (deviceId != Guid.Empty)
        {
            var trustConfig = await dbContext.TenantDeviceTrustConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

            if (trustConfig is { Enabled: true })
            {
                // Materialize device — DateTimeOffset ORDER/WHERE on SQLite is unreliable
                var device = await dbContext.AgentDevices
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenantId && d.DeviceId == deviceId)
                    .FirstOrDefaultAsync(cancellationToken);

                var cutoff = decisionTime.AddDays(-trustConfig.RequiredCheckinDays);
                var lastCheckin = device?.LastHeartbeatAtUtc;

                if (lastCheckin == null || lastCheckin.Value < cutoff)
                {
                    if (writeAudit)
                    {
                        dbContext.AuditEvents.Add(new AuditEventEntity
                        {
                            TenantId = tenantId,
                            FileId = fileId,
                            UserId = userId,
                            EventType = "access_denied",
                            ReasonCode = "device_trust_expired",
                            CreatedAtUtc = decisionTime
                        });
                        await dbContext.SaveChangesAsync(cancellationToken);
                        await notificationService.NotifyAsync(
                            tenantId,
                            new AdminNotificationEvent(
                                "access_denied",
                                fileId,
                                userId,
                                null,
                                decisionTime,
                                ReasonCode: "device_trust_expired"),
                            cancellationToken);
                    }

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "device_trust_expired",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }
            }
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

        // v1.9: filter grants by time window in-memory (SQLite DateTimeOffset translation is unreliable)
        var activeGrants = grantRows.Where(g =>
            (g.ValidFromUtc == null || g.ValidFromUtc.Value <= decisionTime) &&
            (g.ValidUntilUtc == null || g.ValidUntilUtc.Value > decisionTime)
        ).ToList();

        var effectivePermissions = Permission.None;
        foreach (var grant in activeGrants)
        {
            if (PermissionParser.TryParse(grant.Permissions, out var grantPermissions))
            {
                effectivePermissions |= grantPermissions;
            }
        }

        var hasUserGrant = activeGrants.Any(grant => grant.SubjectType == GrantSubjectType.User.ToString());
        if (!hasUserGrant && file.OwnerUserId == userId && file.Permissions != Permission.None)
        {
            effectivePermissions |= file.Permissions;
        }

        var grants = new List<FileGrant>();
        if (effectivePermissions != Permission.None)
        {
            grants.Add(new FileGrant(new UserId(userId), effectivePermissions));
        }

        // Load the per-user access tally. If no row exists this is the user's
        // first attempt; treat as 0 opens used.
        var accessCount = await dbContext.FileAccessCounts
            .FirstOrDefaultAsync(
                row => row.TenantId == tenantId && row.FileId == fileId && row.UserId == userId,
                cancellationToken);
        var opensUsed = accessCount?.OpensUsed ?? 0;

        var policy = new FilePolicy(
            new TenantId(file.TenantId),
            new ProtectedFileId(file.Id),
            file.ExpiresAtUtc,
            file.Revoked,
            grants,
            file.WatermarkTemplate,
            MaxOpens: file.MaxOpens,
            OpensUsed: opensUsed);

        var decision = PolicyEvaluator.Evaluate(policy, new PolicyRequest(
            new TenantId(tenantId),
            new ProtectedFileId(fileId),
            new UserId(userId),
            new DeviceId(deviceId),
            requestedPermission,
            decisionTime));

        if (writeAudit)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = tenantId,
                FileId = fileId,
                UserId = userId,
                EventType = decision.Allowed ? "access_allowed" : "access_denied",
                ReasonCode = decision.ReasonCode,
                CreatedAtUtc = decisionTime
            });

            // Consume one open against MaxOpens on every successful access.
            // Simulation (writeAudit=false) MUST NOT mutate the counter,
            // otherwise the policy simulator silently exhausts real opens.
            if (decision.Allowed && file.MaxOpens.HasValue)
            {
                if (accessCount is null)
                {
                    dbContext.FileAccessCounts.Add(new FileAccessCountEntity
                    {
                        TenantId = tenantId,
                        FileId = fileId,
                        UserId = userId,
                        OpensUsed = 1,
                        FirstOpenedAtUtc = decisionTime,
                        LastOpenedAtUtc = decisionTime
                    });
                }
                else
                {
                    accessCount.OpensUsed += 1;
                    accessCount.LastOpenedAtUtc = decisionTime;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (!decision.Allowed)
            {
                await notificationService.NotifyAsync(
                    tenantId,
                    new AdminNotificationEvent(
                        "access_denied",
                        fileId,
                        userId,
                        null,
                        decisionTime,
                        ReasonCode: decision.ReasonCode),
                    cancellationToken);
            }
        }

        var offlineLeaseExpiresAtUtc = decision.Allowed && file.OfflineLeaseMinutes > 0
            ? decisionTime.AddMinutes(file.OfflineLeaseMinutes)
            : (DateTimeOffset?)null;

        return new ServerPolicyDecision(
            decision.Allowed,
            decision.AllowedPermissions,
            decision.ReasonCode,
            decision.WatermarkTemplate,
            offlineLeaseExpiresAtUtc,
            FileFound: true,
            InvalidPermission: false,
            MaxOpens: file.MaxOpens,
            OpensRemaining: decision.OpensRemaining);
    }
}
