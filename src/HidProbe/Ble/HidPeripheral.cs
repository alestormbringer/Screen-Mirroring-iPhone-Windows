using System.Runtime.InteropServices.WindowsRuntime;
using IPhoneMirror.HidProbe.Diagnostics;
using IPhoneMirror.HidProbe.Hid;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPhoneMirror.HidProbe.Ble;

/// <summary>
/// BLE HID peripheral (HID over GATT, service 0x1812) built on the WinRT GATT server.
/// Publishes one composite HID service (keyboard + mouse + consumer control) and,
/// optionally, a Battery Service (0x180F), which HOGP expects on a HID device.
/// </summary>
internal sealed class HidPeripheral : IDisposable
{
    private static readonly Guid HidServiceUuid = BluetoothUuidHelper.FromShortId(0x1812);
    private static readonly Guid BatteryServiceUuid = BluetoothUuidHelper.FromShortId(0x180F);
    private static readonly Guid ReportUuid = BluetoothUuidHelper.FromShortId(0x2A4D);
    private static readonly Guid ReportMapUuid = BluetoothUuidHelper.FromShortId(0x2A4B);
    private static readonly Guid HidInformationUuid = BluetoothUuidHelper.FromShortId(0x2A4A);
    private static readonly Guid HidControlPointUuid = BluetoothUuidHelper.FromShortId(0x2A4C);
    private static readonly Guid ProtocolModeUuid = BluetoothUuidHelper.FromShortId(0x2A4E);
    private static readonly Guid BatteryLevelUuid = BluetoothUuidHelper.FromShortId(0x2A19);
    private static readonly Guid ReportReferenceUuid = BluetoothUuidHelper.FromShortId(0x2908);

    private const byte ReportTypeInput = 0x01;
    private const byte ReportTypeOutput = 0x02;

    private readonly bool _includeBattery;
    private readonly object _sessionLock = new();
    private readonly Dictionary<string, TrackedDevice> _devices = new();

    private GattServiceProvider? _hidProvider;
    private GattServiceProvider? _batteryProvider;
    private GattLocalCharacteristic? _keyboardInput;
    private GattLocalCharacteristic? _mouseInput;
    private GattLocalCharacteristic? _consumerInput;
    private NotifyChannel? _keyboardChannel;
    private NotifyChannel? _mouseChannel;
    private NotifyChannel? _consumerChannel;
    private byte _keyboardLeds;
    private bool _disposed;

    public HidPeripheral(PointerMode mode, bool includeBattery)
    {
        Mode = mode;
        _includeBattery = includeBattery;
    }

    /// <summary>Raised (on a thread-pool thread) when advertising, subscriptions or devices change.</summary>
    public event Action? StateChanged;

    public PointerMode Mode { get; }

    public GattServiceProviderAdvertisementStatus? HidAdvertisementStatus => _hidProvider?.AdvertisementStatus;

    public int KeyboardSubscribers => SafeCount(_keyboardInput);

    public int MouseSubscribers => SafeCount(_mouseInput);

    public int ConsumerSubscribers => SafeCount(_consumerInput);

    public int MouseMinIntervalMs
    {
        set
        {
            if (_mouseChannel is not null)
            {
                _mouseChannel.MinIntervalMs = value;
            }
        }
    }

    public string DevicesSummary
    {
        get
        {
            lock (_sessionLock)
            {
                return _devices.Count == 0
                    ? "-"
                    : string.Join("; ", _devices.Values.Select(d => d.Summary));
            }
        }
    }

    public async Task InitializeAsync()
    {
        Log.Info($"Creating HID service (pointer mode: {Mode}, battery service: {_includeBattery})...");
        var reportMap = HidReportMaps.Build(Mode);
        Log.Info($"Report map ({reportMap.Length} bytes): {Convert.ToHexString(reportMap)}");

        var hidResult = await GattServiceProvider.CreateAsync(HidServiceUuid);
        if (hidResult.Error != BluetoothError.Success)
        {
            throw new InvalidOperationException($"GattServiceProvider.CreateAsync(HID 0x1812) failed: {hidResult.Error}");
        }

        _hidProvider = hidResult.ServiceProvider;
        _hidProvider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;
        var hid = _hidProvider.Service;

        // Protocol Mode: report protocol only (no boot protocol).
        var protocolMode = await CreateCharacteristicAsync(hid, ProtocolModeUuid, "Protocol Mode", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = new byte[] { 0x01 }.AsBuffer(),
        });
        protocolMode.WriteRequested += (_, args) => LogWrite("Protocol Mode", args);

        // Input reports. Their values are dynamic, so reads are answered in OnInputReportRead.
        _keyboardInput = await CreateInputReportAsync(hid, HidReportMaps.KeyboardReportId, "Keyboard input report");
        _mouseInput = await CreateInputReportAsync(hid, HidReportMaps.MouseReportId, "Mouse input report");
        _consumerInput = await CreateInputReportAsync(hid, HidReportMaps.ConsumerReportId, "Consumer input report");

        // Keyboard output report (Caps Lock LED etc. written by the iPhone).
        var keyboardOutput = await CreateCharacteristicAsync(hid, ReportUuid, "Keyboard output report", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write |
                                       GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });
        await CreateReportReferenceAsync(keyboardOutput, HidReportMaps.KeyboardReportId, ReportTypeOutput, "Keyboard output report");
        keyboardOutput.ReadRequested += (_, args) => RespondToRead("Keyboard output report", args, [_keyboardLeds]);
        keyboardOutput.WriteRequested += OnKeyboardLedsWritten;

        await CreateCharacteristicAsync(hid, ReportMapUuid, "Report Map", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = reportMap.AsBuffer(),
        });

        await CreateCharacteristicAsync(hid, HidInformationUuid, "HID Information", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            // bcdHID 1.11, country code 0 (not localized), flags: RemoteWake.
            StaticValue = new byte[] { 0x11, 0x01, 0x00, 0x01 }.AsBuffer(),
        });

        var controlPoint = await CreateCharacteristicAsync(hid, HidControlPointUuid, "HID Control Point", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });
        controlPoint.WriteRequested += (_, args) => LogWrite("HID Control Point (0=Suspend, 1=Exit Suspend)", args);

        _keyboardChannel = new NotifyChannel(_keyboardInput, "KBD", HidReportMaps.KeyboardInputLength);
        _consumerChannel = new NotifyChannel(_consumerInput, "CONSUMER", HidReportMaps.ConsumerLength);
        _mouseChannel = new NotifyChannel(
            _mouseInput,
            "MOUSE",
            HidReportMaps.MouseLength(Mode),
            Mode == PointerMode.Absolute ? MergeAbsolute : MergeRelative);

        if (_includeBattery)
        {
            await CreateBatteryServiceAsync();
        }

        Log.Info("HID service created.");
    }

    public void StartAdvertising()
    {
        if (_hidProvider is null)
        {
            throw new InvalidOperationException("InitializeAsync must be called first.");
        }

        if (_batteryProvider is not null)
        {
            // Published (connectable) but not advertised: only the HID UUID goes in the advertisement.
            _batteryProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
            {
                IsConnectable = true,
                IsDiscoverable = false,
            });
            Log.Info($"Battery service publishing requested. Status: {_batteryProvider.AdvertisementStatus}");
        }

        _hidProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
        {
            IsConnectable = true,
            IsDiscoverable = true,
        });
        Log.Info($"HID advertising requested. Status: {_hidProvider.AdvertisementStatus}");
    }

    public void StopAdvertising()
    {
        StopProvider(_hidProvider, "HID");
        StopProvider(_batteryProvider, "Battery");
    }

    public void SendKeyboardReport(byte[] report) => _keyboardChannel?.Enqueue(report);

    /// <summary>Relative mode: queues a movement, split into several reports if it exceeds ±127.</summary>
    public void SendMouseRelative(byte buttons, int dx, int dy, int wheel = 0)
    {
        if (_mouseChannel is null || Mode != PointerMode.Relative)
        {
            return;
        }

        do
        {
            var stepX = Math.Clamp(dx, -127, 127);
            var stepY = Math.Clamp(dy, -127, 127);
            var stepWheel = Math.Clamp(wheel, -127, 127);
            _mouseChannel.Enqueue([buttons, (byte)(sbyte)stepX, (byte)(sbyte)stepY, (byte)(sbyte)stepWheel]);
            dx -= stepX;
            dy -= stepY;
            wheel -= stepWheel;
        }
        while (dx != 0 || dy != 0 || wheel != 0);
    }

    /// <summary>Absolute mode: queues a position in 0..<see cref="HidReportMaps.AbsoluteMax"/>.</summary>
    public void SendMouseAbsolute(byte buttons, ushort x, ushort y, int wheel = 0)
    {
        if (_mouseChannel is null || Mode != PointerMode.Absolute)
        {
            return;
        }

        x = Math.Min(x, HidReportMaps.AbsoluteMax);
        y = Math.Min(y, HidReportMaps.AbsoluteMax);
        var w = (sbyte)Math.Clamp(wheel, -127, 127);
        _mouseChannel.Enqueue([buttons, (byte)x, (byte)(x >> 8), (byte)y, (byte)(y >> 8), (byte)w]);
    }

    /// <summary>Sends a Consumer usage press followed by its release.</summary>
    public void SendConsumerClick(ushort usage)
    {
        _consumerChannel?.Enqueue([(byte)usage, (byte)(usage >> 8)]);
        _consumerChannel?.Enqueue([0, 0]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAdvertising();
        lock (_sessionLock)
        {
            _devices.Clear();
        }
    }

    // Two queued relative reports with the same buttons can be summed if the result still fits.
    private static byte[]? MergeRelative(byte[] queued, byte[] next)
    {
        if (queued[0] != next[0])
        {
            return null;
        }

        var dx = (sbyte)queued[1] + (sbyte)next[1];
        var dy = (sbyte)queued[2] + (sbyte)next[2];
        var wheel = (sbyte)queued[3] + (sbyte)next[3];
        if (dx is < -127 or > 127 || dy is < -127 or > 127 || wheel is < -127 or > 127)
        {
            return null;
        }

        return [queued[0], (byte)(sbyte)dx, (byte)(sbyte)dy, (byte)(sbyte)wheel];
    }

    // A queued absolute position is superseded by a newer one when buttons and wheel are idle.
    private static byte[]? MergeAbsolute(byte[] queued, byte[] next) =>
        queued[0] == next[0] && queued[5] == 0 && next[5] == 0 ? next : null;

    private async Task<GattLocalCharacteristic> CreateInputReportAsync(GattLocalService service, byte reportId, string name)
    {
        var characteristic = await CreateCharacteristicAsync(service, ReportUuid, name, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });
        await CreateReportReferenceAsync(characteristic, reportId, ReportTypeInput, name);
        characteristic.SubscribedClientsChanged += (sender, _) => OnSubscribedClientsChanged(sender, name);
        characteristic.ReadRequested += (sender, args) => RespondToRead(name, args, LastValueOf(sender));
        return characteristic;
    }

    private byte[] LastValueOf(GattLocalCharacteristic characteristic)
    {
        if (ReferenceEquals(characteristic, _keyboardInput))
        {
            return _keyboardChannel?.LastValue ?? new byte[HidReportMaps.KeyboardInputLength];
        }

        if (ReferenceEquals(characteristic, _mouseInput))
        {
            return _mouseChannel?.LastValue ?? new byte[HidReportMaps.MouseLength(Mode)];
        }

        return _consumerChannel?.LastValue ?? new byte[HidReportMaps.ConsumerLength];
    }

    private static async Task CreateReportReferenceAsync(GattLocalCharacteristic characteristic, byte reportId, byte reportType, string name)
    {
        var result = await characteristic.CreateDescriptorAsync(ReportReferenceUuid, new GattLocalDescriptorParameters
        {
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = new byte[] { reportId, reportType }.AsBuffer(),
        });
        if (result.Error != BluetoothError.Success)
        {
            throw new InvalidOperationException($"Creating Report Reference for '{name}' failed: {result.Error}");
        }

        Log.Debug($"  + Report Reference for '{name}': id={reportId} type={reportType}");
    }

    private static async Task<GattLocalCharacteristic> CreateCharacteristicAsync(
        GattLocalService service, Guid uuid, string name, GattLocalCharacteristicParameters parameters)
    {
        var result = await service.CreateCharacteristicAsync(uuid, parameters);
        if (result.Error != BluetoothError.Success)
        {
            throw new InvalidOperationException($"Creating characteristic '{name}' ({uuid}) failed: {result.Error}");
        }

        Log.Debug($"  + characteristic '{name}' ({parameters.CharacteristicProperties})");
        return result.Characteristic;
    }

    private async Task CreateBatteryServiceAsync()
    {
        var result = await GattServiceProvider.CreateAsync(BatteryServiceUuid);
        if (result.Error != BluetoothError.Success)
        {
            // Not fatal: the HID service may work without it.
            Log.Warn($"Battery service could not be created: {result.Error}. Continuing without it.");
            return;
        }

        _batteryProvider = result.ServiceProvider;
        _batteryProvider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;
        var level = await CreateCharacteristicAsync(_batteryProvider.Service, BatteryLevelUuid, "Battery Level", new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.Plain,
        });
        level.ReadRequested += (_, args) => RespondToRead("Battery Level", args, [100]);
        Log.Info("Battery service created (level 100%).");
    }

    private static async void RespondToRead(string name, GattReadRequestedEventArgs args, byte[] value)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
            {
                Log.Warn($"Read of '{name}': request was null (access denied?).");
                return;
            }

            Log.Info($"Read of '{name}' by {args.Session?.DeviceId?.Id} (offset {request.Offset}) -> {Convert.ToHexString(value)}");
            request.RespondWithValue(value.AsBuffer());
        }
        catch (Exception ex)
        {
            Log.Error($"Read of '{name}' failed", ex);
        }
        finally
        {
            // Deferral.Dispose() only closes it; Complete() is what releases the request.
            deferral.Complete();
        }
    }

    private async void OnKeyboardLedsWritten(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
            {
                return;
            }

            var value = request.Value.ToArray();
            if (value.Length > 0)
            {
                _keyboardLeds = value[0];
            }

            Log.Info($"Keyboard LEDs written by iPhone: {Convert.ToHexString(value)} (bit0=NumLock bit1=CapsLock)");
            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard LED write failed", ex);
        }
        finally
        {
            // Deferral.Dispose() only closes it; Complete() is what releases the request.
            deferral.Complete();
        }
    }

    private static async void LogWrite(string name, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
            {
                return;
            }

            Log.Info($"Write to '{name}': {Convert.ToHexString(request.Value.ToArray())}");
            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Write to '{name}' failed", ex);
        }
        finally
        {
            // Deferral.Dispose() only closes it; Complete() is what releases the request.
            deferral.Complete();
        }
    }

    private void OnAdvertisementStatusChanged(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
    {
        var which = ReferenceEquals(sender, _hidProvider) ? "HID" : "Battery";
        var message = $"{which} advertisement status: {args.Status} (error: {args.Error})";
        if (args.Error != BluetoothError.Success || args.Status == GattServiceProviderAdvertisementStatus.Aborted)
        {
            Log.Warn(message);
        }
        else
        {
            Log.Info(message);
        }

        StateChanged?.Invoke();
    }

    private void OnSubscribedClientsChanged(GattLocalCharacteristic sender, string name)
    {
        var clients = sender.SubscribedClients;
        Log.Info($"{name}: subscribed clients = {clients.Count}");
        foreach (var client in clients)
        {
            var session = client.Session;
            Log.Info($"  client {session.DeviceId.Id} | MaxPduSize={session.MaxPduSize} " +
                     $"| MaxNotificationSize={client.MaxNotificationSize} | session={session.SessionStatus}");
            TrackDevice(session);
        }

        StateChanged?.Invoke();
    }

    private void TrackDevice(GattSession session)
    {
        var id = session.DeviceId.Id;
        lock (_sessionLock)
        {
            if (_devices.ContainsKey(id))
            {
                return;
            }

            _devices[id] = new TrackedDevice(id, session);
        }

        session.SessionStatusChanged += (s, e) =>
        {
            Log.Info($"GATT session {s.DeviceId.Id}: {e.Status} (error: {e.Error})");
            StateChanged?.Invoke();
        };
        session.MaxPduSizeChanged += (s, _) => Log.Info($"GATT session {s.DeviceId.Id}: MaxPduSize = {s.MaxPduSize}");
        _ = DescribeDeviceAsync(id);
    }

    private async Task DescribeDeviceAsync(string id)
    {
        try
        {
            var device = await BluetoothLEDevice.FromIdAsync(id);
            if (device is null)
            {
                Log.Warn($"BluetoothLEDevice.FromIdAsync({id}) returned null.");
                return;
            }

            lock (_sessionLock)
            {
                if (_devices.TryGetValue(id, out var tracked))
                {
                    tracked.Device = device;
                }
            }

            Log.Info($"Device: '{device.Name}' [{EnvironmentInfo.FormatAddress(device.BluetoothAddress)}] " +
                     $"connection={device.ConnectionStatus} paired={device.DeviceInformation?.Pairing?.IsPaired} " +
                     $"protection={device.DeviceInformation?.Pairing?.ProtectionLevel}");
            device.ConnectionStatusChanged += (d, _) =>
            {
                Log.Info($"Device '{d.Name}': connection status = {d.ConnectionStatus}");
                StateChanged?.Invoke();
            };
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not describe device {id}", ex);
        }
    }

    private static void StopProvider(GattServiceProvider? provider, string name)
    {
        if (provider is null)
        {
            return;
        }

        try
        {
            if (provider.AdvertisementStatus is not GattServiceProviderAdvertisementStatus.Created
                and not GattServiceProviderAdvertisementStatus.Stopped)
            {
                provider.StopAdvertising();
                Log.Info($"{name} advertising stopped.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Stopping {name} advertising failed", ex);
        }
    }

    private static int SafeCount(GattLocalCharacteristic? characteristic)
    {
        try
        {
            return characteristic?.SubscribedClients.Count ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private sealed class TrackedDevice(string id, GattSession session)
    {
        // Keeps the WinRT objects (and their event subscriptions) alive.
        public GattSession Session { get; } = session;

        public BluetoothLEDevice? Device { get; set; }

        public string Summary =>
            Device is null
                ? $"{id} ({Session.SessionStatus})"
                : $"{Device.Name} ({Device.ConnectionStatus}, GATT {Session.SessionStatus})";
    }
}
