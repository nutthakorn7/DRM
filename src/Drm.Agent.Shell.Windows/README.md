# Drm.Agent.Shell.Windows — Explorer right-click integration

This is **not a compiled component**. It is a pair of PowerShell scripts that
register Windows Explorer right-click context-menu entries pointing at the
already-installed `Drm.Agent.Tray.Windows.exe` (protect / send) and
`Drm.Viewer.Windows.exe` (open). The scripts use plain registry keys, so:

- No COM, no C++, no compiled DLL — pure `*.reg` / `*.ps1`
- No admin rights required — writes under `HKCU\Software\Classes`, per-user
- No managed-DLL-in-Explorer issue — Explorer launches the exes out-of-process

## What gets registered

Right-clicking **any file** in Explorer adds a top-level "DRM" submenu with
three actions:

| Menu entry | Launches | Argument |
|---|---|---|
| **DRM → Quick send (recommended)** | `Drm.Agent.Tray.Windows.exe` | `--quick-protect "<file>"` |
| **DRM → Protect (advanced)** | `Drm.Agent.Tray.Windows.exe` | `--protect "<file>"` |
| **DRM → Transparent protect (preserve extension)** | `Drm.Agent.Tray.Windows.exe` | `--transparent-protect "<file>"` |

Double-clicking a `.drmx` or `.drmcontainer` file launches:

| Extension | Launches | Argument |
|---|---|---|
| `.drmx` | `Drm.Viewer.Windows.exe` | `--open "<file>"` |
| `.drmcontainer` | `Drm.Viewer.Windows.exe` | `--open "<file>"` |

## Install

```powershell
# As the end-user (no admin rights needed):
cd src\Drm.Agent.Shell.Windows
.\install.ps1 -TrayExe "C:\Program Files\DRM\Drm.Agent.Tray.Windows.exe" `
              -ViewerExe "C:\Program Files\DRM\Drm.Viewer.Windows.exe"

# Verify
.\status.ps1
```

## Uninstall

```powershell
.\uninstall.ps1
```

## Sanity check before deployment

```powershell
# Lint the install script — refuses to write any key if any required path
# fails -Test
.\install.ps1 -TrayExe "C:\Program Files\DRM\Drm.Agent.Tray.Windows.exe" `
              -ViewerExe "C:\Program Files\DRM\Drm.Viewer.Windows.exe" `
              -WhatIf
```

## Why per-user (`HKCU`) instead of `HKLM`?

- A single regular user can install without IT involvement
- Easier to roll back per user (clear policy)
- Multiple users on the same machine each get the right binary paths
- If your org prefers machine-wide install, replace `HKCU` with `HKLM` in
  `install.ps1` and run elevated

## Why a submenu and not a top-level entry?

Top-level entries pollute the right-click menu. The Microsoft-recommended
`SubCommands` / `ExtendedSubCommandsKey` registry pattern collapses our
three entries under one "DRM" group, matching the FinalCode catalogue
pattern (single "FinalCode" entry with sub-actions).

## Limitations

- No custom icon next to the menu entry (requires COM)
- Will not auto-update when `Drm.Agent.Tray.Windows.exe` moves — rerun
  `install.ps1` if you change paths
- Windows 10/11 only (verified — older Windows uses different submenu syntax)
