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
        var trustConfig = await dbContext.TenantDeviceTrustConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (trustConfig is { Enabled: true })
        {
            if (deviceId == Guid.Empty)
            {
                await WriteAccessDeniedAuditAsync(
                    tenantId,
                    fileId,
                    userId,
                    decisionTime,
                    "device_trust_required",
                    writeAudit,
                    cancellationToken);

                return new ServerPolicyDecision(
                    false,
                    Permission.None,
                    "device_trust_required",
                    null,
                    null,
                    FileFound: true,
                    InvalidPermission: false);
            }

            // Materialize device — DateTimeOffset ORDER/WHERE on SQLite is unreliable
            var device = await dbContext.AgentDevices
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.DeviceId == deviceId)
                .FirstOrDefaultAsync(cancellationToken);

            if (trustConfig.RequireDomainJoined)
            {
                if (device is null || !device.DomainJoined)
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "device_not_domain_joined",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "device_not_domain_joined",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }

                if (string.IsNullOrWhiteSpace(device.WindowsUser))
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "ad_user_not_detected",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "ad_user_not_detected",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }

                var allowedDomains = ParseAllowedAdDomains(trustConfig.AllowedAdDomainsCsv);
                if (allowedDomains.Count == 0)
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "allowed_ad_domains_required",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "allowed_ad_domains_required",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }

                var deviceDomain = NormalizeAdDomain(device.DomainName);
                if (!allowedDomains.Contains(deviceDomain))
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "ad_domain_not_allowed",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "ad_domain_not_allowed",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }

                if (!IsWindowsUserDomainAllowed(device.WindowsUser, deviceDomain, allowedDomains))
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "ad_user_not_allowed",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "ad_user_not_allowed",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }

                var tenantUser = await dbContext.TenantUsers
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate =>
                        candidate.TenantId == tenantId &&
                        candidate.UserId == userId,
                        cancellationToken);

                if (!IsWindowsUserMappedToTenantUser(device.WindowsUser, tenantUser))
                {
                    await WriteAccessDeniedAuditAsync(
                        tenantId,
                        fileId,
                        userId,
                        decisionTime,
                        "ad_user_mismatch",
                        writeAudit,
                        cancellationToken);

                    return new ServerPolicyDecision(
                        false,
                        Permission.None,
                        "ad_user_mismatch",
                        null,
                        null,
                        FileFound: true,
                        InvalidPermission: false);
                }
            }

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

    private async Task WriteAccessDeniedAuditAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        DateTimeOffset decisionTime,
        string reasonCode,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        if (!writeAudit)
        {
            return;
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = "access_denied",
            ReasonCode = reasonCode,
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
                ReasonCode: reasonCode),
            cancellationToken);
    }

    private static HashSet<string> ParseAllowedAdDomains(string? allowedAdDomainsCsv)
    {
        return allowedAdDomainsCsv?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeAdDomain)
            .Where(domain => domain.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    }

    private static string NormalizeAdDomain(string? domain)
    {
        return domain?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static bool IsWindowsUserDomainAllowed(
        string windowsUser,
        string deviceDomain,
        HashSet<string> allowedDomains)
    {
        var userDomain = ExtractWindowsUserDomain(windowsUser);
        if (string.IsNullOrWhiteSpace(userDomain))
        {
            return false;
        }

        return allowedDomains.Count > 0
            ? allowedDomains.Contains(userDomain)
            : false;
    }

    private static string ExtractWindowsUserDomain(string windowsUser)
    {
        var normalized = NormalizeAdDomain(windowsUser);
        var separatorIndex = normalized.IndexOf('\\', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return string.Empty;
        }

        return normalized[..separatorIndex];
    }

    private static bool IsWindowsUserMappedToTenantUser(string windowsUser, TenantUserEntity? tenantUser)
    {
        if (tenantUser is null || !tenantUser.Active)
        {
            return false;
        }

        var account = ExtractWindowsUserAccount(windowsUser);
        if (string.IsNullOrWhiteSpace(account))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tenantUser.ExternalId))
        {
            var externalId = tenantUser.ExternalId.Trim();
            if (string.Equals(externalId, windowsUser.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(externalId, account, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var email = tenantUser.Email.Trim();
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return false;
        }

        return string.Equals(email[..at], account, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractWindowsUserAccount(string windowsUser)
    {
        var trimmed = windowsUser.Trim();
        var separatorIndex = trimmed.IndexOf('\\', StringComparison.Ordinal);
        return separatorIndex >= 0 && separatorIndex < trimmed.Length - 1
            ? trimmed[(separatorIndex + 1)..].Trim()
            : trimmed;
    }
}
