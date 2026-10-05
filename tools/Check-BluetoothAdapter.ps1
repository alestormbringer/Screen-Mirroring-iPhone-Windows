<#
.SYNOPSIS
    Phase 0, step 1: checks whether this PC's Bluetooth adapter can act as a BLE peripheral
    (required to emulate a Bluetooth LE mouse/keyboard) and collects chip/driver details.

.DESCRIPTION
    Read-only: it changes nothing on the system. Must run in Windows PowerShell 5.1
    (powershell.exe), because PowerShell 7 cannot call WinRT APIs directly.

    Output is printed and saved to %TEMP%\iPhoneMirror\adapter-check-<timestamp>.txt

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\Check-BluetoothAdapter.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

if ($PSVersionTable.PSEdition -eq 'Core') {
    Write-Host "This script needs Windows PowerShell 5.1 (WinRT access). Re-launching with powershell.exe..." -ForegroundColor Yellow
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}

$logDir = Join-Path $env:TEMP 'iPhoneMirror'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir ("adapter-check-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

function Out-Line {
    param([string]$Text = '', [ConsoleColor]$Color = [ConsoleColor]::Gray)
    Write-Host $Text -ForegroundColor $Color
    Add-Content -Path $logFile -Value $Text -Encoding UTF8
}

function Out-Section([string]$Title) {
    Out-Line ''
    Out-Line ("=== {0} ===" -f $Title) Cyan
}

# --- WinRT helpers -----------------------------------------------------------
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$script:asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
                   $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1

function Wait-WinRt($Operation, [Type]$ResultType) {
    $task = $script:asTaskGeneric.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $null = $task.Wait(-1)
    return $task.Result
}

function Get-SafeProperty($Object, [string]$Name) {
    try { $value = $Object.$Name } catch { $value = $null }
    if ($null -eq $value) { return 'n/a (not available on this Windows build)' }
    return $value
}

# --- Native BR/EDR state (Bluetooth Classic) --------------------------------
$nativeSource = @'
using System;
using System.Runtime.InteropServices;
public static class IPhoneMirrorBtNative {
    [DllImport("BluetoothAPIs.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothIsDiscoverable(IntPtr hRadio);
    [DllImport("BluetoothAPIs.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothIsConnectable(IntPtr hRadio);
}
'@
if (-not ('IPhoneMirrorBtNative' -as [Type])) { Add-Type -TypeDefinition $nativeSource }

Out-Line ("iPhone Mirror - Bluetooth adapter check ({0})" -f (Get-Date -Format 's')) White
Out-Line ("Log file: {0}" -f $logFile)

# --- Windows -------------------------------------------------------------------
Out-Section 'Windows'
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$build = [int]$cv.CurrentBuild
$product = if ($build -ge 22000) { $cv.ProductName -replace 'Windows 10', 'Windows 11' } else { $cv.ProductName }
Out-Line ("Edition : {0}" -f $product)
Out-Line ("Version : {0} (build {1}.{2})" -f $cv.DisplayVersion, $cv.CurrentBuild, $cv.UBR)
Out-Line ("Arch    : {0}" -f $env:PROCESSOR_ARCHITECTURE)

$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -eq 1) {
    Out-Line 'Developer Mode: ON (needed to register the test app package)' Green
} else {
    Out-Line 'Developer Mode: OFF -> enable it: Settings > System > For developers > Developer Mode' Yellow
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $sdks = (& dotnet --list-sdks) -join '; '
    Out-Line (".NET SDKs: {0}" -f ($(if ($sdks) { $sdks } else { 'none (only runtimes)' })))
} else {
    Out-Line '.NET SDK: not found -> install the .NET 8 SDK (winget install Microsoft.DotNet.SDK.8)' Yellow
}

# --- WinRT adapter ---------------------------------------------------------------
Out-Section 'Bluetooth adapter (WinRT)'
$peripheral = $null
try {
    $null = [Windows.Devices.Bluetooth.BluetoothAdapter, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $null = [Windows.Devices.Radios.Radio, Windows.Devices.Radios, ContentType = WindowsRuntime]
    $adapter = Wait-WinRt ([Windows.Devices.Bluetooth.BluetoothAdapter]::GetDefaultAsync()) ([Windows.Devices.Bluetooth.BluetoothAdapter])
    if ($null -eq $adapter) {
        Out-Line 'No Bluetooth adapter found (GetDefaultAsync returned null). Is Bluetooth turned on?' Red
    } else {
        $radio = Wait-WinRt ($adapter.GetRadioAsync()) ([Windows.Devices.Radios.Radio])
        $addrBytes = [BitConverter]::GetBytes([UInt64]$adapter.BluetoothAddress)[5..0]
        Out-Line ("Radio name                    : {0}" -f $radio.Name)
        Out-Line ("Radio state                   : {0}" -f $radio.State)
        Out-Line ("Address                       : {0}" -f (($addrBytes | ForEach-Object { $_.ToString('X2') }) -join ':'))
        Out-Line ("DeviceId                      : {0}" -f $adapter.DeviceId)
        Out-Line ("IsLowEnergySupported          : {0}" -f $adapter.IsLowEnergySupported)
        Out-Line ("IsClassicSupported            : {0}" -f $adapter.IsClassicSupported)
        Out-Line ("IsCentralRoleSupported        : {0}" -f $adapter.IsCentralRoleSupported)
        $peripheral = [bool]$adapter.IsPeripheralRoleSupported
        Out-Line ("IsPeripheralRoleSupported     : {0}" -f $peripheral) ($(if ($peripheral) { 'Green' } else { 'Red' }))
        Out-Line ("LE Secure Connections         : {0}" -f (Get-SafeProperty $adapter 'AreLowEnergySecureConnectionsSupported'))
        Out-Line ("Advertisement offload         : {0}" -f (Get-SafeProperty $adapter 'IsAdvertisementOffloadSupported'))
        Out-Line ("Extended advertising          : {0}" -f (Get-SafeProperty $adapter 'IsExtendedAdvertisingSupported'))
        Out-Line ("Max advertisement data length : {0}" -f (Get-SafeProperty $adapter 'MaxAdvertisementDataLength'))
        if ($radio.State -ne 'On') {
            Out-Line 'The Bluetooth radio is not ON: turn Bluetooth on and run the check again.' Yellow
        }
    }
} catch {
    Out-Line ("WinRT query failed: {0}" -f $_.Exception.Message) Red
}

# --- BR/EDR ----------------------------------------------------------------------
Out-Section 'Bluetooth Classic (BR/EDR) state'
try {
    Out-Line ("Discoverable (inquiry scan) : {0}" -f [IPhoneMirrorBtNative]::BluetoothIsDiscoverable([IntPtr]::Zero))
    Out-Line ("Connectable  (page scan)    : {0}" -f [IPhoneMirrorBtNative]::BluetoothIsConnectable([IntPtr]::Zero))
} catch {
    Out-Line ("BluetoothAPIs.dll query failed: {0}" -f $_.Exception.Message) Yellow
}

# --- PnP / driver -------------------------------------------------------------------
Out-Section 'Bluetooth devices and drivers (PnP)'
$vendorNames = @{
    '8087' = 'Intel'; '0E8D' = 'MediaTek'; '0BDA' = 'Realtek'; '0489' = 'Foxconn (often MediaTek/Realtek module)';
    '13D3' = 'IMC Networks/AzureWave (often MediaTek/Realtek module)'; '0CF3' = 'Qualcomm Atheros';
    '04CA' = 'Lite-On (often Qualcomm/Realtek module)'; '0A12' = 'Cambridge Silicon Radio'; '0A5C' = 'Broadcom'; '17EF' = 'Lenovo'
}
$radios = @(Get-PnpDevice -Class Bluetooth -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^(USB|PCI|SD|ACPI)\\' })
foreach ($dev in $radios) {
    Out-Line ("Device      : {0} [{1}]" -f $dev.FriendlyName, $dev.Status)
    Out-Line ("InstanceId  : {0}" -f $dev.InstanceId)
    if ($dev.InstanceId -match 'VID_([0-9A-F]{4})&PID_([0-9A-F]{4})') {
        $vid = $Matches[1]; $productId = $Matches[2]
        $vendor = if ($vendorNames.ContainsKey($vid)) { $vendorNames[$vid] } else { 'unknown vendor' }
        Out-Line ("Chip vendor : {0} (VID {1}, PID {2})" -f $vendor, $vid, $productId)
    }
    $drv = Get-CimInstance Win32_PnPSignedDriver -Filter ("DeviceID='{0}'" -f ($dev.InstanceId -replace '\\', '\\')) -ErrorAction SilentlyContinue
    if ($drv) {
        Out-Line ("Driver      : {0} {1} ({2:yyyy-MM-dd}) by {3}" -f $drv.DriverName, $drv.DriverVersion, $drv.DriverDate, $drv.Manufacturer)
    }
    $props = Get-PnpDeviceProperty -InstanceId $dev.InstanceId -ErrorAction SilentlyContinue |
        Where-Object { $_.KeyName -match 'Bluetooth' -and $null -ne $_.Data -and "$($_.Data)" -ne '' }
    foreach ($p in $props) {
        Out-Line ("  {0} = {1}" -f $p.KeyName, ($p.Data -join ','))
    }
    Out-Line ''
}
if ($radios.Count -eq 0) {
    Out-Line 'No Bluetooth radio found among PnP devices. Listing all Bluetooth-class devices:' Yellow
    Get-PnpDevice -Class Bluetooth -PresentOnly -ErrorAction SilentlyContinue |
        ForEach-Object { Out-Line ("  {0} | {1} | {2}" -f $_.FriendlyName, $_.Status, $_.InstanceId) }
}

$paired = @(Get-PnpDevice -Class Bluetooth -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^BTHENUM\\|^BTHLE\\' -and $_.FriendlyName -match 'iPhone' })
if ($paired.Count -gt 0) {
    Out-Section 'Existing iPhone pairings (e.g. Phone Link)'
    $paired | ForEach-Object { Out-Line ("  {0} | {1}" -f $_.FriendlyName, $_.InstanceId) }
    Out-Line 'Note: an existing pairing can interfere with the HID test. The test plan explains when to remove it.' Yellow
}

# --- Verdict ---------------------------------------------------------------------
Out-Section 'Verdict'
if ($peripheral -eq $true) {
    Out-Line 'PERIPHERAL ROLE SUPPORTED: the PC can try to act as a BLE mouse/keyboard. Go on with step 2.' Green
} elseif ($peripheral -eq $false) {
    Out-Line 'PERIPHERAL ROLE NOT SUPPORTED: the zero-hardware path cannot work with this adapter/driver.' Red
    Out-Line 'Before giving up: update the Bluetooth driver from the Asus/chip vendor site and run this check again.' Yellow
} else {
    Out-Line 'Could not determine Peripheral role support (see errors above).' Yellow
}
Out-Line ''
Out-Line ("Saved to {0}" -f $logFile)
