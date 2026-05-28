#requires -Version 5.1
<#
.SYNOPSIS
    Register DRM Explorer right-click menus and file associations.

.DESCRIPTION
    Writes per-user registry entries under HKCU\Software\Classes that add a
    "DRM" submenu with a single Protect CAD file (internal) action to the
    right-click menu for files, and associates .drmx / .drmcontainer files
    with the DRM viewer.

    No COM, no compilation, no admin rights required.

.PARAMETER TrayExe
    Absolute path to Drm.Agent.Tray.Windows.exe.

.PARAMETER ViewerExe
    Absolute path to Drm.Viewer.Windows.exe.

.PARAMETER WhatIf
    Validate paths and print what would be written without touching the
    registry.

.EXAMPLE
    .\install.ps1 `
        -TrayExe "C:\Program Files\DRM\Drm.Agent.Tray.Windows.exe" `
        -ViewerExe "C:\Program Files\DRM\Drm.Viewer.Windows.exe"
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayExe,
    [Parameter(Mandatory = $true)]
    [string]$ViewerExe
)

$ErrorActionPreference = "Stop"

function Assert-FileExists {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "$Label not found at: $Path"
    }
    return (Resolve-Path $Path).Path
}

$tray = Assert-FileExists -Path $TrayExe -Label "Tray executable"
$viewer = Assert-FileExists -Path $ViewerExe -Label "Viewer executable"

# Layout of the menu structure we are about to write.
$entries = @(
    @{
        Name        = "DrmProtect"
        DisplayName = "DRM"
        Subcommands = "Drm.QuickSend"
    }
)

$commands = @(
    @{ Verb = "Drm.QuickSend"; Label = "Protect CAD file (internal)"; Exe = $tray; Argument = "--quick-protect" }
)

$assocs = @(
    @{ Extension = ".drmx";         ProgId = "DRM.ProtectedFile.1";  FriendlyName = "DRM Protected File";  Exe = $viewer; Argument = "--open" },
    @{ Extension = ".drmcontainer"; ProgId = "DRM.SecureContainer.1"; FriendlyName = "DRM Secure Container"; Exe = $viewer; Argument = "--open" }
)

function Set-RegistryValue {
    param(
        [string]$Path,
        [string]$Name,
        [string]$Value,
        [string]$Type = "String"
    )
    if ($PSCmdlet.ShouldProcess("$Path :: $Name = $Value", "Set-ItemProperty")) {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
        }
        if ($Name) {
            New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType $Type -Force | Out-Null
        }
    } else {
        Write-Host "[WhatIf] $Path :: $Name = $Value"
    }
}

# Top-level "DRM" submenu attached to every file's right-click menu.
# The 'Subcommands' value takes a single semicolon-separated string of
# verb identifiers that resolve under HKCU\Software\Classes\CommandStore.
# 'Icon' references a Windows system icon (padlock from ImageRes.dll) so
# the right-click entry carries a recognisable lock glyph without
# requiring a compiled COM in-proc server.
$baseKey = "HKCU:\Software\Classes\*\shell\DrmProtect"
Set-RegistryValue -Path $baseKey -Name "MUIVerb" -Value "DRM"
Set-RegistryValue -Path $baseKey -Name "Subcommands" -Value (($commands | ForEach-Object { $_.Verb }) -join ";")
# Padlock icon (ImageRes.dll resource index -78 is the closed-lock badge
# used by Windows for protected items). Fallback path is always present
# on Windows 10/11.
Set-RegistryValue -Path $baseKey -Name "Icon" -Value "imageres.dll,-78"

foreach ($cmd in $commands) {
    $verbKey = "HKCU:\Software\Classes\CommandStore\shell\$($cmd.Verb)"
    Set-RegistryValue -Path $verbKey -Name "" -Value $cmd.Label
    Set-RegistryValue -Path $verbKey -Name "Icon" -Value "imageres.dll,-78"
    $cmdLine = "`"$($cmd.Exe)`" $($cmd.Argument) `"%1`""
    Set-RegistryValue -Path "$verbKey\command" -Name "" -Value $cmdLine
}

foreach ($assoc in $assocs) {
    $extKey = "HKCU:\Software\Classes\$($assoc.Extension)"
    Set-RegistryValue -Path $extKey -Name "" -Value $assoc.ProgId

    $progKey = "HKCU:\Software\Classes\$($assoc.ProgId)"
    Set-RegistryValue -Path $progKey -Name "" -Value $assoc.FriendlyName

    $openCmd = "`"$($assoc.Exe)`" $($assoc.Argument) `"%1`""
    Set-RegistryValue -Path "$progKey\shell\open\command" -Name "" -Value $openCmd
}

Write-Host ""
Write-Host "✓ DRM shell integration installed for current user."
Write-Host "  Right-click a CAD file → DRM → Protect CAD file (internal)"
Write-Host "  Double-click .drmx / .drmcontainer → opens in DRM Viewer"
Write-Host ""
Write-Host "If the new menus do not appear immediately, sign out and back in,"
Write-Host "or restart Explorer:   taskkill /im explorer.exe /f && explorer.exe"
