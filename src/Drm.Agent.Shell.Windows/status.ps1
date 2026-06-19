#requires -Version 5.1
<#
.SYNOPSIS
    Report whether the DRM shell-integration entries are present and
    point at valid executables.
#>

$ErrorActionPreference = "Stop"

function Test-RegValue {
    param([string]$Path, [string]$Name = "")
    if (-not (Test-Path $Path)) { return $null }
    try {
        if ($Name) {
            return (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name
        } else {
            return (Get-ItemProperty -Path $Path -ErrorAction Stop)."(default)"
        }
    } catch {
        return $null
    }
}

function Report-Row {
    param([string]$Label, [string]$Detail, [bool]$Ok)
    $mark = if ($Ok) { "✓" } else { "✗" }
    $color = if ($Ok) { "Green" } else { "Yellow" }
    Write-Host ("  {0} {1,-44} {2}" -f $mark, $Label, $Detail) -ForegroundColor $color
}

Write-Host ""
Write-Host "DRM shell-integration status" -ForegroundColor Cyan
Write-Host "============================"

$submenu = Test-RegValue -Path "HKCU:\Software\Classes\*\shell\DrmProtect" -Name "MUIVerb"
Report-Row "Top-level 'DRM' submenu" ($submenu ?? "MISSING") ($null -ne $submenu)

foreach ($verb in "Drm.QuickSend") {
    $cmdKey = "HKCU:\Software\Classes\CommandStore\shell\$verb\command"
    $cmd = Test-RegValue -Path $cmdKey
    Report-Row "Verb $verb" ($cmd ?? "MISSING") ($null -ne $cmd)
}

foreach ($pair in @(@{Ext=".drmx"; Prog="DRM.ProtectedFile.1"}, @{Ext=".drmcontainer"; Prog="DRM.SecureContainer.1"})) {
    $extKey = "HKCU:\Software\Classes\$($pair.Ext)"
    $bound = Test-RegValue -Path $extKey
    $progCmd = Test-RegValue -Path "HKCU:\Software\Classes\$($pair.Prog)\shell\open\command"
    Report-Row "$($pair.Ext) → $($pair.Prog)" (($bound ?? "MISSING") + " | " + ($progCmd ?? "no command")) ($bound -eq $pair.Prog -and $null -ne $progCmd)
}

Write-Host ""
