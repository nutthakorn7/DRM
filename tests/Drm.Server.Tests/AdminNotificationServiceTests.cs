using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drm.Server.Tests;

public sealed class AdminNotificationServiceTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-notif-svc-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Notify_does_nothing_when_smtp_not_configured()
    {
        await using var db = CreateDb();
        var sender = new RecordingNotificationSender();
        var unConfiguredSmtp = new SmtpEmailSettings(); // SmtpHost is null
        var service = new AdminNotificationService(db, sender, unConfiguredSmtp, NullLogger<AdminNotificationService>.Instance);

        await service.NotifyAsync(Guid.NewGuid(), new AdminNotificationEvent(
            "file_revoked", Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        sender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_does_nothing_when_no_config_for_tenant()
    {
        await using var db = CreateDb();
        var sender = new RecordingNotificationSender();
        var smtp = new SmtpEmailSettings { SmtpHost = "smtp.test", FromAddress = "noreply@test" };
        var service = new AdminNotificationService(db, sender, smtp, NullLogger<AdminNotificationService>.Instance);

        await service.NotifyAsync(Guid.NewGuid(), new AdminNotificationEvent(
            "file_revoked", Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        sender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_does_nothing_when_notification_type_disabled()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.TenantAdminNotificationConfigs.Add(new TenantAdminNotificationConfigEntity
        {
            TenantId = tenantId,
            AdminEmailsCsv = "admin@corp.com",
            NotifyOnFileRevoked = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var sender = new RecordingNotificationSender();
        var smtp = new SmtpEmailSettings { SmtpHost = "smtp.test", FromAddress = "noreply@test" };
        var service = new AdminNotificationService(db, sender, smtp, NullLogger<AdminNotificationService>.Instance);

        await service.NotifyAsync(tenantId, new AdminNotificationEvent(
            "file_revoked", Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        sender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_sends_to_all_configured_emails_when_enabled()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        db.TenantAdminNotificationConfigs.Add(new TenantAdminNotificationConfigEntity
        {
            TenantId = tenantId,
            AdminEmailsCsv = "admin1@corp.com,admin2@corp.com",
            NotifyOnFileRevoked = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var sender = new RecordingNotificationSender();
        var smtp = new SmtpEmailSettings { SmtpHost = "smtp.test", FromAddress = "noreply@test" };
        var service = new AdminNotificationService(db, sender, smtp, NullLogger<AdminNotificationService>.Instance);

        var @event = new AdminNotificationEvent("file_revoked", fileId, null, null, DateTimeOffset.UtcNow);
        await service.NotifyAsync(tenantId, @event, CancellationToken.None);

        sender.SentMessages.Should().HaveCount(2);
        sender.SentMessages.Should().Contain(m => m.RecipientEmail == "admin1@corp.com");
        sender.SentMessages.Should().Contain(m => m.RecipientEmail == "admin2@corp.com");
        sender.SentMessages.All(m => m.Event.FileId == fileId).Should().BeTrue();
    }

    [Fact]
    public async Task Notify_does_not_throw_when_sender_fails()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.TenantAdminNotificationConfigs.Add(new TenantAdminNotificationConfigEntity
        {
            TenantId = tenantId,
            AdminEmailsCsv = "admin@corp.com",
            NotifyOnFileRevoked = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var sender = new FailingNotificationSender();
        var smtp = new SmtpEmailSettings { SmtpHost = "smtp.test", FromAddress = "noreply@test" };
        var service = new AdminNotificationService(db, sender, smtp, NullLogger<AdminNotificationService>.Instance);

        var act = () => service.NotifyAsync(tenantId, new AdminNotificationEvent(
            "file_revoked", Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Notify_skips_empty_emails_in_csv()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.TenantAdminNotificationConfigs.Add(new TenantAdminNotificationConfigEntity
        {
            TenantId = tenantId,
            AdminEmailsCsv = "admin@corp.com,,  ,another@corp.com",
            NotifyOnFileRevoked = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var sender = new RecordingNotificationSender();
        var smtp = new SmtpEmailSettings { SmtpHost = "smtp.test", FromAddress = "noreply@test" };
        var service = new AdminNotificationService(db, sender, smtp, NullLogger<AdminNotificationService>.Instance);

        await service.NotifyAsync(tenantId, new AdminNotificationEvent(
            "file_revoked", Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        sender.SentMessages.Should().HaveCount(2);
    }

    public void Dispose()
    {
        foreach (var f in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }
}

file sealed class RecordingNotificationSender : IAdminNotificationSender
{
    public List<AdminNotificationMessage> SentMessages { get; } = new();
    public Task SendAsync(AdminNotificationMessage message, CancellationToken cancellationToken)
    {
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}

file sealed class FailingNotificationSender : IAdminNotificationSender
{
    public Task SendAsync(AdminNotificationMessage message, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("SMTP failure simulation"));
}
