<#
.SYNOPSIS
    Builds HidProbe and registers it as a development MSIX package (package identity),
    which the Windows BLE GATT server needs to really publish the HID service.

.DESCRIPTION
    1. checks the .NET SDK and Windows Developer Mode
    2. dotnet build (the output folder is a loose MSIX layout with AppxManifest.xml)
    3. removes any previous registration and runs Add-AppxPackage -Register
    4. with -Launch, starts the packaged app (never start bin\...\HidProbe.exe directly:
       it would run WITHOUT package identity)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\Build-HidProbe.ps1 -Launch
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$packageName = 'iPhoneMirror.HidProbe'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\HidProbe\HidProbe.csproj'
$outDir = Join-Path $repoRoot "src\HidProbe\bin\$Configuration\net8.0-windows10.0.19041.0"

if ($PSVersionTable.PSEdition -eq 'Core') {
    # The Appx cmdlets are not reliable in PowerShell 7; use the Windows PowerShell module.
    Import-Module Appx -UseWindowsPowerShell -WarningAction SilentlyContinue
}

Write-Host '== 1/4 Checking prerequisites' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.8   then open a new terminal.'
}
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^(8|9|1\d)\.' })) {
    throw ".NET SDK 8 or later not found (found: $($sdks -join ', ')). Install it with:  winget install Microsoft.DotNet.SDK.8"
}
$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) {
    Start-Process 'ms-settings:developers'
    throw 'Developer Mode is OFF. Turn on "Developer Mode" in the Settings page that just opened, then run this script again.'
}
if (Get-Process -Name HidProbe -ErrorAction SilentlyContinue) {
    throw 'HidProbe is running. Close it first (its window, or: Stop-Process -Name HidProbe).'
}

Write-Host "== 2/4 Building ($Configuration)" -ForegroundColor Cyan
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed (exit code $LASTEXITCODE)."
}
$manifest = Join-Path $outDir 'AppxManifest.xml'
if (-not (Test-Path (Join-Path $outDir 'HidProbe.exe')) -or -not (Test-Path $manifest)) {
    throw "Build output incomplete: expected HidProbe.exe and AppxManifest.xml in $outDir"
}

Write-Host '== 3/4 Registering the development package' -ForegroundColor Cyan
$existing = Get-AppxPackage -Name $packageName
if ($existing) {
    Write-Host "Removing previous registration $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}
Add-AppxPackage -Register $manifest
$pkg = Get-AppxPackage -Name $packageName
if (-not $pkg) {
    throw 'Registration did not produce a package. See the error above.'
}
Write-Host "Registered : $($pkg.PackageFullName)" -ForegroundColor Green
Write-Host "Location   : $($pkg.InstallLocation)"

$appId = "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
Write-Host '== 4/4 Ready' -ForegroundColor Cyan
Write-Host 'Start the app (with package identity) in one of these ways:'
Write-Host '  - Start menu: "iPhone Mirror HID Probe"'
Write-Host '  - terminal  : hidprobe'
Write-Host "  - terminal  : Start-Process '$appId'"

if ($Launch) {
    Start-Process $appId
    Write-Host 'HidProbe started.' -ForegroundColor Green
}
