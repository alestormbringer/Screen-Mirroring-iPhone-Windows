<#
.SYNOPSIS
    Shows the latest iPhone Mirror logs and opens their folder.

.DESCRIPTION
    Logs are written to %TEMP%\iPhoneMirror. Packaged apps may have their AppData writes
    redirected to %LOCALAPPDATA%\Packages\<package>\LocalCache, so that location is
    searched too.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\Open-Logs.ps1
#>
[CmdletBinding()]
param(
    [int]$Last = 5
)

$candidates = @(Join-Path $env:TEMP 'iPhoneMirror')
$candidates += Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Directory -Filter 'iPhoneMirror.HidProbe_*' -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ChildItem $_.FullName -Recurse -Directory -Filter 'iPhoneMirror' -ErrorAction SilentlyContinue } |
    ForEach-Object { $_.FullName }

$found = $false
foreach ($dir in ($candidates | Select-Object -Unique)) {
    if (-not (Test-Path $dir)) { continue }
    $found = $true
    Write-Host "== $dir" -ForegroundColor Cyan
    Get-ChildItem $dir -File | Sort-Object LastWriteTime -Descending | Select-Object -First $Last |
        Format-Table LastWriteTime, Length, Name -AutoSize | Out-String | Write-Host
    Start-Process explorer.exe $dir
}

if (-not $found) {
    Write-Host 'No log folder found yet. Run HidProbe or tools\Check-BluetoothAdapter.ps1 first.' -ForegroundColor Yellow
}
