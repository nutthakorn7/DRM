using Drm.Domain;

namespace Drm.Server;

public enum TenantStatus { Active = 0, Suspended = 1 }

public sealed class TenantClientKeyEntity
{
    public Guid TenantId { get; set; }

    public Guid KeyId { get; set; }

    public string KeyHash { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public bool Revoked { get; set; }
}

public enum RegistrationStatus { Pending = 0, Verified = 1, Approved = 2, Rejected = 3 }

public sealed class TenantRegistrationEntity
{
    public Guid RegistrationId { get; set; }

    public string TenantName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string AdminEmail { get; set; } = string.Empty;

    public string AdminDisplayName { get; set; } = string.Empty;

    public int? MaxEncrypters { get; set; }

    public RegistrationStatus Status { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset TokenExpiresAtUtc { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public string? ReviewNotes { get; set; }

    public Guid? CreatedTenantId { get; set; }

    public Guid? CreatedUserId { get; set; }
}

public sealed class TenantBillingWebhookEntity
{
    public Guid TenantId { get; set; }

    public Guid WebhookId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string Events { get; set; } = "*";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantEntity
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public TenantStatus Status { get; set; }

    public int? MaxEncrypters { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? SuspendedAtUtc { get; set; }
}

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

    /// <summary>
    /// Maximum number of times each user may open this file. Null means
    /// unlimited (the historical behaviour for files created before v1.4.0).
    /// </summary>
    public int? MaxOpens { get; set; }
}

/// <summary>
/// Per-(file, user) tally of how many times that user has consumed an open
/// against <see cref="ProtectedFileEntity.MaxOpens"/>. One row per pair, created
/// lazily on the user's first access. Increment happens after a successful
/// policy decision, before the response is returned to the viewer.
/// </summary>
public sealed class FileAccessCountEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public Guid UserId { get; set; }
    public int OpensUsed { get; set; }
    public DateTimeOffset FirstOpenedAtUtc { get; set; }
    public DateTimeOffset LastOpenedAtUtc { get; set; }
}

public sealed class AuditEventEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? FileId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? ActorAdminId { get; set; }

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

    public string? ExternalId { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantGroupEntity
{
    public Guid TenantId { get; set; }

    public Guid GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ExternalId { get; set; }

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

    /// <summary>
    /// Per-user open limit baked into the template. When this template is
    /// applied to a file, the value is copied onto
    /// <see cref="ProtectedFileEntity.MaxOpens"/>. Null means unlimited.
    /// </summary>
    public int? MaxOpens { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WatermarkTemplateEntity
{
    public Guid TenantId { get; set; }

    public Guid WatermarkTemplateId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public int OpacityPercent { get; set; } = 33;

    public int DensityTiles { get; set; } = 4;

    public int DiagonalAngleDegrees { get; set; } = -28;

    public bool IncludeUserId { get; set; } = true;

    public bool IncludeTimestamp { get; set; } = true;

    public bool IncludeIpAddress { get; set; }

    public bool IncludeSessionId { get; set; }

    public bool RollingEnabled { get; set; }

    public bool PrintWatermarkEnabled { get; set; }

    public string PrintWatermarkPattern { get; set; } = string.Empty;

    public int PrintWatermarkOpacityPercent { get; set; } = 33;

    public string PrintWatermarkPosition { get; set; } = "diagonal";

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

    /// <summary>Optional: grant is only active on or after this UTC timestamp.</summary>
    public DateTimeOffset? ValidFromUtc { get; set; }

    /// <summary>Optional: grant expires at this UTC timestamp.</summary>
    public DateTimeOffset? ValidUntilUtc { get; set; }
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

    public DateTimeOffset? ViewerOpenedAtUtc { get; set; }
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

public sealed class TenantExternalShareSettingsEntity
{
    public Guid TenantId { get; set; }

    public bool ExternalSharingEnabled { get; set; } = true;

    public string? AllowedGuestEmailDomainsCsv { get; set; }

    public string? BlockedGuestEmailsCsv { get; set; }

    public int? MaxShareLinkLifetimeHours { get; set; }

    public int? MaxShareLinkMaxUses { get; set; }

    public int? MaxActiveShareLinksPerFile { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid UpdatedByUserId { get; set; }
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

public sealed class TenantDirectorySyncConfigEntity
{
    public Guid TenantId { get; set; }

    public string EntraTenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string? LastSyncStatus { get; set; }

    public DateTimeOffset? LastSyncAtUtc { get; set; }

    public int? LastSyncUserCount { get; set; }

    public int? LastSyncGroupCount { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class TenantBoxIntegrationConfigEntity
{
    public Guid TenantId { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string EnterpriseId { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string? LastConnectionStatus { get; set; }

    public DateTimeOffset? LastConnectionAtUtc { get; set; }

    public int LastWebhookEventCount { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class BoxWebhookEventEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string SourceItemId { get; set; } = string.Empty;

    public string SourceItemName { get; set; } = string.Empty;

    public string? CreatedByEmail { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class TenantFolderWatcherConfigEntity
{
    public Guid TenantId { get; set; }

    public string WatchedFoldersJson { get; set; } = "[]";

    public bool Enabled { get; set; }

    public string? LastReportStatus { get; set; }

    public DateTimeOffset? LastReportAtUtc { get; set; }

    public int LastFilesProtected { get; set; }

    public string? Hostname { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class FolderWatcherEventEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string Status { get; set; } = string.Empty;

    public Guid? FileId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class SecureContainerEntity
{
    public Guid TenantId { get; set; }

    public Guid ContainerId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    public Guid? PolicyTemplateId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class SecureContainerFileEntity
{
    public Guid TenantId { get; set; }

    public Guid ContainerId { get; set; }

    public int OrdinalIndex { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }
}

public sealed class TransparentProtectedFileEntity
{
    public Guid TenantId { get; set; }

    public Guid FileId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public Guid? PolicyTemplateId { get; set; }

    public DateTimeOffset RegisteredAtUtc { get; set; }
}

public sealed class TenantUserPersonaEntity
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Persona { get; set; } = "Employee";

    public DateTimeOffset AssignedAtUtc { get; set; }
}

public sealed class FileTagEntity
{
    public Guid TenantId { get; set; }

    public Guid FileId { get; set; }

    public string Tag { get; set; } = string.Empty;

    public DateTimeOffset AssignedAtUtc { get; set; }
}

public sealed class TenantOutlookIntegrationConfigEntity
{
    public Guid TenantId { get; set; }

    public bool Enabled { get; set; }

    public bool AutoEncryptOutgoingAttachments { get; set; } = true;

    public int MinAttachmentSizeKb { get; set; }

    public string SkipDomainsCsv { get; set; } = string.Empty;

    public string? DefaultPolicyTemplateId { get; set; }

    public int LifetimeProtectedCount { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class OutlookAttachmentEventEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public string SenderEmail { get; set; } = string.Empty;

    public string RecipientCsv { get; set; } = string.Empty;

    public string AttachmentName { get; set; } = string.Empty;

    public long AttachmentSizeBytes { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ProtectedFileId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class TenantAdminNotificationConfigEntity
{
    public Guid TenantId { get; set; }

    public string AdminEmailsCsv { get; set; } = string.Empty;

    public bool NotifyOnExternalShareViewed { get; set; }

    public bool NotifyOnFileRevoked { get; set; }

    public bool NotifyOnAccessDenied { get; set; }

    public bool NotifyOnShareLinkCreated { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Alert condition types for operator alert rules.</summary>
public enum AlertCondition
{
    SeatUsagePctAbove = 0,   // usedSeats / maxSeats * 100 > threshold
    FilesProtectedAbove = 1,  // total protected files for tenant > threshold
    TenantInactiveDays = 2,   // no audit events for tenant in N days
    NewRegistrationsAbove = 3 // pending/verified registrations > threshold
}

public sealed class OperatorAlertRuleEntity
{
    public Guid RuleId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional: if set, rule applies to this tenant only. Null = all tenants.</summary>
    public Guid? TenantId { get; set; }

    public AlertCondition Condition { get; set; }

    public double Threshold { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>If true, a billing webhook is fired when the alert triggers.</summary>
    public bool FireWebhook { get; set; } = false;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class OperatorAlertFiredEntity
{
    public long Id { get; set; }

    public Guid RuleId { get; set; }

    public Guid? TenantId { get; set; }

    public string RuleName { get; set; } = string.Empty;

    public double ActualValue { get; set; }

    public double Threshold { get; set; }

    public DateTimeOffset FiredAtUtc { get; set; }
}

// ── v1.6 ─────────────────────────────────────────────────────────────────────

public enum AccessRequestStatus { Pending = 0, Approved = 1, Rejected = 2 }

public sealed class FileAccessRequestEntity
{
    public Guid RequestId { get; set; }

    public Guid TenantId { get; set; }

    public Guid FileId { get; set; }

    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>Optional message from the requester.</summary>
    public string Message { get; set; } = string.Empty;

    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;

    public Guid? ReviewedByAdminId { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }
}

public enum PlanTier { Free = 0, Starter = 1, Enterprise = 2 }

public sealed class TenantPlanEntity
{
    public Guid TenantId { get; set; }

    public PlanTier Tier { get; set; } = PlanTier.Free;

    /// <summary>Max protected files. Null = unlimited.</summary>
    public int? MaxFiles { get; set; }

    /// <summary>Max total storage in megabytes. Null = unlimited.</summary>
    public int? MaxStorageMb { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AuditChainEntity
{
    /// <summary>Mirrors AuditEventEntity.Id — 1:1 relationship.</summary>
    public long AuditEventId { get; set; }

    /// <summary>HMAC-SHA256 hex of (PrevHash + Id + TenantId + EventType + CreatedAtUtc ISO8601).</summary>
    public string Hash { get; set; } = string.Empty;

    public string PrevHash { get; set; } = string.Empty;
}

// v1.7 — file collections, batch ops, key rotation

public sealed class FileCollectionEntity
{
    public Guid CollectionId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class FileCollectionItemEntity
{
    public Guid CollectionId { get; set; }
    public Guid FileId { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
}

public sealed class TenantKeyRotationConfigEntity
{
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; }
    public int IntervalDays { get; set; }
    public DateTimeOffset? LastRotatedAtUtc { get; set; }
    public DateTimeOffset? NextRotationDueUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class KeyRotationHistoryEntity
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public int FilesRotated { get; set; }
    public string TriggeredBy { get; set; } = "schedule";
    public DateTimeOffset RotatedAtUtc { get; set; }
}

// v1.8 — compliance export, GDPR erasure, data retention

public sealed class TenantRetentionPolicyEntity
{
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; }
    public int? FileRetentionDays { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// v1.9 — advanced access control

public sealed class TenantIpAllowlistRuleEntity
{
    public Guid RuleId { get; set; }
    public Guid TenantId { get; set; }
    public string Cidr { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantDeviceTrustConfigEntity
{
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; }
    /// <summary>Maximum number of days since last heartbeat before access is denied.</summary>
    public int RequiredCheckinDays { get; set; } = 7;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
