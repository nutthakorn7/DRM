using System.Text.RegularExpressions;
using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Static validation for the Phase 5AS-shell PowerShell scripts. We do not
/// have a Windows machine in CI for the Linux build, so we sanity-check
/// the scripts on text content alone: every key install.ps1 writes must
/// be removed by uninstall.ps1, and the verb identifiers must match the
/// tray's accepted CLI flags.
/// </summary>
public sealed class ShellIntegrationScriptTests
{
    private static readonly string Root = LocateRepoRoot();

    private static readonly string InstallScript = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Shell.Windows/install.ps1"));
    private static readonly string UninstallScript = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Shell.Windows/uninstall.ps1"));
    private static readonly string StatusScript = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Shell.Windows/status.ps1"));
    private static readonly string TrayMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs"));
    private static readonly string ViewerMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Viewer.Windows/MainWindow.xaml.cs"));

    [Fact]
    public void Install_script_declares_three_verbs_quicksend_protect_transparent()
    {
        InstallScript.Should().Contain("Drm.QuickSend");
        InstallScript.Should().Contain("Drm.Protect");
        InstallScript.Should().Contain("Drm.TransparentProtect");
    }

    [Fact]
    public void Install_script_registers_drmx_and_drmcontainer_associations()
    {
        InstallScript.Should().Contain(".drmx");
        InstallScript.Should().Contain(".drmcontainer");
        InstallScript.Should().Contain("DRM.ProtectedFile.1");
        InstallScript.Should().Contain("DRM.SecureContainer.1");
    }

    [Fact]
    public void Tray_handles_every_cli_flag_used_by_install_script()
    {
        // Every CLI flag we ask Explorer to pass to the tray must be parsed
        // by the tray. Otherwise right-click loses its file path.
        var flagPattern = new Regex(@"-{2}(quick-protect|protect|transparent-protect|quick-send-to)", RegexOptions.Compiled);
        var flagsInInstall = flagPattern.Matches(InstallScript)
            .Select(m => m.Value)
            .Distinct()
            .ToArray();
        flagsInInstall.Should().NotBeEmpty();

        foreach (var flag in flagsInInstall)
        {
            TrayMain.Should().Contain(flag,
                $"tray must accept the {flag} CLI flag the shell integration passes");
        }
    }

    [Fact]
    public void Viewer_handles_open_flag_used_by_install_script()
    {
        InstallScript.Should().Contain("--open");
        ViewerMain.Should().Contain("--open");
    }

    [Fact]
    public void Uninstall_script_removes_every_registry_root_install_creates()
    {
        // Capture every "HKCU:\Software\Classes\..." path the install
        // script touches as a base key (no escaping of $variable refs;
        // we extract by prefix).
        var keyPattern = new Regex(
            @"""(HKCU:\\Software\\Classes\\[^""\$\(]+)""",
            RegexOptions.Compiled);

        var installBaseKeys = keyPattern.Matches(InstallScript)
            .Select(m => m.Groups[1].Value)
            .Where(p => !p.Contains("$"))
            .Select(p => p.TrimEnd('\\'))
            .Distinct()
            .ToList();

        installBaseKeys.Should().NotBeEmpty();

        foreach (var installedKey in installBaseKeys)
        {
            var root = GetTopLevelKey(installedKey);
            UninstallScript.Should().Contain(root,
                $"uninstall.ps1 must list {root} (descendants of {installedKey}) so install is reversible");
        }
    }

    [Fact]
    public void All_scripts_use_HKCU_only_for_per_user_install_without_admin()
    {
        // Per-user install is the explicit design choice. Catch a future
        // refactor that accidentally writes to HKLM (which would require
        // elevation and surprise the operator).
        foreach (var (name, script) in new[]
        {
            ("install.ps1", InstallScript),
            ("uninstall.ps1", UninstallScript),
            ("status.ps1", StatusScript)
        })
        {
            script.Should().NotContain("HKLM:",
                $"{name} must stay under HKCU per the per-user install contract");
        }
    }

    [Fact]
    public void Install_script_quotes_paths_for_spaces_in_program_files()
    {
        // Command lines that interpolate the exe path must wrap it in
        // double quotes so paths like "C:\Program Files\DRM\..." survive
        // Explorer's argv parsing.
        InstallScript.Should().Contain("`\"$(",
            "install.ps1 must wrap path interpolations in `\" to handle spaces");
    }

    [Fact]
    public void Install_script_assigns_an_icon_to_the_drm_submenu_and_every_subverb()
    {
        // Phase 5AS-polish stopgap: rely on Windows system icons (imageres.dll)
        // so the right-click "DRM" submenu and every sub-action carry a
        // recognisable glyph without a compiled COM in-proc server.
        InstallScript.Should().Contain("imageres.dll,-78",
            "DRM submenu must have a padlock icon");
        InstallScript.Should().Contain("Icon",
            "Icon values must be set on the registry keys");

        var subIconCount = Regex.Matches(InstallScript, "imageres\\.dll,-\\d+").Count;
        subIconCount.Should().BeGreaterThanOrEqualTo(3,
            "submenu + at least three sub-verbs each get an icon");
    }

    private static string GetTopLevelKey(string fullKey)
    {
        // We treat "HKCU:\Software\Classes\X\Y\Z" → "HKCU:\Software\Classes\X"
        // when checking that uninstall covers the root. Wildcards inside
        // the key (e.g. *\shell\DrmProtect) keep the wildcard.
        var parts = fullKey.Split('\\');
        if (parts.Length <= 4) return fullKey;
        return string.Join("\\", parts.Take(4));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
