using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Drm.Server;

public interface IExternalShareVerificationSender
{
    Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken);
}

public sealed record ExternalShareVerificationMessage(
    Guid TenantId,
    Guid ShareLinkId,
    Guid VerificationId,
    string GuestEmail,
    string Code,
    DateTimeOffset ExpiresAtUtc);

public sealed class NoopExternalShareVerificationSender : IExternalShareVerificationSender
{
    public Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSettings
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseTls { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string FromAddress { get; set; } = "noreply@drm.local";
    public string FromName { get; set; } = "DRM Security";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}

public sealed class SmtpExternalShareVerificationSender(
    SmtpEmailSettings settings,
    ILogger<SmtpExternalShareVerificationSender> logger) : IExternalShareVerificationSender
{
    public async Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken)
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
            logger.LogWarning(ex, "Failed to send verification email to {GuestEmail}.", message.GuestEmail);
            throw;
        }
    }

    public static MimeMessage BuildMessage(ExternalShareVerificationMessage message, SmtpEmailSettings settings)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(string.Empty, message.GuestEmail));
        mime.Subject = "Your document access verification code";

        mime.Body = new TextPart("plain")
        {
            Text = $"""
                Your verification code is: {message.Code}

                This code expires at {message.ExpiresAtUtc:R}.

                Enter this code in the document viewer to complete your identity verification.

                If you did not request this code, please ignore this message.
                """
        };

        return mime;
    }
}
