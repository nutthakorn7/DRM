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

    public int OfflineLeaseMinutes { get; set; } = 15;
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

public sealed class TenantUserEntity
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantGroupEntity
{
    public Guid TenantId { get; set; }

    public Guid GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class GroupMemberEntity
{
    public Guid TenantId { get; set; }

    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PolicyTemplateEntity
{
    public Guid TenantId { get; set; }

    public Guid TemplateId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Permissions { get; set; } = string.Empty;

    public string WatermarkTemplate { get; set; } = string.Empty;

    public int OfflineLeaseMinutes { get; set; }

    public bool AllowPrint { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WatermarkTemplateEntity
{
    public Guid TenantId { get; set; }

    public Guid WatermarkTemplateId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class FileGrantEntity
{
    public Guid TenantId { get; set; }

    public Guid FileId { get; set; }

    public string SubjectType { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public string Permissions { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ExternalShareLinkEntity
{
    public Guid TenantId { get; set; }

    public Guid ShareLinkId { get; set; }

    public Guid FileId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string GuestEmail { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public bool Revoked { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class ExternalShareVerificationEntity
{
    public Guid TenantId { get; set; }

    public Guid VerificationId { get; set; }

    public Guid ShareLinkId { get; set; }

    public string GuestEmail { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public string? SessionTokenHash { get; set; }

    public DateTimeOffset? SessionExpiresAtUtc { get; set; }
}

public sealed class SiemWebhookEntity
{
    public Guid TenantId { get; set; }

    public Guid WebhookId { get; set; }

    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AgentDeviceEntity
{
    public Guid TenantId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid UserId { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }

    public string? DisabledReason { get; set; }
}

public sealed class AgentCommandEntity
{
    public Guid TenantId { get; set; }

    public Guid CommandId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid FileId { get; set; }

    public string CommandType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class FileKeyEntity
{
    public Guid TenantId { get; set; }

    public Guid FileId { get; set; }

    public string WrappedKeyNonceBase64 { get; set; } = string.Empty;

    public string WrappedKeyCiphertextBase64 { get; set; } = string.Empty;

    public string WrappedKeyTagBase64 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
