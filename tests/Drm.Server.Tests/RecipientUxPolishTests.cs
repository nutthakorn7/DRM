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

    private static readonly string DrmxPreviewJs = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/drmx-preview.js"));

    [Fact]
    public void Share_page_wires_inbrowser_preview_for_viewonly_shares()
    {
        // Increment 2: in-browser preview UI + the View-only gate + the
        // client-side decrypt module are all present and wired.
        ShareHtml.Should().Contain(@"id=""inbrowserPreview""",
            "the preview section must exist (revealed by JS only for View-only shares)");
        ShareHtml.Should().Contain(@"id=""drmxFileInput""",
            "recipient needs a file input to load the .drmx from the email");
        ShareHtml.Should().Contain(@"type=""module"" src=""/share/drmx-preview.js""",
            "the decrypt module must be loaded on the page");

        ShareJs.Should().Contain("maybeEnableInBrowserPreview",
            "app.js must decide whether to reveal preview based on permissions");
        ShareJs.Should().Contain("granted.size === 1 && granted.has(\"View\")",
            "preview must be gated to exactly View-only — never shown when stricter perms can't be enforced in a browser");
        ShareJs.Should().Contain("/api/share-links/viewer/content-key",
            "preview must fetch the key from the gated content-key endpoint");

        DrmxPreviewJs.Should().Contain("export async function decryptDrmx",
            "the client decrypt entry point must exist");
        DrmxPreviewJs.Should().Contain("additionalData: aad",
            "AES-GCM must pass the reconstructed associated data or auth fails");
    }

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
    public void Folder_share_still_has_mail_composer_helpers()
    {
        // The internal CAD primary flow no longer opens an external share
        // email, but the legacy folder-share surface still uses the
        // reusable composer helpers.
        TrayMain.Should().Contain("ComposeShareEmail",
            "folder share still launches the mail client after recipient share success");
        TrayMain.Should().Contain("BuildFileShareEmailBody",
            "legacy file-share subject + body helpers remain for non-CAD surfaces");
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
    public void Internal_cad_quick_protect_has_no_mail_client_warning_dependency()
    {
        var trayXaml = File.ReadAllText(
            Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));

        trayXaml.Should().NotContain("MailClientWarningBanner",
            "internal CAD protect does not open an external email composer");
        TrayMain.Should().NotContain("IsAnyMailtoHandlerRegistered",
            "internal CAD protect should not gate UX on a mailto registry probe");
        TrayMain.Should().NotContain(@"mailto\shell\open\command",
            "internal CAD protect should not probe the mailto handler at launch");
    }

    [Fact]
    public void Internal_cad_quick_protect_clears_file_picker_after_success()
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
    public void Internal_cad_quick_protect_copy_names_the_customer_flow()
    {
        var trayXaml = File.ReadAllText(
            Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));

        trayXaml.Should().Contain("Protect CAD file (internal)");
        trayXaml.Should().Contain("Encrypt CAD files for AD-joined company devices");
        trayXaml.Should().Contain("Protect CAD file");
    }

    [Fact]
    public void Internal_cad_quick_protect_uses_empty_recipients_and_cad_gate()
    {
        var trayXaml = File.ReadAllText(
            Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));

        TrayMain.Should().Contain("IsSupportedCadFile(quickPickedFile)",
            "internal protect must reject non-CAD files before encryption");
        TrayMain.Should().Contain("new ProtectFilePolicyOptions(Permission.View, PolicyTemplateId: null, Recipients: [])",
            "internal CAD protect does not create guest recipients or external share links");
        TrayMain.Should().NotContain("BulkRecipientParser.Parse",
            "the CAD-only path should not parse external recipient input");
        TrayMain.Should().NotContain("for (var index = 0; index < recipients.Count; index++)",
            "the CAD-only path should not loop over guest recipients");
        trayXaml.Should().NotContain("separate multiple recipients with a comma or semicolon",
            "the primary UX must not teach external bulk-share syntax for this customer flow");
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
