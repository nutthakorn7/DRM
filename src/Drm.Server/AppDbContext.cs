using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    public DbSet<TenantClientKeyEntity> TenantClientKeys => Set<TenantClientKeyEntity>();

    public DbSet<TenantBillingWebhookEntity> TenantBillingWebhooks => Set<TenantBillingWebhookEntity>();

    public DbSet<TenantRegistrationEntity> TenantRegistrations => Set<TenantRegistrationEntity>();

    public DbSet<ProtectedFileEntity> ProtectedFiles => Set<ProtectedFileEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<TenantUserEntity> TenantUsers => Set<TenantUserEntity>();

    public DbSet<TenantGroupEntity> TenantGroups => Set<TenantGroupEntity>();

    public DbSet<GroupMemberEntity> GroupMembers => Set<GroupMemberEntity>();

    public DbSet<PolicyTemplateEntity> PolicyTemplates => Set<PolicyTemplateEntity>();

    public DbSet<WatermarkTemplateEntity> WatermarkTemplates => Set<WatermarkTemplateEntity>();

    public DbSet<FileGrantEntity> FileGrants => Set<FileGrantEntity>();

    public DbSet<ExternalShareLinkEntity> ExternalShareLinks => Set<ExternalShareLinkEntity>();

    public DbSet<ExternalShareVerificationEntity> ExternalShareVerifications => Set<ExternalShareVerificationEntity>();

    public DbSet<TenantExternalShareSettingsEntity> TenantExternalShareSettings => Set<TenantExternalShareSettingsEntity>();

    public DbSet<SiemWebhookEntity> SiemWebhooks => Set<SiemWebhookEntity>();

    public DbSet<AgentDeviceEntity> AgentDevices => Set<AgentDeviceEntity>();

    public DbSet<AgentCommandEntity> AgentCommands => Set<AgentCommandEntity>();

    public DbSet<FileKeyEntity> FileKeys => Set<FileKeyEntity>();

    public DbSet<TenantDirectorySyncConfigEntity> TenantDirectorySyncConfigs => Set<TenantDirectorySyncConfigEntity>();

    public DbSet<TenantAdminNotificationConfigEntity> TenantAdminNotificationConfigs => Set<TenantAdminNotificationConfigEntity>();

    public DbSet<TenantBoxIntegrationConfigEntity> TenantBoxIntegrationConfigs => Set<TenantBoxIntegrationConfigEntity>();

    public DbSet<BoxWebhookEventEntity> BoxWebhookEvents => Set<BoxWebhookEventEntity>();

    public DbSet<TenantOutlookIntegrationConfigEntity> TenantOutlookIntegrationConfigs => Set<TenantOutlookIntegrationConfigEntity>();

    public DbSet<OutlookAttachmentEventEntity> OutlookAttachmentEvents => Set<OutlookAttachmentEventEntity>();

    public DbSet<FileTagEntity> FileTags => Set<FileTagEntity>();

    public DbSet<TenantUserPersonaEntity> TenantUserPersonas => Set<TenantUserPersonaEntity>();

    public DbSet<TransparentProtectedFileEntity> TransparentProtectedFiles => Set<TransparentProtectedFileEntity>();

    public DbSet<SecureContainerEntity> SecureContainers => Set<SecureContainerEntity>();

    public DbSet<SecureContainerFileEntity> SecureContainerFiles => Set<SecureContainerFileEntity>();

    public DbSet<TenantFolderWatcherConfigEntity> TenantFolderWatcherConfigs => Set<TenantFolderWatcherConfigEntity>();

    public DbSet<FolderWatcherEventEntity> FolderWatcherEvents => Set<FolderWatcherEventEntity>();

    public DbSet<AdminUserEntity> AdminUsers => Set<AdminUserEntity>();

    public DbSet<AdminRoleEntity> AdminRoles => Set<AdminRoleEntity>();

    public DbSet<AdminApiTokenEntity> AdminApiTokens => Set<AdminApiTokenEntity>();

    public DbSet<OperatorAlertRuleEntity> OperatorAlertRules => Set<OperatorAlertRuleEntity>();

    public DbSet<OperatorAlertFiredEntity> OperatorAlertsFired => Set<OperatorAlertFiredEntity>();

    public DbSet<FileAccessRequestEntity> FileAccessRequests => Set<FileAccessRequestEntity>();

    public DbSet<TenantPlanEntity> TenantPlans => Set<TenantPlanEntity>();

    public DbSet<AuditChainEntity> AuditChain => Set<AuditChainEntity>();

    public DbSet<FileCollectionEntity> FileCollections => Set<FileCollectionEntity>();

    public DbSet<FileCollectionItemEntity> FileCollectionItems => Set<FileCollectionItemEntity>();

    public DbSet<TenantKeyRotationConfigEntity> TenantKeyRotationConfigs => Set<TenantKeyRotationConfigEntity>();

    public DbSet<KeyRotationHistoryEntity> KeyRotationHistory => Set<KeyRotationHistoryEntity>();

    public DbSet<TenantRetentionPolicyEntity> TenantRetentionPolicies => Set<TenantRetentionPolicyEntity>();

    public DbSet<TenantIpAllowlistRuleEntity> TenantIpAllowlistRules => Set<TenantIpAllowlistRuleEntity>();

    public DbSet<TenantDeviceTrustConfigEntity> TenantDeviceTrustConfigs => Set<TenantDeviceTrustConfigEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantClientKeyEntity>(entity =>
        {
            entity.HasKey(k => new { k.TenantId, k.KeyId });
            entity.HasIndex(k => k.KeyHash).IsUnique();
            entity.Property(k => k.KeyHash).HasMaxLength(128);
            entity.Property(k => k.Label).HasMaxLength(128);
        });

        modelBuilder.Entity<TenantBillingWebhookEntity>(entity =>
        {
            entity.HasKey(w => new { w.TenantId, w.WebhookId });
            entity.Property(w => w.Url).HasMaxLength(2048);
            entity.Property(w => w.Secret).HasMaxLength(256);
            entity.Property(w => w.Events).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantRegistrationEntity>(entity =>
        {
            entity.HasKey(r => r.RegistrationId);
            entity.HasIndex(r => r.AdminEmail);
            entity.HasIndex(r => r.TenantName);
            entity.HasIndex(r => r.TokenHash).IsUnique();
            entity.Property(r => r.TenantName).HasMaxLength(256);
            entity.Property(r => r.DisplayName).HasMaxLength(256);
            entity.Property(r => r.AdminEmail).HasMaxLength(320);
            entity.Property(r => r.AdminDisplayName).HasMaxLength(256);
            entity.Property(r => r.TokenHash).HasMaxLength(128);
            entity.Property(r => r.ReviewNotes).HasMaxLength(1024);
            entity.Property(r => r.Status).HasConversion<int>();
        });

        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.HasKey(t => t.TenantId);
            entity.HasIndex(t => t.Name).IsUnique();
            entity.Property(t => t.Name).HasMaxLength(256);
            entity.Property(t => t.DisplayName).HasMaxLength(256);
            entity.Property(t => t.Status).HasConversion<int>();
        });

        modelBuilder.Entity<ProtectedFileEntity>(entity =>
        {
            entity.HasKey(file => new { file.TenantId, file.Id });
            entity.Property(file => file.ContentType).HasMaxLength(256);
            entity.Property(file => file.WatermarkTemplate).HasMaxLength(1024);
            entity.Property(file => file.Permissions).HasConversion<int>();
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.CreatedAtUtc });
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(128);
            entity.Property(auditEvent => auditEvent.ReasonCode).HasMaxLength(128);
        });

        modelBuilder.Entity<TenantUserEntity>(entity =>
        {
            entity.HasKey(user => new { user.TenantId, user.UserId });
            entity.HasIndex(user => new { user.TenantId, user.Email }).IsUnique();
            entity.HasIndex(user => new { user.TenantId, user.ExternalId });
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasMaxLength(256);
            entity.Property(user => user.ExternalId).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantGroupEntity>(entity =>
        {
            entity.HasKey(group => new { group.TenantId, group.GroupId });
            entity.HasIndex(group => new { group.TenantId, group.ExternalId });
            entity.Property(group => group.Name).HasMaxLength(256);
            entity.Property(group => group.ExternalId).HasMaxLength(256);
        });

        modelBuilder.Entity<GroupMemberEntity>(entity =>
        {
            entity.HasKey(member => new { member.TenantId, member.GroupId, member.UserId });
        });

        modelBuilder.Entity<PolicyTemplateEntity>(entity =>
        {
            entity.HasKey(template => new { template.TenantId, template.TemplateId });
            entity.Property(template => template.Name).HasMaxLength(256);
            entity.Property(template => template.Permissions).HasMaxLength(256);
            entity.Property(template => template.WatermarkTemplate).HasMaxLength(1024);
        });

        modelBuilder.Entity<WatermarkTemplateEntity>(entity =>
        {
            entity.HasKey(template => new { template.TenantId, template.WatermarkTemplateId });
            entity.Property(template => template.Name).HasMaxLength(256);
            entity.Property(template => template.Pattern).HasMaxLength(1024);
            entity.Property(template => template.PrintWatermarkPattern).HasMaxLength(1024);
            entity.Property(template => template.PrintWatermarkPosition).HasMaxLength(32);
        });

        modelBuilder.Entity<FileGrantEntity>(entity =>
        {
            entity.HasKey(grant => new { grant.TenantId, grant.FileId, grant.SubjectType, grant.SubjectId });
            entity.Property(grant => grant.SubjectType).HasMaxLength(32);
            entity.Property(grant => grant.Permissions).HasMaxLength(256);
        });

        modelBuilder.Entity<ExternalShareLinkEntity>(entity =>
        {
            entity.HasKey(shareLink => new { shareLink.TenantId, shareLink.ShareLinkId });
            entity.HasIndex(shareLink => new { shareLink.TenantId, shareLink.FileId });
            entity.HasIndex(shareLink => new { shareLink.TenantId, shareLink.TokenHash }).IsUnique();
            entity.Property(shareLink => shareLink.TokenHash).HasMaxLength(128);
            entity.Property(shareLink => shareLink.GuestEmail).HasMaxLength(320);
        });

        modelBuilder.Entity<ExternalShareVerificationEntity>(entity =>
        {
            entity.HasKey(verification => new { verification.TenantId, verification.VerificationId });
            entity.HasIndex(verification => new { verification.TenantId, verification.ShareLinkId });
            entity.Property(verification => verification.GuestEmail).HasMaxLength(320);
            entity.Property(verification => verification.CodeHash).HasMaxLength(128);
            entity.Property(verification => verification.SessionTokenHash).HasMaxLength(128);
        });

        modelBuilder.Entity<TenantExternalShareSettingsEntity>(entity =>
        {
            entity.HasKey(settings => settings.TenantId);
            entity.Property(settings => settings.AllowedGuestEmailDomainsCsv).HasMaxLength(4096);
            entity.Property(settings => settings.BlockedGuestEmailsCsv).HasMaxLength(16384);
        });

        modelBuilder.Entity<SiemWebhookEntity>(entity =>
        {
            entity.HasKey(webhook => new { webhook.TenantId, webhook.WebhookId });
            entity.Property(webhook => webhook.Url).HasMaxLength(2048);
        });

        modelBuilder.Entity<AgentDeviceEntity>(entity =>
        {
            entity.HasKey(device => new { device.TenantId, device.DeviceId });
            entity.HasIndex(device => new { device.TenantId, device.UserId });
            entity.Property(device => device.Hostname).HasMaxLength(256);
            entity.Property(device => device.OperatingSystem).HasMaxLength(256);
            entity.Property(device => device.AgentVersion).HasMaxLength(64);
            entity.Property(device => device.Status).HasMaxLength(64);
            entity.Property(device => device.DisabledReason).HasMaxLength(128);
        });

        modelBuilder.Entity<AgentCommandEntity>(entity =>
        {
            entity.HasKey(command => new { command.TenantId, command.CommandId });
            entity.HasIndex(command => new { command.TenantId, command.DeviceId, command.Status });
            entity.Property(command => command.CommandType).HasMaxLength(64);
            entity.Property(command => command.Status).HasMaxLength(64);
            entity.Property(command => command.ReasonCode).HasMaxLength(128);
        });

        modelBuilder.Entity<FileKeyEntity>(entity =>
        {
            entity.HasKey(fileKey => new { fileKey.TenantId, fileKey.FileId });
            entity.Property(fileKey => fileKey.WrappedKeyNonceBase64).HasMaxLength(64);
            entity.Property(fileKey => fileKey.WrappedKeyCiphertextBase64).HasMaxLength(256);
            entity.Property(fileKey => fileKey.WrappedKeyTagBase64).HasMaxLength(64);
        });

        modelBuilder.Entity<TenantDirectorySyncConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
            entity.Property(c => c.EntraTenantId).HasMaxLength(64);
            entity.Property(c => c.ClientId).HasMaxLength(128);
            entity.Property(c => c.ClientSecret).HasMaxLength(512);
            entity.Property(c => c.LastSyncStatus).HasMaxLength(32);
        });

        modelBuilder.Entity<TenantAdminNotificationConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
            entity.Property(c => c.AdminEmailsCsv).HasMaxLength(1024);
        });

        modelBuilder.Entity<TenantBoxIntegrationConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
            entity.Property(c => c.ClientId).HasMaxLength(128);
            entity.Property(c => c.ClientSecret).HasMaxLength(512);
            entity.Property(c => c.EnterpriseId).HasMaxLength(64);
            entity.Property(c => c.WebhookSecret).HasMaxLength(256);
            entity.Property(c => c.LastConnectionStatus).HasMaxLength(64);
        });

        modelBuilder.Entity<BoxWebhookEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.ReceivedAtUtc });
            entity.Property(e => e.EventType).HasMaxLength(128);
            entity.Property(e => e.SourceItemId).HasMaxLength(64);
            entity.Property(e => e.SourceItemName).HasMaxLength(512);
            entity.Property(e => e.CreatedByEmail).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantOutlookIntegrationConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
            entity.Property(c => c.SkipDomainsCsv).HasMaxLength(1024);
            entity.Property(c => c.DefaultPolicyTemplateId).HasMaxLength(64);
        });

        modelBuilder.Entity<OutlookAttachmentEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Id });
            entity.Property(e => e.SenderEmail).HasMaxLength(256);
            entity.Property(e => e.RecipientCsv).HasMaxLength(2048);
            entity.Property(e => e.AttachmentName).HasMaxLength(512);
            entity.Property(e => e.Status).HasMaxLength(64);
            entity.Property(e => e.ProtectedFileId).HasMaxLength(64);
        });

        modelBuilder.Entity<FileTagEntity>(entity =>
        {
            entity.HasKey(t => new { t.TenantId, t.FileId, t.Tag });
            entity.HasIndex(t => new { t.TenantId, t.Tag });
            entity.Property(t => t.Tag).HasMaxLength(64);
        });

        modelBuilder.Entity<TenantUserPersonaEntity>(entity =>
        {
            entity.HasKey(p => new { p.TenantId, p.UserId });
            entity.Property(p => p.Persona).HasMaxLength(32);
        });

        modelBuilder.Entity<TransparentProtectedFileEntity>(entity =>
        {
            entity.HasKey(t => new { t.TenantId, t.FileId });
            entity.HasIndex(t => t.TenantId);
            entity.Property(t => t.OriginalFileName).HasMaxLength(512);
            entity.Property(t => t.ContentType).HasMaxLength(128);
        });

        modelBuilder.Entity<SecureContainerEntity>(entity =>
        {
            entity.HasKey(c => new { c.TenantId, c.ContainerId });
            entity.HasIndex(c => c.TenantId);
            entity.Property(c => c.DisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<SecureContainerFileEntity>(entity =>
        {
            entity.HasKey(c => new { c.TenantId, c.ContainerId, c.OrdinalIndex });
            entity.HasIndex(c => new { c.TenantId, c.ContainerId });
            entity.Property(c => c.RelativePath).HasMaxLength(1024);
        });

        modelBuilder.Entity<TenantFolderWatcherConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
            entity.Property(c => c.LastReportStatus).HasMaxLength(64);
            entity.Property(c => c.Hostname).HasMaxLength(256);
        });

        modelBuilder.Entity<FolderWatcherEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Id });
            entity.Property(e => e.Hostname).HasMaxLength(256);
            entity.Property(e => e.FolderPath).HasMaxLength(1024);
            entity.Property(e => e.FileName).HasMaxLength(512);
            entity.Property(e => e.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<AdminUserEntity>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(320);
            entity.Property(u => u.DisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<AdminRoleEntity>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Name).IsUnique();
            entity.Property(r => r.Name).HasMaxLength(64);
            entity.Property(r => r.PermissionsCsv).HasMaxLength(4096);
        });

        modelBuilder.Entity<AdminApiTokenEntity>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.AdminUserId);
            entity.Property(t => t.TokenHash).HasMaxLength(128);
            entity.Property(t => t.Label).HasMaxLength(128);
        });

        modelBuilder.Entity<OperatorAlertRuleEntity>(entity =>
        {
            entity.HasKey(r => r.RuleId);
            entity.HasIndex(r => r.TenantId);
            entity.Property(r => r.Name).HasMaxLength(256);
            entity.Property(r => r.Condition).HasConversion<int>();
        });

        modelBuilder.Entity<OperatorAlertFiredEntity>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.HasIndex(f => new { f.RuleId, f.FiredAtUtc });
            entity.Property(f => f.RuleName).HasMaxLength(256);
        });

        modelBuilder.Entity<FileAccessRequestEntity>(entity =>
        {
            entity.HasKey(r => r.RequestId);
            entity.HasIndex(r => new { r.TenantId, r.FileId });
            entity.HasIndex(r => new { r.TenantId, r.Status });
            entity.Property(r => r.RequesterEmail).HasMaxLength(320);
            entity.Property(r => r.Message).HasMaxLength(2048);
            entity.Property(r => r.Status).HasConversion<int>();
        });

        modelBuilder.Entity<TenantPlanEntity>(entity =>
        {
            entity.HasKey(p => p.TenantId);
            entity.Property(p => p.Tier).HasConversion<int>();
        });

        modelBuilder.Entity<AuditChainEntity>(entity =>
        {
            entity.HasKey(c => c.AuditEventId);
            entity.Property(c => c.Hash).HasMaxLength(64);
            entity.Property(c => c.PrevHash).HasMaxLength(64);
        });

        modelBuilder.Entity<FileCollectionEntity>(entity =>
        {
            entity.HasKey(c => c.CollectionId);
            entity.HasIndex(c => c.TenantId);
            entity.Property(c => c.Name).HasMaxLength(256);
            entity.Property(c => c.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<FileCollectionItemEntity>(entity =>
        {
            entity.HasKey(i => new { i.CollectionId, i.FileId });
            entity.HasIndex(i => i.TenantId);
        });

        modelBuilder.Entity<TenantKeyRotationConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
        });

        modelBuilder.Entity<KeyRotationHistoryEntity>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.TenantId);
            entity.Property(h => h.TriggeredBy).HasMaxLength(32);
        });

        modelBuilder.Entity<TenantRetentionPolicyEntity>(entity =>
        {
            entity.HasKey(p => p.TenantId);
        });

        modelBuilder.Entity<TenantIpAllowlistRuleEntity>(entity =>
        {
            entity.HasKey(r => r.RuleId);
            entity.HasIndex(r => r.TenantId);
            entity.Property(r => r.Cidr).HasMaxLength(50);
            entity.Property(r => r.Label).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantDeviceTrustConfigEntity>(entity =>
        {
            entity.HasKey(c => c.TenantId);
        });
    }
}
