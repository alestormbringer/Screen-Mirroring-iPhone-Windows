<#
.SYNOPSIS
    Emergency restore of Bluetooth Classic (BR/EDR) after a BR/EDR suppression test, in case
    HidProbe could not restore it (normally HidProbe restores it on exit and on next start).

.DESCRIPTION
    Re-enables incoming connections (page scan), which is the normal Windows state.
    Discoverability is left off: Windows turns it on by itself while the Bluetooth
    settings page is open. Turning Bluetooth off and on again also restores defaults.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\Restore-BrEdr.ps1
#>
[CmdletBinding()]
param()

$source = @'
using System;
using System.Runtime.InteropServices;
public static class IPhoneMirrorBtRestore {
    [DllImport("BluetoothAPIs.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothEnableIncomingConnections(IntPtr hRadio, [MarshalAs(UnmanagedType.Bool)] bool fEnabled);
    [DllImport("BluetoothAPIs.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothIsDiscoverable(IntPtr hRadio);
    [DllImport("BluetoothAPIs.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothIsConnectable(IntPtr hRadio);
}
'@
if (-not ('IPhoneMirrorBtRestore' -as [Type])) { Add-Type -TypeDefinition $source }

$before = [IPhoneMirrorBtRestore]::BluetoothIsConnectable([IntPtr]::Zero)
$ok = [IPhoneMirrorBtRestore]::BluetoothEnableIncomingConnections([IntPtr]::Zero, $true)
$after = [IPhoneMirrorBtRestore]::BluetoothIsConnectable([IntPtr]::Zero)
Write-Host "Connectable before: $before | EnableIncomingConnections(true): $ok | connectable now: $after"
Write-Host "Discoverable now: $([IPhoneMirrorBtRestore]::BluetoothIsDiscoverable([IntPtr]::Zero))"

$stateFile = Join-Path $env:TEMP 'iPhoneMirror\bredr-state.json'
if (Test-Path $stateFile) {
    Remove-Item $stateFile
    Write-Host "Removed $stateFile"
}
if ($after) {
    Write-Host 'Bluetooth Classic is connectable again.' -ForegroundColor Green
} else {
    Write-Host 'Still not connectable: turn Bluetooth off and on (Settings > Bluetooth & devices) or reboot.' -ForegroundColor Yellow
}
