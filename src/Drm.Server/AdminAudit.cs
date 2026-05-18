namespace Drm.Server;

public static class AdminAudit
{
    public static Guid? ActorId(HttpContext? httpContext)
        => httpContext is not null ? AdminIdentityContext.Current(httpContext)?.AdminUserId : null;

    public static AuditEventEntity SystemEvent(Guid tenantId, Guid? userId, string reasonCode, HttpContext? httpContext = null)
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
            ActorAdminId = ActorId(httpContext),
            EventType = "system_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    public static AuditEventEntity PermissionEvent(Guid tenantId, Guid fileId, Guid? userId, string reasonCode, HttpContext? httpContext = null)
        => new()
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            ActorAdminId = ActorId(httpContext),
            EventType = "permission_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
