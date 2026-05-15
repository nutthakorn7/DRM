param(
    [string]$TrayPath,
    [string]$ViewerPath,
    [string]$ProgId = "EnterpriseDRM.ProtectedFile",
    [switch]$Unregister
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$classesRoot = "HKCU:\Software\Classes"
$protectedExtensionKey = Join-Path $classesRoot ".drmx"
$progIdKey = Join-Path $classesRoot $ProgId
$openCommandKey = Join-Path $progIdKey "shell\open\command"
$protectShellKey = Join-Path $classesRoot "*\shell\EnterpriseDRMProtect"
$protectCommandKey = Join-Path $protectShellKey "command"

if ($Unregister) {
    Remove-Item -Path $protectedExtensionKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $progIdKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $protectShellKey -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "DRM shell integration removed for the current user."
    return
}

if ([string]::IsNullOrWhiteSpace($TrayPath) -or -not (Test-Path -LiteralPath $TrayPath)) {
    throw "TrayPath must point to Drm.Agent.Tray.Windows.exe."
}

if ([string]::IsNullOrWhiteSpace($ViewerPath) -or -not (Test-Path -LiteralPath $ViewerPath)) {
    throw "ViewerPath must point to Drm.Viewer.Windows.exe."
}

$trayCommand = '"{0}" --protect "%1"' -f $TrayPath
$viewerCommand = '"{0}" --open "%1"' -f $ViewerPath

New-Item -Path $protectedExtensionKey -Force | Out-Null
Set-Item -Path $protectedExtensionKey -Value $ProgId

New-Item -Path $progIdKey -Force | Out-Null
Set-Item -Path $progIdKey -Value "Enterprise DRM protected file"

New-Item -Path $openCommandKey -Force | Out-Null
Set-Item -Path $openCommandKey -Value $viewerCommand

New-Item -Path $protectShellKey -Force | Out-Null
Set-Item -Path $protectShellKey -Value "Protect with DRM"
Set-ItemProperty -Path $protectShellKey -Name "Icon" -Value $TrayPath

New-Item -Path $protectCommandKey -Force | Out-Null
Set-Item -Path $protectCommandKey -Value $trayCommand

Write-Host "DRM shell integration registered for the current user."
