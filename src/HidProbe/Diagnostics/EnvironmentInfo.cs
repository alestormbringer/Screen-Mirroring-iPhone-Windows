using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace IPhoneMirror.HidProbe.Diagnostics;

internal sealed record AdapterInfo(
    string Address,
    bool IsLowEnergySupported,
    bool IsClassicSupported,
    bool IsPeripheralRoleSupported,
    bool IsCentralRoleSupported,
    bool? AreLowEnergySecureConnectionsSupported,
    bool? IsExtendedAdvertisingSupported,
    uint? MaxAdvertisementDataLength,
    string RadioName,
    RadioState? RadioState);

/// <summary>Collects environment facts that decide whether the probe can work at all.</summary>
internal static class EnvironmentInfo
{
    /// <summary>
    /// Returns the package full name, or null when the process runs without package identity
    /// (e.g. HidProbe.exe started directly from the bin folder). Without identity the GATT
    /// server APIs report success but the HID service is never really published.
    /// </summary>
    public static string? TryGetPackageFullName()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void LogProcessInfo()
    {
        Log.Info($"HidProbe {typeof(EnvironmentInfo).Assembly.GetName().Version}");
        Log.Info($"OS: {RuntimeInformation.OSDescription} | Environment.OSVersion: {Environment.OSVersion.Version}");
        Log.Info($"Runtime: {RuntimeInformation.FrameworkDescription} | Process arch: {RuntimeInformation.ProcessArchitecture}");
        Log.Info($"Executable: {Environment.ProcessPath}");
        Log.Info($"Log file: {Log.FilePath}");
    }

    public static async Task<AdapterInfo?> GetAdapterInfoAsync()
    {
        BluetoothAdapter? adapter;
        try
        {
            adapter = await BluetoothAdapter.GetDefaultAsync();
        }
        catch (Exception ex)
        {
            Log.Error("BluetoothAdapter.GetDefaultAsync failed", ex);
            return null;
        }

        if (adapter is null)
        {
            Log.Error("No Bluetooth adapter found (BluetoothAdapter.GetDefaultAsync returned null).");
            return null;
        }

        string radioName = "?";
        RadioState? radioState = null;
        try
        {
            var radio = await adapter.GetRadioAsync();
            radioName = radio?.Name ?? "?";
            radioState = radio?.State;
        }
        catch (Exception ex)
        {
            Log.Warn($"GetRadioAsync failed: {ex.Message}");
        }

        var info = new AdapterInfo(
            Address: FormatAddress(adapter.BluetoothAddress),
            IsLowEnergySupported: adapter.IsLowEnergySupported,
            IsClassicSupported: adapter.IsClassicSupported,
            IsPeripheralRoleSupported: adapter.IsPeripheralRoleSupported,
            IsCentralRoleSupported: adapter.IsCentralRoleSupported,
            AreLowEnergySecureConnectionsSupported: TryRead(() => adapter.AreLowEnergySecureConnectionsSupported),
            IsExtendedAdvertisingSupported: TryRead(() => adapter.IsExtendedAdvertisingSupported),
            MaxAdvertisementDataLength: TryRead(() => adapter.MaxAdvertisementDataLength),
            RadioName: radioName,
            RadioState: radioState);

        Log.Info($"Adapter: {info.RadioName} [{info.Address}] radio={info.RadioState}");
        Log.Info($"  LE={info.IsLowEnergySupported} Classic={info.IsClassicSupported} " +
                 $"Peripheral={info.IsPeripheralRoleSupported} Central={info.IsCentralRoleSupported}");
        Log.Info($"  LE secure connections={Fmt(info.AreLowEnergySecureConnectionsSupported)} " +
                 $"Extended advertising={Fmt(info.IsExtendedAdvertisingSupported)} " +
                 $"Max adv data={Fmt(info.MaxAdvertisementDataLength)}");

        if (!info.IsPeripheralRoleSupported)
        {
            Log.Error("The adapter does NOT report Peripheral role support: the PC cannot act as a BLE HID device.");
        }

        return info;
    }

    public static string FormatAddress(ulong address)
    {
        var bytes = BitConverter.GetBytes(address);
        return string.Join(":", bytes.Take(6).Reverse().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static T? TryRead<T>(Func<T> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            // Property not available on this Windows build.
            return null;
        }
    }

    private static string Fmt<T>(T? value)
        where T : struct => value?.ToString() ?? "n/a";
}
