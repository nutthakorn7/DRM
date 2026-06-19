param(
    [string]$TrayPath,
    [string]$ViewerPath,
    [string]$ProgId = "EnterpriseDRM.ProtectedFile",
    [string]$ServerUrl,
    [string]$ClientApiKey,
    [string]$TenantId,
    [string]$UserId,
    [string]$DeviceId,
    [string]$DeviceSecret,
    [switch]$Unregister
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$classesRoot = "HKCU:\Software\Classes"
$configRoot = "HKCU:\Software\zcrDRM"
$protectedExtensionKey = Join-Path $classesRoot ".drmx"
$progIdKey = Join-Path $classesRoot $ProgId
$openCommandKey = Join-Path $progIdKey "shell\open\command"
$protectShellKey = Join-Path $classesRoot "*\shell\EnterpriseDRMProtect"
$protectCommandKey = Join-Path $protectShellKey "command"

if ($Unregister) {
    Remove-Item -Path $protectedExtensionKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $progIdKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $protectShellKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $configRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "DRM shell integration removed for the current user."
    return
}

if ([string]::IsNullOrWhiteSpace($TrayPath) -or -not (Test-Path -LiteralPath $TrayPath)) {
    throw "TrayPath must point to Drm.Agent.Tray.Windows.exe."
}

if ([string]::IsNullOrWhiteSpace($ViewerPath) -or -not (Test-Path -LiteralPath $ViewerPath)) {
    throw "ViewerPath must point to Drm.Viewer.Windows.exe."
}

$trayCommand = '"{0}" --quick-protect "%1"' -f $TrayPath
$viewerCommand = '"{0}" --open "%1"' -f $ViewerPath

New-Item -Path $protectedExtensionKey -Force | Out-Null
Set-Item -Path $protectedExtensionKey -Value $ProgId

New-Item -Path $progIdKey -Force | Out-Null
Set-Item -Path $progIdKey -Value "Enterprise DRM protected file"

New-Item -Path $openCommandKey -Force | Out-Null
Set-Item -Path $openCommandKey -Value $viewerCommand

New-Item -Path $protectShellKey -Force | Out-Null
Set-Item -Path $protectShellKey -Value "Protect CAD file (internal)"
Set-ItemProperty -Path $protectShellKey -Name "Icon" -Value $TrayPath

New-Item -Path $protectCommandKey -Force | Out-Null
Set-Item -Path $protectCommandKey -Value $trayCommand

if ($ServerUrl -or $ClientApiKey -or $TenantId -or $UserId -or $DeviceId -or $DeviceSecret) {
    New-Item -Path $configRoot -Force | Out-Null
    if ($ServerUrl) { New-ItemProperty -Path $configRoot -Name "ServerUrl" -Value $ServerUrl -PropertyType String -Force | Out-Null }
    if ($ClientApiKey) { New-ItemProperty -Path $configRoot -Name "ClientApiKey" -Value $ClientApiKey -PropertyType String -Force | Out-Null }
    if ($TenantId) { New-ItemProperty -Path $configRoot -Name "TenantId" -Value $TenantId -PropertyType String -Force | Out-Null }
    if ($UserId) { New-ItemProperty -Path $configRoot -Name "UserId" -Value $UserId -PropertyType String -Force | Out-Null }
    if ($DeviceId) { New-ItemProperty -Path $configRoot -Name "DeviceId" -Value $DeviceId -PropertyType String -Force | Out-Null }
    if ($DeviceSecret) { New-ItemProperty -Path $configRoot -Name "DeviceSecret" -Value $DeviceSecret -PropertyType String -Force | Out-Null }
}

Write-Host "DRM shell integration registered for the current user."
