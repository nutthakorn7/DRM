using Drm.Domain;

namespace Drm.Server;

public sealed class ProtectedFileEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool Revoked { get; set; }

    public Permission Permissions { get; set; }

    public string WatermarkTemplate { get; set; } = string.Empty;
}

public sealed class AuditEventEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? FileId { get; set; }

    public Guid? UserId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
