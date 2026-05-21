using System.IO;
using System.Runtime.InteropServices;
using Drm.Agent.Core;

namespace Drm.Agent.Tray.Windows;

/// <summary>
/// Stage 14 — opens the user's Outlook with the .drmx attachment already
/// inlined, so the sender just clicks Send. Falls through to the next
/// composer in the chain (mailto:) when Outlook isn't installed or COM
/// activation fails for any reason. Late-bound via reflection + dynamic
/// so the project compiles on a Mac dev box without an Outlook PIA.
/// </summary>
public sealed class OutlookComEmailComposer : IEmailComposer
{
    private const int OlMailItem = 0;
    private const string OutlookProgId = "Outlook.Application";

    public EmailComposeResult Compose(EmailComposition message)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EmailComposeResult(false, false, "Outlook COM is Windows-only");
        }

        var outlookType = Type.GetTypeFromProgID(OutlookProgId, throwOnError: false);
        if (outlookType is null)
        {
            return new EmailComposeResult(false, false, "Outlook is not installed on this machine");
        }

        dynamic? outlook = null;
        dynamic? mail = null;
        try
        {
            outlook = Activator.CreateInstance(outlookType);
            if (outlook is null)
            {
                return new EmailComposeResult(false, false, "Outlook COM activation returned null");
            }

            mail = outlook.CreateItem(OlMailItem);
            mail.To = message.Recipient;
            mail.Subject = message.Subject;
            mail.Body = message.Body;

            foreach (var attachmentPath in message.AttachmentPaths)
            {
                if (!File.Exists(attachmentPath)) continue;
                mail.Attachments.Add(attachmentPath);
            }

            // Display(false) shows the composer modeless — the sender
            // reviews and clicks Send themselves. We never auto-Send.
            mail.Display(false);

            var attached = message.AttachmentPaths.Any(File.Exists);
            return new EmailComposeResult(
                ComposerOpened: true,
                AttachmentInlined: attached,
                FailureReason: null);
        }
        catch (Exception ex)
        {
            return new EmailComposeResult(false, false, ex.Message);
        }
        finally
        {
            // Release COM references explicitly — otherwise the Outlook
            // process can linger as a zombie after the sender closes the
            // composer.
            if (mail is not null) Marshal.FinalReleaseComObject(mail);
            if (outlook is not null) Marshal.FinalReleaseComObject(outlook);
        }
    }
}
