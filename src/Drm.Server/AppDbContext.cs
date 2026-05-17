using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
