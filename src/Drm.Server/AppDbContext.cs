using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProtectedFileEntity> ProtectedFiles => Set<ProtectedFileEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProtectedFileEntity>(entity =>
        {
            entity.HasKey(file => file.Id);
            entity.HasIndex(file => new { file.TenantId, file.Id }).IsUnique();
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
    }
}
