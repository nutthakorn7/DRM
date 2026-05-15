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

    public DbSet<FileGrantEntity> FileGrants => Set<FileGrantEntity>();

    public DbSet<SiemWebhookEntity> SiemWebhooks => Set<SiemWebhookEntity>();

    public DbSet<AgentDeviceEntity> AgentDevices => Set<AgentDeviceEntity>();

    public DbSet<AgentCommandEntity> AgentCommands => Set<AgentCommandEntity>();

    public DbSet<FileKeyEntity> FileKeys => Set<FileKeyEntity>();

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
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantGroupEntity>(entity =>
        {
            entity.HasKey(group => new { group.TenantId, group.GroupId });
            entity.Property(group => group.Name).HasMaxLength(256);
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

        modelBuilder.Entity<FileGrantEntity>(entity =>
        {
            entity.HasKey(grant => new { grant.TenantId, grant.FileId, grant.SubjectType, grant.SubjectId });
            entity.Property(grant => grant.SubjectType).HasMaxLength(32);
            entity.Property(grant => grant.Permissions).HasMaxLength(256);
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
    }
}
