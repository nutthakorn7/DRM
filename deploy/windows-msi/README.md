# zcrDRM Agent — Windows MSI installer

This directory builds `zcrdrm-agent.msi`, the single-file Windows
installer we ship to end-user laptops. Double-click → Next → Done —
no PowerShell, no GUIDs, no manual registry steps.

## What the MSI does

| | |
|---|---|
| **Install location** | `C:\Program Files\zcrDRM\` |
| **Bundled apps** | `Drm.Agent.Tray.Windows.exe` + `Drm.Viewer.Windows.exe` + `Drm.Agent.Service.Windows.exe` (self-contained .NET 10, no runtime prereq) |
| **Server URL** | Baked into `HKLM\SOFTWARE\zcrDRM\ServerUrl = https://drm.zcr.ai` |
| **Machine config** | Optional MSI properties `CLIENTAPIKEY`, `TENANTID`, `USERID`, `DEVICEID`, `DEVICESECRET` are written under `HKLM\SOFTWARE\zcrDRM` so protect/open flows do not ask the user for secrets or GUIDs |
| **File association** | `.drmx` and `.drmcontainer` → open in zcrDRM Viewer |
| **Right-click menu** | "Protect CAD file (internal)" → tray `--quick-protect` flow |
| **Device posture** | Windows service `zcrDRMAgent` starts automatically and reports AD-domain posture with the provisioned device secret |
| **Start Menu** | "zcrDRM Agent" and "zcrDRM Viewer" shortcuts |

## Build from a Windows machine

```powershell
# One-time setup
# WiX 5 (not WiX 4) — the bulk <Files Include="**\*"/> harvester is a
# WiX 5+ feature. Same XML namespace as WiX 4, fully source-compatible.
dotnet tool install --global wix --version 5.0.2

# Build
cd deploy\windows-msi
.\build-msi.ps1
# → produces zcrdrm-agent.msi (~80 MB self-contained)
```

## Build from CI

The `msi-build` job in `.github/workflows/ci.yml` runs on
`windows-latest`, produces the MSI, then runs `msiexec /i /qn` and
asserts that all the registry keys + file paths landed. Download the
`zcrdrm-agent-msi` artifact from the workflow run page.

## Verify a built MSI by hand

```powershell
# Install silently
msiexec /i zcrdrm-agent.msi /qn /l*v install.log

# Provisioned install for an internal AD demo
msiexec /i zcrdrm-agent.msi /qn CLIENTAPIKEY="drm_client_..." TENANTID="<tenant-guid>" USERID="<user-guid>" DEVICEID="<device-guid>" DEVICESECRET="<device-secret-from-admin-provisioning>"

# Spot-check
Test-Path "C:\Program Files\zcrDRM\Drm.Agent.Tray.Windows.exe"
Get-Service zcrDRMAgent
Get-ItemProperty HKLM:\SOFTWARE\zcrDRM
Get-Item HKCR:\.drmx
Get-Item 'HKCR:\*\shell\zcrDRMProtect'

# Uninstall
msiexec /x zcrdrm-agent.msi /qn
```

## Code-signing

**Pre-demo (current state)**: MSI is **unsigned**. SmartScreen will
warn on the first install of every machine. Strategy for the
upcoming customer demo: engineer pre-installs the MSI on the demo
laptop ahead of time so the customer never sees SmartScreen.

**Post-demo (TODO)**: buy a Sectigo code-signing cert (standard
~$200/yr, EV ~$300-400/yr) and add `signtool sign` to the build
script. Tracked separately.

## Schema overview

`Product.wxs` declares these main components:

1. `AgentBinaries` — all files under `publish\agent\**` except the service EXE, which is owned by the service component
2. `ServerConfigRegistry` — `HKLM\SOFTWARE\zcrDRM\*`
3. `DrmxFileAssociation` — `.drmx` → `zcrDRM.ProtectedFile.1`
4. `DrmContainerFileAssociation` — `.drmcontainer` → `zcrDRM.SecureContainer.1`
5. `ProtectShellMenu` — `HKCR\*\shell\zcrDRMProtect` (top-level menu)
6. `AgentPostureService` — installs and starts `zcrDRMAgent`
7. `TrayStartMenuShortcut` — Start Menu shortcut

Every `RegistryValue` is its component's `KeyPath` so MSI uninstall
removes them all automatically — no separate uninstaller logic needed.

## Upgrade behaviour

`<MajorUpgrade>` is on. Installing a newer MSI silently uninstalls
the older one first. Downgrades are blocked with an error dialog.
The `UpgradeCode` GUID in `Product.wxs` is stable across versions —
**never regenerate it**.
