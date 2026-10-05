<#
.SYNOPSIS
    Removes the HidProbe development package registration (the build folder is left untouched).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\Unregister-HidProbe.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    Import-Module Appx -UseWindowsPowerShell -WarningAction SilentlyContinue
}

$pkg = Get-AppxPackage -Name 'iPhoneMirror.HidProbe'
if (-not $pkg) {
    Write-Host 'HidProbe is not registered.'
    return
}

Get-Process -Name HidProbe -ErrorAction SilentlyContinue | Stop-Process
Remove-AppxPackage -Package $pkg.PackageFullName
Write-Host "Removed $($pkg.PackageFullName)" -ForegroundColor Green
