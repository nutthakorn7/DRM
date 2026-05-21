namespace Drm.Agent.Core;

public sealed record EmailComposition(
    string Recipient,
    string Subject,
    string Body,
    IReadOnlyList<string> AttachmentPaths);

public interface IEmailComposer
{
    EmailComposeResult Compose(EmailComposition message);
}

public sealed record EmailComposeResult(
    bool ComposerOpened,
    bool AttachmentInlined,
    string? FailureReason);

public sealed class MailtoEmailComposer(IMailtoProtocolHandler? protocolHandler = null) : IEmailComposer
{
    private readonly IMailtoProtocolHandler handler = protocolHandler ?? new ShellExecuteMailtoProtocolHandler();

    public EmailComposeResult Compose(EmailComposition message)
    {
        try
        {
            var url =
                $"mailto:{Uri.EscapeDataString(message.Recipient)}" +
                $"?subject={Uri.EscapeDataString(message.Subject)}" +
                $"&body={Uri.EscapeDataString(message.Body)}";
            handler.Open(url);
            return new EmailComposeResult(
                ComposerOpened: true,
                AttachmentInlined: false,
                FailureReason: null);
        }
        catch (Exception ex)
        {
            return new EmailComposeResult(
                ComposerOpened: false,
                AttachmentInlined: false,
                FailureReason: ex.Message);
        }
    }
}

public interface IMailtoProtocolHandler
{
    void Open(string mailtoUrl);
}

internal sealed class ShellExecuteMailtoProtocolHandler : IMailtoProtocolHandler
{
    public void Open(string mailtoUrl)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = mailtoUrl,
            UseShellExecute = true
        });
    }
}
