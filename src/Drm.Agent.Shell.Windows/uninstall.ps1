#requires -Version 5.1
<#
.SYNOPSIS
    Remove the DRM Explorer right-click menus and file associations
    installed by install.ps1.

.DESCRIPTION
    Safe to run even if install.ps1 was never executed — missing keys are
    skipped silently.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = "Stop"

$keysToRemove = @(
    "HKCU:\Software\Classes\*\shell\DrmProtect",
    "HKCU:\Software\Classes\CommandStore\shell\Drm.QuickSend",
    "HKCU:\Software\Classes\CommandStore\shell\Drm.Protect",
    "HKCU:\Software\Classes\CommandStore\shell\Drm.TransparentProtect",
    "HKCU:\Software\Classes\DRM.ProtectedFile.1",
    "HKCU:\Software\Classes\DRM.SecureContainer.1"
)

# Note: we intentionally do NOT remove the .drmx / .drmcontainer
# extension keys themselves (HKCU:\Software\Classes\.drmx), because the
# user may have other apps registered for them. Removing the ProgId is
# enough to drop our association.

foreach ($key in $keysToRemove) {
    if (Test-Path $key) {
        if ($PSCmdlet.ShouldProcess($key, "Remove-Item -Recurse")) {
            Remove-Item -Path $key -Recurse -Force
            Write-Host "  Removed $key"
        }
    }
}

Write-Host ""
Write-Host "✓ DRM shell integration removed."
Write-Host ""
Write-Host "If a stale right-click entry still appears, sign out and back in,"
Write-Host "or restart Explorer:   taskkill /im explorer.exe /f && explorer.exe"
