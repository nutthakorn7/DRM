using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Static validation that the WiX MSI registers the
/// folder-right-click → "Protect folder with zcrDRM" verb and that the
/// tray accepts the --protect-folder CLI flag it shells out with.
///
/// CI builds on Linux (no Windows installer toolchain), so the only way
/// to catch a typo / missing reference is to grep the source files.
/// </summary>
public sealed class MsiFolderRightClickTests
{
    private static readonly string Root = LocateRepoRoot();

    private static readonly string ProductWxs = File.ReadAllText(
        Path.Combine(Root, "deploy/windows-msi/Product.wxs"));
    private static readonly string TrayMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs"));
    private static readonly string TrayXaml = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml"));

    [Fact]
    public void Wix_registers_directory_shell_protect_folder_verb()
    {
        // Folders live under HKCR\Directory\shell — file verbs at
        // HKCR\*\shell don't fire on folders. The verb is "zcrDRMProtectFolder"
        // (no submenu — folders only have one DRM operation).
        ProductWxs.Should().Contain(@"Directory\shell\zcrDRMProtectFolder",
            "WiX must register the Directory verb so folder right-click works");
        ProductWxs.Should().Contain(@"Directory\shell\zcrDRMProtectFolder\command",
            "the Directory verb needs a \\command subkey or right-click does nothing");
    }

    [Fact]
    public void Wix_folder_verb_shells_tray_with_protect_folder_flag_and_v_token()
    {
        // %V is the canonical Directory\shell selection token; using %1
        // also works on most builds but isn't guaranteed. The tray must
        // also be quoted to survive paths with spaces (Program Files).
        ProductWxs.Should().Contain("--protect-folder &quot;%V&quot;",
            "the verb's command must pass --protect-folder \"%V\" so the tray gets the picked folder");
        ProductWxs.Should().Contain("[INSTALLFOLDER]Drm.Agent.Tray.Windows.exe",
            "the verb must shell the tray, not the viewer");
    }

    [Fact]
    public void Wix_main_feature_references_the_new_folder_verb_component()
    {
        // A Component without a ComponentRef in the Feature is silently
        // dropped from the MSI — easy to miss in code review.
        ProductWxs.Should().Contain(@"<ComponentRef Id=""ProtectFolderShellMenu"" />",
            "Main feature must reference ProtectFolderShellMenu or the verb is missing from the MSI");
    }

    [Fact]
    public void Tray_accepts_protect_folder_cli_flag()
    {
        // The verb passes --protect-folder; the tray must parse it.
        // Otherwise the folder path is dropped on the floor and the user
        // sees an empty drop zone.
        TrayMain.Should().Contain("--protect-folder",
            "tray must parse --protect-folder or the folder-right-click flow is broken");
    }

    [Fact]
    public void Tray_xaml_exposes_seal_folder_button_for_cli_entry_path()
    {
        // The CLI entry path (right-click → tray launched fresh) can't
        // simulate a drag-and-drop event. The tray needs an explicit
        // button so the user can finish the workflow after typing the
        // passphrase.
        TrayXaml.Should().Contain("SealFolderButton_Click",
            "Container section must have a Seal folder button or CLI launches force re-drop");
        TrayXaml.Should().Contain(@"x:Name=""SealFolderButton""",
            "the button needs a stable name for the code-behind handler");
        TrayMain.Should().Contain("SealFolderButton_Click",
            "code-behind must implement the button handler");
    }

    [Fact]
    public void Tray_seal_handler_delegates_to_shared_pack_method()
    {
        // Drag-drop and the Seal button both must funnel through
        // PackContainerFromFolderAsync — otherwise drift creeps in (one
        // path forgets to register with the server, etc).
        TrayMain.Should().Contain("PackContainerFromFolderAsync",
            "drop + button must share the packaging code path");
    }

    private static string LocateRepoRoot()
    {
        // Walk up until we find a `.git` entry. In a checked-out clone
        // it's a directory; in a git WORKTREE it's a FILE pointing back
        // at the main repo's gitdir. The original ShellIntegrationScriptTests
        // helper only checked Directory.Exists which silently walks past
        // a worktree root and reads the main repo's stale source —
        // making in-worktree CI runs assert against the wrong file.
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
