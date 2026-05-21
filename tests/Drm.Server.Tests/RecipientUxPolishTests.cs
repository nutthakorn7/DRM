using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Stage 10 — recipient UX polish. Three improvements that close the
/// most visible friction points without rebuilding architecture:
///   (1) /share/ page surfaces a "what to do next" panel post-verification
///       so recipients don't think the page failed when only metadata loads.
///   (2) /share/ links to a viewer-MSI download for recipients without it.
///   (3) Agent opens the default mail client with subject + body pre-filled
///       so the sender doesn't have to retype anything.
/// </summary>
public sealed class RecipientUxPolishTests
{
    private static readonly string Root = LocateRepoRoot();

    private static readonly string ShareHtml = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/index.html"));
    private static readonly string ShareCss = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/app.css"));
    private static readonly string ShareJs = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/app.js"));
    private static readonly string TrayMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs"));
    // Stage 14 extracted the mailto: implementation out of MainWindow into
    // a reusable composer in Drm.Agent.Core. Source-presence assertions
    // about mailto plumbing now span both files.
    private static readonly string EmailComposerCore = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Core/EmailComposer.cs"));
    private static readonly string AgentEmailSurface = TrayMain + "\n" + EmailComposerCore;

    [Fact]
    public void Share_page_has_next_steps_panel_with_drmx_and_drmcontainer_callouts()
    {
        ShareHtml.Should().Contain(@"id=""nextStepsPanel""",
            "the recipient must see a next-steps guide after verification");
        ShareHtml.Should().Contain("What to do next");
        ShareHtml.Should().Contain(".drmx");
        ShareHtml.Should().Contain(".drmcontainer",
            "Stage 9 container shares need an explicit callout too");
    }

    [Fact]
    public void Share_page_has_viewer_download_link_for_external_recipients()
    {
        ShareHtml.Should().Contain(@"id=""viewerDownloadLink""",
            "external recipients without the viewer need an obvious download link");
        ShareHtml.Should().Contain("/static/zcrdrm-agent.msi",
            "link points at a static path so engineer can drop the MSI in wwwroot/static/");
    }

    [Fact]
    public void Share_css_hides_next_steps_until_session_verified()
    {
        // Pre-verification the panel should be hidden to avoid distracting
        // the recipient from the verification form. After verification
        // (data-has-session="true") the parent's CSS shows it.
        ShareCss.Should().Contain(
            "preview-details[data-has-session=\"false\"] .next-steps",
            "next-steps must be hidden until verification completes");
    }

    [Fact]
    public void Share_js_does_not_inject_misleading_sender_email()
    {
        // The verifier intentionally doesn't return the sender's email
        // (privacy). The footer copy must NOT pretend to have it.
        ShareJs.Should().NotContain("the person who shared this file",
            "footer must stay generic — verifier doesn't expose sender email");
        ShareHtml.Should().NotContain("senderHintEmail",
            "remove the placeholder span if we're not populating it");
    }

    [Fact]
    public void Tray_opens_mailto_composer_after_quick_send_success()
    {
        // Stage 14: OpenMailtoCompose was inlined into ComposeShareEmail
        // which now picks Outlook COM first and falls back to mailto:.
        TrayMain.Should().Contain("ComposeShareEmail",
            "the agent must launch the mail client after Quick Send success");
        TrayMain.Should().Contain("BuildFileShareEmailBody",
            "subject + body must be pre-composed, not blank");
        TrayMain.Should().Contain("OutlookComEmailComposer",
            "Stage 14 — Outlook COM is the preferred composer so the .drmx auto-attaches");
        TrayMain.Should().Contain("MailtoEmailComposer",
            "Stage 14 — mailto stays as fallback for non-Outlook mail clients");
    }

    [Fact]
    public void Tray_opens_mailto_composer_after_folder_share_success()
    {
        TrayMain.Should().Contain("BuildContainerShareEmailBody",
            "Stage 9 folder share must also open the mail client");
    }

    [Fact]
    public void Mailto_body_for_container_warns_against_same_email_passphrase()
    {
        // Sending the passphrase in the same email as the .drmcontainer
        // would defeat the encryption. The pre-filled body must explicitly
        // instruct the sender to use a separate channel.
        TrayMain.Should().Contain("separate channel",
            "container email body must warn against sending the passphrase in-band");
    }

    [Fact]
    public void Body_factory_branches_on_attachment_inlined_flag()
    {
        // Stage 14 — both BuildFileShareEmailBody and BuildContainerShareEmailBody
        // pick different language based on whether the attachment was inlined
        // (Outlook COM path) vs the sender drags it in manually (mailto fallback).
        // Regression guard: if a refactor accidentally collapses the two branches
        // into one string, the body lies on whichever path it didn't match.
        // We can't call the private static methods directly, but we can grep the
        // source for both branches' marker strings to prove both still exist.
        TrayMain.Should().Contain("already attached to this email",
            "Outlook-path body must say the attachment is there");
        TrayMain.Should().Contain("BEFORE SENDING THIS EMAIL",
            "mailto-fallback body must instruct the sender to attach the file");
    }

    [Fact]
    public void Quick_send_surfaces_actionable_message_when_no_composer_opens()
    {
        // Stage 16 — when both Outlook COM and mailto fail (no default
        // mail client at all) the agent must NOT show "✅ composer opened"
        // because nothing did open. The message tells the sender how to
        // fix it (Settings → Apps → Default apps → Mail) and reminds them
        // the share URL is on the clipboard.
        TrayMain.Should().Contain("Settings → Apps → Default apps → Mail",
            "the failure-mode message must give the sender the exact fix path");
        TrayMain.Should().Contain("Couldn't open an email composer",
            "the failure-mode message must say what failed, not lie about success");
    }

    [Fact]
    public void Quick_send_clears_file_picker_after_success_for_send_another_flow()
    {
        // Stage 16 — after a successful Quick Send the file picker resets
        // so the next file drop just works. Recipient stays so the
        // "same recipient, next file" pattern is one drop away.
        TrayMain.Should().Contain("quickPickedFile = null",
            "post-success must clear the picked file so the sender can drop another");
        TrayMain.Should().Contain("QuickDropFile.Text = string.Empty",
            "the drop-zone label must reset too — otherwise it still shows the just-sent file");
    }

    [Fact]
    public void Mail_client_warning_banner_appears_at_launch_when_no_handler_registered()
    {
        // Stage 17 — when Windows has no mailto: handler registered the
        // agent surfaces a yellow banner at launch (visible BEFORE any
        // Quick Send attempt) with the exact Windows Settings fix path.
        var trayXaml = File.ReadAllText(
            Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));
        trayXaml.Should().Contain("MailClientWarningBanner",
            "the banner element must exist in XAML so the code-behind can toggle it");
        trayXaml.Should().Contain("No default mail client detected",
            "the banner copy must name the problem clearly");
        TrayMain.Should().Contain("IsAnyMailtoHandlerRegistered",
            "MainWindow must probe the registry for a mailto handler");
        TrayMain.Should().Contain(@"mailto\shell\open\command",
            "the probe must check HKEY_CLASSES_ROOT\\mailto\\shell\\open\\command, the canonical handler key");
        TrayMain.Should().Contain("UpdateMailClientWarningVisibility",
            "the constructor must call the probe at launch, not lazily");
    }

    [Fact]
    public void Mailto_helper_uses_shell_execute()
    {
        // mailto: URLs only work via the shell protocol handler on Windows.
        // Direct Process.Start without UseShellExecute=true silently fails.
        // Stage 14 moved this into Drm.Agent.Core's ShellExecuteMailtoProtocolHandler.
        AgentEmailSurface.Should().Contain("UseShellExecute = true",
            "mailto: must hand off to the registered protocol handler");
    }

    [Fact]
    public void Mailto_helper_percent_encodes_subject_and_body()
    {
        // Raw newlines and quotes in body break mailto on some clients.
        // EscapeDataString covers RFC 3986 reserved chars.
        AgentEmailSurface.Should().Contain("Uri.EscapeDataString",
            "mailto subject/body must be percent-encoded");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}
