using System.Runtime.InteropServices;
using System.Text.Json;
using IPhoneMirror.HidProbe.Diagnostics;

namespace IPhoneMirror.HidProbe.Ble;

/// <summary>
/// Temporarily turns off Bluetooth Classic (BR/EDR) inquiry scan (discoverable) and page scan
/// (connectable) while the BLE HID service is advertised, so the phone sees an LE-only device
/// and does not try classic HID instead of HID over GATT.
/// The original state is persisted to disk before changing anything, so it can be restored
/// on the next start if the app crashes. Side effect while active: classic Bluetooth devices
/// (headphones, speakers) cannot connect to the PC.
/// </summary>
internal static class BrEdrSuppressor
{
    private static readonly string s_stateFile = Path.Combine(Log.LogDirectory, "bredr-state.json");
    private static readonly object s_lock = new();
    private static SavedState? s_saved;

    public static bool IsActive
    {
        get
        {
            lock (s_lock)
            {
                return s_saved is not null;
            }
        }
    }

    /// <summary>Current BR/EDR state across all local radios.</summary>
    public static (bool Discoverable, bool Connectable) Query() =>
        (NativeMethods.BluetoothIsDiscoverable(IntPtr.Zero), NativeMethods.BluetoothIsConnectable(IntPtr.Zero));

    public static void Suppress()
    {
        lock (s_lock)
        {
            if (s_saved is not null)
            {
                return;
            }

            var (discoverable, connectable) = Query();
            var saved = new SavedState(discoverable, connectable, DateTime.Now);
            File.WriteAllText(s_stateFile, JsonSerializer.Serialize(saved));
            s_saved = saved;
            Log.Info($"BR/EDR before suppression: discoverable={discoverable} connectable={connectable} (saved to {s_stateFile})");

            // Disabling incoming connections also disables discovery, but be explicit.
            var discoveryOk = NativeMethods.BluetoothEnableDiscovery(IntPtr.Zero, false);
            var discoveryErr = Marshal.GetLastWin32Error();
            var connectionsOk = NativeMethods.BluetoothEnableIncomingConnections(IntPtr.Zero, false);
            var connectionsErr = Marshal.GetLastWin32Error();
            Log.Info($"BluetoothEnableDiscovery(false) -> {discoveryOk} (err {discoveryErr}); " +
                     $"BluetoothEnableIncomingConnections(false) -> {connectionsOk} (err {connectionsErr})");

            var (d, c) = Query();
            var level = d || c ? "WARNING: suppression did not fully take effect" : "OK";
            Log.Info($"BR/EDR after suppression: discoverable={d} connectable={c} -> {level}");
        }
    }

    public static void Restore()
    {
        lock (s_lock)
        {
            if (s_saved is null)
            {
                return;
            }

            RestoreCore(s_saved);
            s_saved = null;
        }
    }

    /// <summary>Restores a state left behind by a previous run that did not exit cleanly.</summary>
    public static void RecoverFromPreviousRun()
    {
        if (!File.Exists(s_stateFile))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(s_stateFile));
            if (saved is null)
            {
                File.Delete(s_stateFile);
                return;
            }

            Log.Warn($"Found BR/EDR state saved at {saved.SavedAt} by a previous run that did not restore it. Restoring now.");
            lock (s_lock)
            {
                RestoreCore(saved);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Recovering BR/EDR state from previous run failed", ex);
        }
    }

    private static void RestoreCore(SavedState saved)
    {
        // Order matters: discovery can only be enabled while the radio is connectable.
        if (saved.Connectable)
        {
            var ok = NativeMethods.BluetoothEnableIncomingConnections(IntPtr.Zero, true);
            Log.Info($"BluetoothEnableIncomingConnections(true) -> {ok} (err {Marshal.GetLastWin32Error()})");
        }

        if (saved.Discoverable)
        {
            var ok = NativeMethods.BluetoothEnableDiscovery(IntPtr.Zero, true);
            Log.Info($"BluetoothEnableDiscovery(true) -> {ok} (err {Marshal.GetLastWin32Error()})");
        }

        var (d, c) = Query();
        Log.Info($"BR/EDR restored: discoverable={d} connectable={c} (expected discoverable={saved.Discoverable} connectable={saved.Connectable})");
        try
        {
            File.Delete(s_stateFile);
        }
        catch (IOException ex)
        {
            Log.Warn($"Could not delete {s_stateFile}: {ex.Message}");
        }
    }

    private sealed record SavedState(bool Discoverable, bool Connectable, DateTime SavedAt);

    private static class NativeMethods
    {
        // hRadio = NULL applies the call to (or queries) all local radios.
        [DllImport("BluetoothAPIs.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothEnableDiscovery(IntPtr hRadio, [MarshalAs(UnmanagedType.Bool)] bool fEnabled);

        [DllImport("BluetoothAPIs.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothEnableIncomingConnections(IntPtr hRadio, [MarshalAs(UnmanagedType.Bool)] bool fEnabled);

        [DllImport("BluetoothAPIs.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothIsDiscoverable(IntPtr hRadio);

        [DllImport("BluetoothAPIs.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothIsConnectable(IntPtr hRadio);
    }
}
