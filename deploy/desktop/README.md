# Desktop Shell Integration

This folder contains current user Windows shell integration for the DRM desktop client.

The registration script writes under `HKCU:\Software\Classes` only. It does not require machine-wide registry changes, and it does not make the tray protect or viewer open files automatically without showing their normal UI.

## Register

Run PowerShell with paths to the built or installed desktop binaries:

```powershell
.\register-shell-integration.ps1 `
  -TrayPath "C:\Program Files\EnterpriseDRM\Drm.Agent.Tray.Windows.exe" `
  -ViewerPath "C:\Program Files\EnterpriseDRM\Drm.Viewer.Windows.exe"
```

This registers:

- `.drmx` files to open the viewer with `--open "%1"`
- a `Protect with DRM` context menu for files that opens the tray with `--protect "%1"`

## Unregister

```powershell
.\register-shell-integration.ps1 -Unregister
```

Unregister removes the current user `.drmx` association and `Protect with DRM` context menu keys.
