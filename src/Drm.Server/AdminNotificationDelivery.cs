using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Drm.Server;

public sealed record AdminNotificationMessage(
    Guid TenantId,
    string RecipientEmail,
    AdminNotificationEvent Event);

public sealed record AdminNotificationEvent(
    string Type,
    Guid? FileId,
    Guid? UserId,
    string? GuestEmail,
    DateTimeOffset OccurredAtUtc,
    string? ReasonCode = null);

public interface IAdminNotificationSender
{
    Task SendAsync(AdminNotificationMessage message, CancellationToken cancellationToken);
}

public interface IAdminNotificationService
{
    Task NotifyAsync(Guid tenantId, AdminNotificationEvent @event, CancellationToken cancellationToken);
}

public sealed class NoopAdminNotificationSender : IAdminNotificationSender
{
    public Task SendAsync(AdminNotificationMessage message, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SmtpAdminNotificationSender(
    SmtpEmailSettings settings,
    ILogger<SmtpAdminNotificationSender> logger) : IAdminNotificationSender
{
    public async Task SendAsync(AdminNotificationMessage message, CancellationToken cancellationToken)
    {
        var mimeMessage = BuildMessage(message, settings);

        using var client = new SmtpClient();
        try
        {
            var socketOptions = settings.SmtpUseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(settings.SmtpHost!, settings.SmtpPort, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
                await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword ?? string.Empty, cancellationToken);

            await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin notification to {Recipient}.", message.RecipientEmail);
            throw;
        }
    }

    public static MimeMessage BuildMessage(AdminNotificationMessage message, SmtpEmailSettings settings)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(string.Empty, message.RecipientEmail));
        mime.Subject = GetSubject(message.Event.Type);

        var sb = new StringBuilder();
        sb.AppendLine($"Event: {message.Event.Type}");
        sb.AppendLine($"Tenant: {message.TenantId}");
        if (message.Event.FileId.HasValue)
            sb.AppendLine($"File: {message.Event.FileId}");
        if (!string.IsNullOrWhiteSpace(message.Event.GuestEmail))
            sb.AppendLine($"Guest: {message.Event.GuestEmail}");
        if (message.Event.UserId.HasValue)
            sb.AppendLine($"User: {message.Event.UserId}");
        sb.AppendLine($"Time: {message.Event.OccurredAtUtc:u}");
        if (!string.IsNullOrWhiteSpace(message.Event.ReasonCode))
            sb.AppendLine($"Reason: {message.Event.ReasonCode}");
        sb.AppendLine();
        sb.AppendLine("This is an automated notification from DRM Security.");

        mime.Body = new TextPart("plain") { Text = sb.ToString() };
        return mime;
    }

    private static string GetSubject(string eventType) => eventType switch
    {
        "external_share_viewer_opened" => "[DRM] External guest accessed document",
        "file_revoked" => "[DRM] Protected file revoked",
        "access_denied" => "[DRM] Access denied",
        "external_share_link_created" => "[DRM] External share link created",
        _ => $"[DRM] Event: {eventType}"
    };
}

public sealed class AdminNotificationService(
    AppDbContext dbContext,
    IAdminNotificationSender sender,
    SmtpEmailSettings smtpEmailSettings,
    ILogger<AdminNotificationService> logger) : IAdminNotificationService
{
    public async Task NotifyAsync(Guid tenantId, AdminNotificationEvent @event, CancellationToken cancellationToken)
    {
        if (!smtpEmailSettings.IsConfigured)
            return;

        var config = await dbContext.TenantAdminNotificationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config == null)
            return;

        if (!IsEventTypeEnabled(config, @event.Type))
            return;

        var emails = config.AdminEmailsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        if (emails.Count == 0)
            return;

        foreach (var email in emails)
        {
            try
            {
                await sender.SendAsync(new AdminNotificationMessage(tenantId, email, @event), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send admin notification for event {EventType} to {Email}.", @event.Type, email);
            }
        }
    }

    private static bool IsEventTypeEnabled(TenantAdminNotificationConfigEntity config, string eventType)
        => eventType switch
        {
            "external_share_viewer_opened" => config.NotifyOnExternalShareViewed,
            "file_revoked" => config.NotifyOnFileRevoked,
            "access_denied" => config.NotifyOnAccessDenied,
            "external_share_link_created" => config.NotifyOnShareLinkCreated,
            _ => false
        };
}
