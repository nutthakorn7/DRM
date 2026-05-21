using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Stage 9 — confirm the agent's Container section exposes the per-
/// recipient share picker (recipient + permissions + expiry) on top of
/// the existing self-share-via-passphrase flow. Mirrors the wire shape
/// of Quick Send so the server doesn't need a new endpoint.
/// </summary>
public sealed class FolderShareWiringTests
{
    private static readonly string Root = LocateRepoRoot();

    private static readonly string TrayXaml = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));
    private static readonly string TrayMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs"));

    [Fact]
    public void Container_section_has_recipient_email_box()
    {
        TrayXaml.Should().Contain(@"x:Name=""FolderShareRecipientBox""",
            "Container section must expose a recipient field — folder parity with Quick Send");
    }

    [Fact]
    public void Container_section_has_all_four_permission_checkboxes()
    {
        // Same four flags Quick Send (Stage 7) ships: Print, Copy, Edit,
        // Download-original. View is implicit.
        TrayXaml.Should().Contain(@"x:Name=""FolderShareAllowPrintBox""");
        TrayXaml.Should().Contain(@"x:Name=""FolderShareAllowCopyBox""");
        TrayXaml.Should().Contain(@"x:Name=""FolderShareAllowEditBox""");
        TrayXaml.Should().Contain(@"x:Name=""FolderShareAllowExportOriginalBox""");
    }

    [Fact]
    public void Container_section_has_expiry_dropdown_with_quick_send_tags()
    {
        // The four tag values match Quick Send: 168 / 720 / 2160 / 8760
        // (7d / 30d / 90d / 365d). Locked so divergence between file and
        // folder UX shows up here loud.
        TrayXaml.Should().Contain(@"x:Name=""FolderShareExpiryDropdown""");
        TrayXaml.Should().Contain(@"Tag=""168""");
        TrayXaml.Should().Contain(@"Tag=""720""");
        TrayXaml.Should().Contain(@"Tag=""2160""");
        TrayXaml.Should().Contain(@"Tag=""8760""");
    }

    [Fact]
    public void Container_section_has_share_with_recipient_button()
    {
        TrayXaml.Should().Contain(@"x:Name=""ShareFolderButton""",
            "Container section needs the Stage 9 'Share with recipient' button");
        TrayXaml.Should().Contain("ShareFolderButton_Click");
        TrayMain.Should().Contain("ShareFolderButton_Click",
            "code-behind must implement the button handler");
    }

    [Fact]
    public void Share_handler_posts_to_me_share_with_container_content_type()
    {
        // Hard-locked: the agent MUST hit /api/me/share (not the admin
        // secure-containers endpoint) and MUST advertise the container
        // MIME so server-side audit can distinguish containers from
        // single files later.
        TrayMain.Should().Contain("\"/api/me/share\"",
            "Stage 9 must reuse the file Quick Send endpoint, not /api/admin/secure-containers");
        TrayMain.Should().Contain("application/vnd.zcrdrm.container",
            "container shares must advertise their MIME so /share/ can render container-aware UI later");
    }

    [Fact]
    public void Share_handler_keeps_passphrase_requirement_for_recipient_unpack()
    {
        // The recipient still unpacks with the passphrase out-of-band;
        // /api/me/share gives us policy + audit, not key delivery. If a
        // future refactor drops the passphrase gate, this test fails so
        // the gap is surfaced explicitly.
        TrayMain.Should().Contain("passphrase ≥ 6 chars",
            "Stage 9 must still require passphrase — server doesn't hold container keys");
    }

    [Fact]
    public void Self_share_seal_button_still_present()
    {
        // Backwards compat: the old passphrase-only "Seal folder" flow
        // must still work for operators who don't have a recipient yet
        // (or who want the .drmcontainer without any server registration).
        TrayXaml.Should().Contain(@"x:Name=""SealFolderButton""",
            "self-share Seal button must coexist with the new recipient-share button");
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
