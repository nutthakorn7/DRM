# Phase 5M Desktop Shell Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows desktop shell integration assets for right-click protection and `.drmx` viewer association.

**Architecture:** Provide a current-user PowerShell registration script under `deploy/desktop/` that writes `HKCU:\Software\Classes` registry entries instead of requiring machine-wide admin registry writes. The script registers a `Protect with DRM` context menu command that launches the tray app with `--protect "%1"` and associates `.drmx` files with the viewer command. Tray and viewer apps parse command-line paths to prefill their file fields without auto-protecting or auto-opening before identity/server inputs are supplied.

**Tech Stack:** PowerShell, WPF tray/viewer, .NET 10 tests for install assets.

---

## File Structure

- Modify `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs`: add deploy/desktop asset contract tests.
- Create `deploy/desktop/register-shell-integration.ps1`: HKCU file association/context-menu registration script.
- Create `deploy/desktop/README.md`: operator instructions for registering and removing shell integration.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`: parse `--protect <path>` and prefill `SourcePathBox`.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: parse `--open <path>` or a direct `.drmx` path and prefill `ProtectedPathBox`.
- Modify `README.md`: document Phase 5M shell integration.

## Tasks

### Task 1: Desktop Install Asset Contract

- [x] **Step 1: Write failing install asset tests**

Add tests to `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs`:

```csharp
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
```

- [x] **Step 2: Run failing install asset tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Desktop_shell_registration_script_contains_user_scope_associations|Desktop_shell_integration_readme_documents_register_and_unregister"
```

Expected: FAIL because `deploy/desktop` assets do not exist.

- [x] **Step 3: Add desktop shell integration assets**

Create `deploy/desktop/register-shell-integration.ps1` with parameters:

- `-TrayPath`
- `-ViewerPath`
- `-ProgId` default `EnterpriseDRM.ProtectedFile`
- `-Unregister`

The script must:

- write only under `HKCU:\Software\Classes`;
- require `TrayPath` and `ViewerPath` when registering;
- register `.drmx` to the prog ID;
- register `shell\open\command` as `"<ViewerPath>" --open "%1"`;
- register `*\shell\EnterpriseDRMProtect\command` as `"<TrayPath>" --protect "%1"`;
- remove those keys when `-Unregister` is supplied.

Create `deploy/desktop/README.md` with register/unregister examples.

- [x] **Step 4: Run passing install asset tests**

Run the same filtered server test command. Expected: PASS.

### Task 2: Tray and Viewer Argument Prefill

- [x] **Step 1: Implement tray prefill**

Update `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`:

- in the constructor, call a helper that reads `Environment.GetCommandLineArgs()`;
- when it sees `--protect <path>`, set `SourcePathBox.Text` to `<path>`;
- do not start protection automatically.

- [x] **Step 2: Implement viewer prefill**

Update `src/Drm.Viewer.Windows/MainWindow.xaml.cs`:

- in the constructor, prefill `ProtectedPathBox.Text` from `--open <path>`;
- also accept a direct first `.drmx` argument for Windows file association fallback;
- do not open automatically.

- [x] **Step 3: Update README**

Add Phase 5M notes for current-user shell integration and manual review before automatic protect/open.

### Task 3: Verification and Commit

- [x] **Step 1: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all pass.

- [x] **Step 2: Commit**

Run:

```bash
git add README.md deploy/desktop src/Drm.Agent.Tray.Windows src/Drm.Viewer.Windows tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5m-desktop-shell-integration.md
git commit -m "feat: add desktop shell integration assets"
```

## Self-Review

- Spec coverage: Adds the visible desktop shell entry points called out in the approved design without making stealth or machine-wide changes.
- Security note: Shell commands only prefill UI paths; users still supply server/identity context and explicitly run protect/open.
- Placeholder scan: No TBD/TODO placeholders.
