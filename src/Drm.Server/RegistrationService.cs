using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Drm.Server;

public sealed class RegistrationSettings
{
    public bool Enabled { get; set; } = false;
    public bool RequireEmailVerification { get; set; } = true;
    public bool RequireAdminApproval { get; set; } = true;
}

public interface IRegistrationEmailSender
{
    Task SendVerificationAsync(string recipientEmail, string verificationUrl, CancellationToken cancellationToken);
}

public sealed class NoopRegistrationEmailSender : IRegistrationEmailSender
{
    public Task SendVerificationAsync(string recipientEmail, string verificationUrl, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SmtpRegistrationEmailSender(
    SmtpEmailSettings settings,
    ILogger<SmtpRegistrationEmailSender> logger) : IRegistrationEmailSender
{
    public async Task SendVerificationAsync(string recipientEmail, string verificationUrl, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(string.Empty, recipientEmail));
        mime.Subject = "[DRM] Verify your tenant registration";
        mime.Body = new TextPart("plain")
        {
            Text = $"Please verify your DRM tenant registration by clicking the link below:\n\n{verificationUrl}\n\nThis link expires in 24 hours.\n\nIf you did not request this, you can ignore this email.\n"
        };

        using var client = new SmtpClient();
        try
        {
            var socketOptions = settings.SmtpUseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(settings.SmtpHost!, settings.SmtpPort, socketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
                await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword ?? string.Empty, cancellationToken);
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send registration verification email to {Email}.", recipientEmail);
            throw;
        }
    }
}

public static class RegistrationTokens
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string token)
    {
        var data = Encoding.UTF8.GetBytes(token);
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
