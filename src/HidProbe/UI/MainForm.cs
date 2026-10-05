using System.Diagnostics;
using System.Text.RegularExpressions;
using IPhoneMirror.HidProbe.Ble;
using IPhoneMirror.HidProbe.Diagnostics;
using IPhoneMirror.HidProbe.Hid;
using IPhoneMirror.HidProbe.Input;

namespace IPhoneMirror.HidProbe.UI;

/// <summary>Phase 0 test window: configuration, live status, quick tests, touchpad and log.</summary>
internal sealed partial class MainForm : Form
{
    private const uint VkLeftControl = 0xA2;
    private const int MaxLogChars = 200_000;

    private readonly TouchpadPanel _touchpad = new() { Dock = DockStyle.Fill };
    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
    };

    private readonly Label _identityLabel = StatusLabel();
    private readonly Label _adapterLabel = StatusLabel();
    private readonly Label _advertisingLabel = StatusLabel();
    private readonly Label _subscribersLabel = StatusLabel();
    private readonly Label _deviceLabel = StatusLabel();
    private readonly Label _brEdrLabel = StatusLabel();

    private readonly RadioButton _relativeRadio = new() { Text = "Puntatore relativo (mouse standard)", AutoSize = true, Checked = true };
    private readonly RadioButton _absoluteRadio = new() { Text = "Puntatore assoluto (X/Y 0–32767)", AutoSize = true };
    private readonly CheckBox _batteryCheck = new() { Text = "Servizio Batteria (0x180F)", AutoSize = true, Checked = true };
    private readonly CheckBox _suppressBrEdrCheck = new() { Text = "Sopprimi Bluetooth classico (BR/EDR) durante il test", AutoSize = true };
    private readonly Button _startButton = ActionButton("Avvia advertising");
    private readonly Button _stopButton = ActionButton("Ferma");

    private readonly CheckBox _forwardKeysCheck = new() { Text = "Inoltra tastiera all'iPhone", AutoSize = true, Checked = true };
    private readonly CheckBox _swapIsoCheck = new() { Text = "Scambia tasti ISO § e <", AutoSize = true };
    private readonly NumericUpDown _sensitivityInput = new() { Minimum = 0.1m, Maximum = 10m, Increment = 0.1m, DecimalPlaces = 1, Value = 1m, Width = 60 };
    private readonly NumericUpDown _mouseIntervalInput = new() { Minimum = 0, Maximum = 100, Value = 15, Width = 60 };

    private readonly Button _typeTestButton = ActionButton("Scrivi «Zürich 123»");
    private readonly Button _spotlightButton = ActionButton("Cmd+Spazio (Spotlight)");
    private readonly Button _cmdHButton = ActionButton("Cmd+H (Home?)");
    private readonly ComboBox _consumerCombo = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _consumerButton = ActionButton("Invia tasto consumer");
    private readonly Button _cornerButton = ActionButton("Puntatore → angolo in alto a sinistra");
    private readonly Button _releaseButton = ActionButton("Rilascia tutto");
    private readonly Button _openLogsButton = ActionButton("Apri cartella log");

    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    private readonly KeyboardState _keyboard = new();
    private readonly LowLevelKeyboardHook _keyboardHook;

    private HidPeripheral? _peripheral;
    private byte _mouseButtons;
    private ushort _absoluteX = HidReportMaps.AbsoluteMax / 2;
    private ushort _absoluteY = HidReportMaps.AbsoluteMax / 2;
    private bool _busy;
    private bool _hasIdentity;

    public MainForm()
    {
        Text = "iPhone Mirror – HID Probe (Fase 0)";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(1280, 860);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;

        _keyboardHook = new LowLevelKeyboardHook(OnRawKey);
        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Log.LineLogged += OnLineLogged;

        EnvironmentInfo.LogProcessInfo();
        var packageName = EnvironmentInfo.TryGetPackageFullName();
        _hasIdentity = packageName is not null;
        if (_hasIdentity)
        {
            Log.Info($"Package identity: {packageName}");
            _identityLabel.Text = $"Package Identity: OK ({packageName})";
            _identityLabel.ForeColor = Color.DarkGreen;
        }
        else
        {
            Log.Error("NO package identity. The GATT server would report success without really publishing the HID " +
                      "service. Start the app via scripts\\Build-HidProbe.ps1 -Launch, the Start menu or 'hidprobe'.");
            _identityLabel.Text = "Package Identity: MANCANTE – avvia con scripts\\Build-HidProbe.ps1 -Launch (vedi log)";
            _identityLabel.ForeColor = Color.Firebrick;
        }

        var adapter = await EnvironmentInfo.GetAdapterInfoAsync();
        if (adapter is null)
        {
            _adapterLabel.Text = "Adattatore Bluetooth: NON TROVATO";
            _adapterLabel.ForeColor = Color.Firebrick;
        }
        else
        {
            _adapterLabel.Text = $"Adattatore: {adapter.RadioName} [{adapter.Address}] radio={adapter.RadioState} | " +
                                 $"Peripheral: {(adapter.IsPeripheralRoleSupported ? "SÌ" : "NO")} | LE: {(adapter.IsLowEnergySupported ? "sì" : "no")}";
            _adapterLabel.ForeColor = adapter.IsPeripheralRoleSupported ? Color.DarkGreen : Color.Firebrick;
        }

        try
        {
            _keyboardHook.Install();
            Log.Info("Low-level keyboard hook installed.");
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard hook installation failed; keyboard forwarding is unavailable", ex);
        }

        _statusTimer.Start();
        UpdateControls();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        ReleaseAll("window deactivated");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statusTimer.Stop();
        Log.LineLogged -= OnLineLogged;
        StopPeripheral();
        _keyboardHook.Dispose();
        base.OnFormClosing(e);
    }

    private static Label StatusLabel() => new() { AutoSize = true, Text = "…", Margin = new Padding(3, 2, 3, 2) };

    private static Button ActionButton(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont.FontFamily, 10f, FontStyle.Bold),
        Margin = new Padding(3, 12, 3, 4),
    };

    private static Label Inline(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(12, 7, 3, 3) };

    private void BuildLayout()
    {
        _consumerCombo.Items.AddRange(
        [
            "0x0223  AC Home",
            "0x0040  Menu",
            "0x0221  AC Search",
            "0x00B8  Eject (tastiera virtuale on/off?)",
            "0x01AE  AL Keyboard Layout",
            "0x00CD  Play/Pausa",
            "0x00E9  Volume +",
            "0x00EA  Volume −",
            "0x00E2  Mute",
        ]);
        _consumerCombo.SelectedIndex = 0;

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        controls.Controls.AddRange(
        [
            Section("Stato"),
            _identityLabel,
            _adapterLabel,
            _advertisingLabel,
            _subscribersLabel,
            _deviceLabel,
            _brEdrLabel,
            Section("1. Configurazione (prima di avviare)"),
            Row(_relativeRadio, _absoluteRadio),
            Row(_batteryCheck, _suppressBrEdrCheck),
            Row(_startButton, _stopButton,
                Inline("Se cambi modalità: su iPhone «Dimentica dispositivo» e ri-accoppia.")),
            Section("2. Input"),
            Row(_forwardKeysCheck, _swapIsoCheck),
            Row(Inline("Sensibilità (relativo):"), _sensitivityInput,
                Inline("Intervallo minimo report mouse (ms):"), _mouseIntervalInput),
            Section("3. Test rapidi"),
            Row(_typeTestButton, _spotlightButton, _cmdHButton),
            Row(_consumerCombo, _consumerButton),
            Row(_cornerButton, _releaseButton, _openLogsButton),
            Section("Log (completo su file)"),
        ]);

        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        side.Controls.Add(controls, 0, 0);
        side.Controls.Add(_logBox, 0, 1);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        root.Controls.Add(_touchpad, 0, 0);
        root.Controls.Add(side, 1, 0);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        _startButton.Click += OnStartClicked;
        _stopButton.Click += (_, _) => StopPeripheral();
        _relativeRadio.CheckedChanged += (_, _) => _touchpad.Mode = SelectedMode;
        _sensitivityInput.ValueChanged += (_, _) => _touchpad.Sensitivity = (double)_sensitivityInput.Value;
        _mouseIntervalInput.ValueChanged += (_, _) =>
        {
            if (_peripheral is not null)
            {
                _peripheral.MouseMinIntervalMs = (int)_mouseIntervalInput.Value;
            }
        };

        _touchpad.RelativeMoved += (dx, dy) => _peripheral?.SendMouseRelative(_mouseButtons, dx, dy);
        _touchpad.AbsoluteMoved += OnAbsoluteMoved;
        _touchpad.ButtonChanged += OnMouseButtonChanged;
        _touchpad.WheelScrolled += OnWheelScrolled;
        _touchpad.LostFocus += (_, _) => ReleaseAll("touchpad lost focus");

        _typeTestButton.Click += (_, _) => TypeTestString();
        _spotlightButton.Click += (_, _) => SendChord(KeyModifiers.LeftGui, 0x2C, "Cmd+Space");
        _cmdHButton.Click += (_, _) => SendChord(KeyModifiers.LeftGui, 0x0B, "Cmd+H");
        _consumerButton.Click += (_, _) => SendSelectedConsumer();
        _cornerButton.Click += (_, _) => MovePointerToTopLeft();
        _releaseButton.Click += (_, _) => ReleaseAll("button");
        _openLogsButton.Click += (_, _) => OpenLogFolder();
        _statusTimer.Tick += (_, _) => RefreshStatus();
    }

    private PointerMode SelectedMode => _absoluteRadio.Checked ? PointerMode.Absolute : PointerMode.Relative;

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_peripheral is not null || _busy)
        {
            return;
        }

        _busy = true;
        UpdateControls();
        HidPeripheral? peripheral = null;
        try
        {
            if (!_hasIdentity)
            {
                Log.Warn("Starting WITHOUT package identity: expect the iPhone not to find the HID service.");
            }

            var mode = SelectedMode;
            _touchpad.Mode = mode;
            peripheral = new HidPeripheral(mode, _batteryCheck.Checked);
            peripheral.StateChanged += OnPeripheralStateChanged;
            await peripheral.InitializeAsync();
            peripheral.MouseMinIntervalMs = (int)_mouseIntervalInput.Value;

            if (_suppressBrEdrCheck.Checked)
            {
                BrEdrSuppressor.Suppress();
            }

            peripheral.StartAdvertising();
            _peripheral = peripheral;
            Log.Info("=== Advertising requested. On the iPhone open Settings > Bluetooth (or AssistiveTouch > Devices) and pair the PC. " +
                     "Accept the pairing prompt on Windows too. ===");
        }
        catch (Exception ex)
        {
            Log.Error("Start failed", ex);
            peripheral?.Dispose();
            BrEdrSuppressor.Restore();
            MessageBox.Show(this, ex.Message, "Avvio non riuscito", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private void StopPeripheral()
    {
        if (_peripheral is not null)
        {
            ReleaseAll("stop");
            _peripheral.StateChanged -= OnPeripheralStateChanged;
            _peripheral.Dispose();
            _peripheral = null;
            Log.Info("=== Peripheral stopped. ===");
        }

        BrEdrSuppressor.Restore();
        UpdateControls();
    }

    private void OnPeripheralStateChanged()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(RefreshStatus);
        }
    }

    private void UpdateControls()
    {
        var running = _peripheral is not null;
        _startButton.Enabled = !running && !_busy;
        _stopButton.Enabled = running;
        _relativeRadio.Enabled = _absoluteRadio.Enabled = !running && !_busy;
        _batteryCheck.Enabled = _suppressBrEdrCheck.Enabled = !running && !_busy;
        _cornerButton.Enabled = running && _peripheral!.Mode == PointerMode.Relative;
        _typeTestButton.Enabled = _spotlightButton.Enabled = _cmdHButton.Enabled = _consumerButton.Enabled = running;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var p = _peripheral;
        _advertisingLabel.Text = p is null
            ? "Advertising: fermo"
            : $"Advertising HID: {p.HidAdvertisementStatus} | modalità puntatore: {p.Mode}";
        _subscribersLabel.Text = p is null
            ? "Iscritti alle notifiche: -"
            : $"Iscritti alle notifiche: tastiera {p.KeyboardSubscribers} | mouse {p.MouseSubscribers} | consumer {p.ConsumerSubscribers}";
        _subscribersLabel.ForeColor = p is not null && p.MouseSubscribers > 0 ? Color.DarkGreen : SystemColors.ControlText;
        _deviceLabel.Text = $"Dispositivi: {p?.DevicesSummary ?? "-"}";

        try
        {
            var (discoverable, connectable) = BrEdrSuppressor.Query();
            _brEdrLabel.Text = $"BR/EDR (Bluetooth classico): rilevabile={discoverable} connettibile={connectable}" +
                               (BrEdrSuppressor.IsActive ? " | SOPPRESSIONE ATTIVA" : string.Empty);
        }
        catch (Exception ex)
        {
            _brEdrLabel.Text = $"BR/EDR: stato non leggibile ({ex.Message})";
        }
    }

    private bool OnRawKey(RawKeyEvent e)
    {
        if (_peripheral is null || !_forwardKeysCheck.Checked || !_touchpad.Focused || ActiveForm != this)
        {
            return false;
        }

        // AltGr is delivered by Windows as a synthetic Left Ctrl (scan code 0x21D) + Right Alt.
        // Forwarding the fake Ctrl would turn AltGr into Ctrl+Option on the iPhone.
        if (e.VirtualKey == VkLeftControl && (e.ScanCode & 0x200) != 0)
        {
            return true;
        }

        var consumer = ScanCodeMap.ToConsumerUsage(e.VirtualKey);
        if (consumer != 0)
        {
            if (!e.IsUp)
            {
                Log.Info($"Media key vk=0x{e.VirtualKey:X2} -> consumer usage 0x{consumer:X4}");
                _peripheral.SendConsumerClick(consumer);
            }

            return true;
        }

        var usage = ScanCodeMap.ToKeyboardUsage(e.ScanCode, e.Extended, e.VirtualKey, _swapIsoCheck.Checked);
        if (usage == ScanCodeMap.UsageNone)
        {
            Log.Debug($"Unmapped key vk=0x{e.VirtualKey:X2} sc=0x{e.ScanCode:X3} ext={e.Extended} (passed to Windows)");
            return false;
        }

        var changed = e.IsUp ? _keyboard.Release(usage) : _keyboard.Press(usage);
        if (changed)
        {
            Log.Debug($"Key {(e.IsUp ? "up" : "down")} vk=0x{e.VirtualKey:X2} sc=0x{e.ScanCode:X3} ext={e.Extended} -> usage 0x{usage:X2}");
            _peripheral.SendKeyboardReport(_keyboard.ToReport());
        }

        return true;
    }

    private void OnAbsoluteMoved(double nx, double ny)
    {
        _absoluteX = (ushort)Math.Round(nx * HidReportMaps.AbsoluteMax);
        _absoluteY = (ushort)Math.Round(ny * HidReportMaps.AbsoluteMax);
        _peripheral?.SendMouseAbsolute(_mouseButtons, _absoluteX, _absoluteY);
    }

    private void OnMouseButtonChanged(byte bit, bool pressed)
    {
        _mouseButtons = pressed ? (byte)(_mouseButtons | bit) : (byte)(_mouseButtons & ~bit);
        SendMouseState(wheel: 0);
    }

    private void OnWheelScrolled(int notches) => SendMouseState(notches);

    private void SendMouseState(int wheel)
    {
        if (_peripheral is null)
        {
            return;
        }

        if (_peripheral.Mode == PointerMode.Absolute)
        {
            _peripheral.SendMouseAbsolute(_mouseButtons, _absoluteX, _absoluteY, wheel);
        }
        else
        {
            _peripheral.SendMouseRelative(_mouseButtons, 0, 0, wheel);
        }
    }

    private void ReleaseAll(string reason)
    {
        if (_peripheral is null)
        {
            _keyboard.Clear();
            _mouseButtons = 0;
            return;
        }

        if (!_keyboard.IsEmpty)
        {
            _keyboard.Clear();
            _peripheral.SendKeyboardReport(_keyboard.ToReport());
            Log.Info($"Released all keys ({reason}).");
        }

        if (_mouseButtons != 0)
        {
            _mouseButtons = 0;
            SendMouseState(wheel: 0);
            Log.Info($"Released mouse buttons ({reason}).");
        }
    }

    /// <summary>
    /// Types "Zürich 123" assuming the iPhone hardware keyboard layout is Swiss German.
    /// Z and ü are sent from their Swiss positions (US Y and US [), so the output also
    /// tells which layout the iPhone is really using.
    /// </summary>
    private void TypeTestString()
    {
        if (_peripheral is null)
        {
            return;
        }

        (KeyModifiers Modifiers, byte Usage)[] keys =
        [
            (KeyModifiers.LeftShift, 0x1C), // Z on Swiss German (Y on US)
            (KeyModifiers.None, 0x2F),      // ü on Swiss German ([ on US, è on Italian)
            (KeyModifiers.None, 0x15),      // r
            (KeyModifiers.None, 0x0C),      // i
            (KeyModifiers.None, 0x06),      // c
            (KeyModifiers.None, 0x0B),      // h
            (KeyModifiers.None, 0x2C),      // space
            (KeyModifiers.None, 0x1E),      // 1
            (KeyModifiers.None, 0x1F),      // 2
            (KeyModifiers.None, 0x20),      // 3
        ];

        ReleaseAll("test string");
        foreach (var (modifiers, usage) in keys)
        {
            _peripheral.SendKeyboardReport(KeyboardState.BuildReport((byte)modifiers, [usage]));
            _peripheral.SendKeyboardReport(KeyboardState.BuildReport(0, []));
        }

        Log.Info("Test string sent. Expected on iPhone: 'Zürich 123' (Swiss German). " +
                 "'Y[rich 123' = US layout, 'Yèrich 123' = Italian layout.");
    }

    private void SendChord(KeyModifiers modifiers, byte usage, string name)
    {
        if (_peripheral is null)
        {
            return;
        }

        ReleaseAll(name);
        _peripheral.SendKeyboardReport(KeyboardState.BuildReport((byte)modifiers, [usage]));
        _peripheral.SendKeyboardReport(KeyboardState.BuildReport(0, []));
        Log.Info($"Sent {name}.");
    }

    private void SendSelectedConsumer()
    {
        var match = ConsumerUsageRegex().Match(_consumerCombo.Text);
        if (!match.Success)
        {
            MessageBox.Show(this, "Scrivi un codice esadecimale, es. 0x0223", "Codice non valido");
            return;
        }

        var usage = Convert.ToUInt16(match.Groups[1].Value, 16);
        Log.Info($"Sending consumer usage 0x{usage:X4} ({_consumerCombo.Text.Trim()})");
        _peripheral?.SendConsumerClick(usage);
    }

    private void MovePointerToTopLeft()
    {
        // iOS clamps the pointer at the screen edge, so a large movement parks it in the corner.
        _peripheral?.SendMouseRelative(_mouseButtons, -127 * 40, -127 * 40);
        Log.Info("Pointer sent to the top-left corner (40 x (-127,-127)).");
    }

    private static void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the log folder", ex);
        }
    }

    private void OnLineLogged(LogLevel level, string line)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_logBox.TextLength > MaxLogChars)
            {
                _logBox.Text = _logBox.Text[^(MaxLogChars / 2)..];
            }

            _logBox.AppendText(line + Environment.NewLine);
        });
    }

    [GeneratedRegex(@"0x([0-9A-Fa-f]{1,4})")]
    private static partial Regex ConsumerUsageRegex();
}
