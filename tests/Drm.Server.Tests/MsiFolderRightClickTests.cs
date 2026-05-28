using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Static validation that the WiX MSI exposes only the internal CAD protect
/// shell surface for the customer demo.
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
    private static readonly string ServiceProgram = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Agent.Service.Windows/Program.cs"));
    private static readonly string ViewerMain = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Viewer.Windows/MainWindow.xaml.cs"));

    [Fact]
    public void Wix_registers_single_internal_cad_file_verb()
    {
        ProductWxs.Should().Contain(@"*\shell\zcrDRMProtect");
        ProductWxs.Should().Contain("Protect CAD file (internal)");
        ProductWxs.Should().Contain("--quick-protect &quot;%1&quot;");
    }

    [Fact]
    public void Wix_does_not_register_external_or_folder_protect_surfaces()
    {
        ProductWxs.Should().NotContain("transparent-protect");
        ProductWxs.Should().NotContain("Protect (advanced)");
        ProductWxs.Should().NotContain("protect-folder");
        ProductWxs.Should().NotContain(@"Directory\shell\zcrDRMProtectFolder");
    }

    [Fact]
    public void Wix_main_feature_references_only_the_internal_cad_verb_component()
    {
        ProductWxs.Should().Contain(@"<ComponentRef Id=""ProtectShellMenu"" />");
        ProductWxs.Should().NotContain(@"<ComponentRef Id=""ProtectShellSubProtect"" />");
        ProductWxs.Should().NotContain(@"<ComponentRef Id=""ProtectShellSubTransparent"" />");
        ProductWxs.Should().NotContain(@"<ComponentRef Id=""ProtectFolderShellMenu"" />");
    }

    [Fact]
    public void Wix_provisions_desktop_no_password_machine_config()
    {
        ProductWxs.Should().Contain(@"HKLM\SOFTWARE\zcrDRM");
        ProductWxs.Should().Contain(@"Name=""ClientApiKey""");
        ProductWxs.Should().Contain(@"Name=""TenantId""");
        ProductWxs.Should().Contain(@"Name=""UserId""");
        ProductWxs.Should().Contain(@"Name=""DeviceId""");
        ProductWxs.Should().Contain(@"Name=""DeviceSecret""");
        ProductWxs.Should().Contain("[CLIENTAPIKEY]");
        ProductWxs.Should().Contain("[DEVICEID]");
        ProductWxs.Should().Contain("[DEVICESECRET]");
    }

    [Fact]
    public void Wix_installs_and_starts_posture_service()
    {
        ProductWxs.Should().Contain("Drm.Agent.Service.Windows.exe");
        ProductWxs.Should().Contain(@"<ServiceInstall Id=""AgentPostureServiceInstall""");
        ProductWxs.Should().Contain(@"Name=""zcrDRMAgent""");
        ProductWxs.Should().Contain(@"Start=""auto""");
        ProductWxs.Should().Contain(@"<ServiceControl Id=""AgentPostureServiceControl""");
        ProductWxs.Should().Contain(@"Start=""install""");
        ProductWxs.Should().Contain(@"<ComponentRef Id=""AgentPostureService"" />");
        ServiceProgram.Should().Contain("options.ServiceName = \"zcrDRMAgent\"");
    }

    [Fact]
    public void Viewer_asks_local_service_to_sign_device_unwrap_requests()
    {
        ViewerMain.Should().Contain("new LocalDeviceRequestSigner()");
        ViewerMain.Should().NotContain("desktopConfiguration.DeviceSecret");
    }

    [Fact]
    public void Tray_accepts_quick_protect_cli_flag_only_for_shell_protect()
    {
        TrayMain.Should().Contain("--quick-protect");
        TrayMain.Should().NotContain("--transparent-protect");
        TrayMain.Should().NotContain("--protect-folder");
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
