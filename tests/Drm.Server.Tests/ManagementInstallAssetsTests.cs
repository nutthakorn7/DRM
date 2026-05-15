using System.Text.Json;
using FluentAssertions;

namespace Drm.Server.Tests;

public sealed class ManagementInstallAssetsTests
{
    [Fact]
    public void OnPrem_example_config_contains_required_management_settings()
    {
        var configPath = Path.Combine(FindRepositoryRoot(), "deploy", "management", "appsettings.onprem.example.json");

        File.Exists(configPath).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        root.GetProperty("Drm").GetProperty("Mode").GetString().Should().Be("OnPrem");
        root.GetProperty("Drm")
            .GetProperty("KeyWrapping")
            .GetProperty("MasterKeyBase64")
            .GetString()
            .Should()
            .Be("REPLACE_WITH_32_BYTE_BASE64_MASTER_KEY");
        root.GetProperty("Drm")
            .GetProperty("Security")
            .GetProperty("AdminApiKey")
            .GetString()
            .Should()
            .Be("REPLACE_WITH_ADMIN_API_KEY");
        root.GetProperty("Drm")
            .GetProperty("Security")
            .GetProperty("ClientApiKey")
            .GetString()
            .Should()
            .Be("REPLACE_WITH_CLIENT_API_KEY");
        root.GetProperty("ConnectionStrings")
            .GetProperty("DrmDb")
            .GetString()
            .Should()
            .Be("Data Source=/var/lib/drm-management/drm-server.db");
        root.GetProperty("Kestrel")
            .GetProperty("Endpoints")
            .GetProperty("Http")
            .GetProperty("Url")
            .GetString()
            .Should()
            .Be("http://0.0.0.0:5080");
    }

    [Fact]
    public void Start_script_contains_required_operational_safeguards()
    {
        var scriptPath = Path.Combine(FindRepositoryRoot(), "deploy", "management", "start-management.sh");

        File.Exists(scriptPath).Should().BeTrue();
        var script = File.ReadAllText(scriptPath);

        script.Should().StartWith("#!/usr/bin/env bash");
        script.Should().Contain("set -euo pipefail");
        script.Should().Contain("DRM_DATA_DIR");
        script.Should().Contain("mkdir -p");
        script.Should().Contain("DRM_KEY_WRAPPING_MASTER_KEY_BASE64");
        script.Should().Contain("DRM_ADMIN_API_KEY");
        script.Should().Contain("DRM_CLIENT_API_KEY");
        script.Should().Contain("Drm__KeyWrapping__MasterKeyBase64");
        script.Should().Contain("Drm__Security__AdminApiKey");
        script.Should().Contain("Drm__Security__ClientApiKey");
        script.Should().Contain("exit 2");
        script.Should().Contain("Drm.Server.dll");
        script.Should().Contain("Drm.Server.csproj");
    }

    [Fact]
    public void Desktop_shell_registration_script_contains_user_scope_associations()
    {
        var scriptPath = Path.Combine(FindRepositoryRoot(), "deploy", "desktop", "register-shell-integration.ps1");

        File.Exists(scriptPath).Should().BeTrue();
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("HKCU:\\Software\\Classes");
        script.Should().Contain(".drmx");
        script.Should().Contain("EnterpriseDRM.ProtectedFile");
        script.Should().Contain("Protect with DRM");
        script.Should().Contain("--protect");
        script.Should().Contain("--open");
        script.Should().Contain("\"%1\"");
        script.Should().Contain("Remove-Item");
    }

    [Fact]
    public void Desktop_shell_integration_readme_documents_register_and_unregister()
    {
        var readmePath = Path.Combine(FindRepositoryRoot(), "deploy", "desktop", "README.md");

        File.Exists(readmePath).Should().BeTrue();
        var readme = File.ReadAllText(readmePath);

        readme.Should().Contain("register-shell-integration.ps1");
        readme.Should().Contain("-TrayPath");
        readme.Should().Contain("-ViewerPath");
        readme.Should().Contain("-Unregister");
        readme.Should().Contain("current user");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Drm.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
