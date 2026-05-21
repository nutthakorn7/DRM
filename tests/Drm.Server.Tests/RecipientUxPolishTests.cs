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
        TrayMain.Should().Contain("OpenMailtoCompose",
            "the agent must launch the mail client after Quick Send success");
        TrayMain.Should().Contain("BuildFileShareEmailBody",
            "subject + body must be pre-composed, not blank");
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
    public void Mailto_helper_uses_shell_execute()
    {
        // mailto: URLs only work via the shell protocol handler on Windows.
        // Direct Process.Start without UseShellExecute=true silently fails.
        TrayMain.Should().Contain("UseShellExecute = true",
            "mailto: must hand off to the registered protocol handler");
    }

    [Fact]
    public void Mailto_helper_percent_encodes_subject_and_body()
    {
        // Raw newlines and quotes in body break mailto on some clients.
        // EscapeDataString covers RFC 3986 reserved chars.
        TrayMain.Should().Contain("Uri.EscapeDataString",
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
