namespace Drm.Server;

public static class AdminAudit
{
    public static AuditEventEntity SystemEvent(Guid tenantId, Guid? userId, string reasonCode)
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
            EventType = "system_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    public static AuditEventEntity PermissionEvent(Guid tenantId, Guid fileId, Guid? userId, string reasonCode)
        => new()
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = "permission_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
