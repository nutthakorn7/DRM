# Desktop Shell Integration

This folder contains current user Windows shell integration for the DRM desktop client.

The registration script writes under `HKCU:\Software\Classes` only. It does not require machine-wide registry changes, and it does not make the tray protect or viewer open files automatically without showing their normal UI.

## Register

Run PowerShell with paths to the built or installed desktop binaries:

```powershell
.\register-shell-integration.ps1 `
  -TrayPath "C:\Program Files\EnterpriseDRM\Drm.Agent.Tray.Windows.exe" `
  -ViewerPath "C:\Program Files\EnterpriseDRM\Drm.Viewer.Windows.exe" `
  -ServerUrl "https://drm.example.local" `
  -ClientApiKey "drm_client_..." `
  -TenantId "<tenant-guid>" `
  -UserId "<user-guid>" `
  -DeviceId "<device-guid>" `
  -DeviceSecret "<device-secret-from-admin-provisioning>"
```

This registers:

- `.drmx` files to open the viewer with `--open "%1"`
- a `Protect CAD file (internal)` context menu for files that opens the tray with `--quick-protect "%1"`
- optional current-user machine config under `HKCU:\Software\zcrDRM` so protect/open flows do not ask for a client key, GUIDs, or device secret

## Unregister

```powershell
.\register-shell-integration.ps1 -Unregister
```

Unregister removes the current user `.drmx` association and `Protect with DRM` context menu keys.
