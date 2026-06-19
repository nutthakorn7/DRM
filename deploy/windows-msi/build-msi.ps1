<#
.SYNOPSIS
    Build the zcrDRM Agent MSI from this directory.

.DESCRIPTION
    Publishes Drm.Agent.Tray.Windows, Drm.Viewer.Windows, and
    Drm.Agent.Service.Windows as
    self-contained .NET 10 win-x64 binaries, merges them into one
    install folder, then invokes the WiX 4 toolset to produce
    zcrdrm-agent.msi.

    The MSI bakes the production server URL (https://drm.zcr.ai) into
    HKLM\SOFTWARE\zcrDRM\ServerUrl and registers the .drmx file
    association plus the "Protect CAD file (internal)" right-click menu.

.PARAMETER OutputPath
    Where to write the final MSI. Defaults to .\zcrdrm-agent.msi
    in the current directory.

.PARAMETER Version
    Override the MSI ProductVersion (e.g. for CI builds that tag
    feature branches). Defaults to whatever is in Product.wxs.

.EXAMPLE
    # Standard build
    .\build-msi.ps1

.EXAMPLE
    # CI build with versioned output
    .\build-msi.ps1 -OutputPath "artifacts\zcrdrm-agent-1.6.1.msi"

.NOTES
    Prerequisites on the build machine:
      - Windows 10/11 or Windows Server (msiexec is Windows-only)
      - .NET 10 SDK
      - WiX 4 toolset:  dotnet tool install --global wix
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "zcrdrm-agent.msi",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir
try {
    # ------------------------------------------------------------------
    # 1. Resolve repo root and project paths
    # ------------------------------------------------------------------
    # This script lives at deploy/windows-msi/, so the repo root is two
    # levels up. Resolve it via path arithmetic rather than `git
    # rev-parse` so the script works in a tarball / non-git checkout.
    $repoRoot = (Resolve-Path "$scriptDir\..\..").Path
    $trayProject    = Join-Path $repoRoot "src\Drm.Agent.Tray.Windows\Drm.Agent.Tray.Windows.csproj"
    $viewerProject  = Join-Path $repoRoot "src\Drm.Viewer.Windows\Drm.Viewer.Windows.csproj"
    $serviceProject = Join-Path $repoRoot "src\Drm.Agent.Service.Windows\Drm.Agent.Service.Windows.csproj"
    $publishDir     = Join-Path $scriptDir "publish\agent"
    $servicePayloadDir = Join-Path $scriptDir "publish\service"

    Write-Host ""
    Write-Host "==== zcrDRM Agent MSI build ====" -ForegroundColor Cyan
    Write-Host "  Repo root:    $repoRoot"
    Write-Host "  Publish dir:  $publishDir"
    Write-Host "  Output:       $OutputPath"
    Write-Host ""

    # ------------------------------------------------------------------
    # 2. Sanity-check tool prerequisites
    # ------------------------------------------------------------------
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet SDK not on PATH. Install .NET 10 SDK from https://dot.net/."
    }
    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        # WiX 5+ required: Product.wxs uses <Files Include="**\*"/>
        # which lands in WiX 5 (WiX 4 errors with WIX0005 here).
        throw "WiX 5 not on PATH. Install with: dotnet tool install --global wix --version 5.0.2"
    }

    # The Util extension provides util:PermissionEx, used by Product.wxs to
    # ACL the DeviceSecret registry key (PR #51 hardening). Adding it is
    # idempotent — a no-op if it is already present in the global cache.
    Write-Host "Ensuring WiX Util extension is available..." -ForegroundColor Yellow
    & wix extension add -g WixToolset.Util.wixext/5.0.2
    if ($LASTEXITCODE -ne 0) { throw "Failed to add WiX Util extension." }

    # ------------------------------------------------------------------
    # 3. Clean previous publish output (avoid stale files in the MSI)
    # ------------------------------------------------------------------
    if (Test-Path $publishDir) {
        Write-Host "Cleaning previous publish output..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force $publishDir
    }
    if (Test-Path $servicePayloadDir) {
        Remove-Item -Recurse -Force $servicePayloadDir
    }

    # ------------------------------------------------------------------
    # 4. Publish tray (self-contained — no .NET runtime install needed
    #    on the end-user machine)
    # ------------------------------------------------------------------
    Write-Host "Publishing Drm.Agent.Tray.Windows (self-contained, win-x64)..." -ForegroundColor Yellow
    dotnet publish $trayProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed." }

    # ------------------------------------------------------------------
    # 5. Publish viewer into the SAME folder so both ship together.
    #    --no-self-contained for the second publish would resurrect the
    #    runtime-prereq problem, so we publish self-contained too — the
    #    runtime files overwrite-merge cleanly (identical content).
    # ------------------------------------------------------------------
    Write-Host "Publishing Drm.Viewer.Windows (self-contained, win-x64)..." -ForegroundColor Yellow
    dotnet publish $viewerProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Viewer publish failed." }

    Write-Host "Publishing Drm.Agent.Service.Windows (self-contained, win-x64)..." -ForegroundColor Yellow
    dotnet publish $serviceProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Service publish failed." }

    # ------------------------------------------------------------------
    # 6. Sanity-check the publish output contains the EXE files the
    #    MSI is going to reference. Catches "publish succeeded but
    #    nothing landed in the folder" early — that failure mode is
    #    confusing to debug at MSI-build time.
    # ------------------------------------------------------------------
    $trayExe    = Join-Path $publishDir "Drm.Agent.Tray.Windows.exe"
    $viewerExe  = Join-Path $publishDir "Drm.Viewer.Windows.exe"
    $serviceExe = Join-Path $publishDir "Drm.Agent.Service.Windows.exe"
    if (-not (Test-Path $trayExe))    { throw "Expected $trayExe after publish." }
    if (-not (Test-Path $viewerExe))  { throw "Expected $viewerExe after publish." }
    if (-not (Test-Path $serviceExe)) { throw "Expected $serviceExe after publish." }

    # WiX must place ServiceInstall in the same component as the service
    # executable. Move only the EXE out of the bulk-harvested folder so
    # Product.wxs can own it explicitly while the service's DLL/deps/
    # runtimeconfig remain in INSTALLFOLDER with the rest of the payload.
    New-Item -ItemType Directory -Force -Path $servicePayloadDir | Out-Null
    Copy-Item -Force $serviceExe (Join-Path $servicePayloadDir "Drm.Agent.Service.Windows.exe")
    Remove-Item -Force $serviceExe

    $fileCount = (Get-ChildItem -Recurse $publishDir).Count
    Write-Host "  Publish OK ($fileCount files)" -ForegroundColor Green

    # ------------------------------------------------------------------
    # 7. Build the MSI
    # ------------------------------------------------------------------
    Write-Host "Building MSI with WiX..." -ForegroundColor Yellow
    $wixArgs = @("build", "Product.wxs", "-arch", "x64", "-ext", "WixToolset.Util.wixext", "-out", $OutputPath)
    if ($Version) {
        # WiX 4 lets us override variables on the command line via -d
        $wixArgs += @("-d", "ProductVersion=$Version")
    }
    & wix @wixArgs
    if ($LASTEXITCODE -ne 0) { throw "wix build failed." }

    # ------------------------------------------------------------------
    # 8. Report MSI size
    # ------------------------------------------------------------------
    $msiPath = (Resolve-Path $OutputPath).Path
    $msiSize = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK Built $msiPath ($msiSize MB)" -ForegroundColor Green
    Write-Host ""
} finally {
    Pop-Location
}
