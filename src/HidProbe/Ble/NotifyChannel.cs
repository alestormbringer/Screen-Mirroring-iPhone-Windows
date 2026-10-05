using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using IPhoneMirror.HidProbe.Diagnostics;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPhoneMirror.HidProbe.Ble;

/// <summary>
/// Sends input reports of one Report characteristic as GATT notifications, strictly in order
/// and one at a time. While a notification is in flight (or the minimum interval has not
/// elapsed) new reports are queued, and an optional merge function can coalesce them into
/// the queued tail (e.g. summing mouse deltas). This bounds latency instead of letting
/// reports pile up inside the Bluetooth stack.
/// </summary>
internal sealed class NotifyChannel
{
    private readonly GattLocalCharacteristic _characteristic;
    private readonly string _name;
    private readonly Func<byte[], byte[], byte[]?>? _merge;
    private readonly object _lock = new();
    private readonly LinkedList<byte[]> _queue = new();
    private readonly Stopwatch _sinceLastSend = Stopwatch.StartNew();
    private byte[] _lastValue;
    private bool _pumping;
    private long _sent;
    private long _dropped;

    public NotifyChannel(GattLocalCharacteristic characteristic, string name, int reportLength, Func<byte[], byte[], byte[]?>? merge = null)
    {
        _characteristic = characteristic;
        _name = name;
        _merge = merge;
        _lastValue = new byte[reportLength];
    }

    /// <summary>Minimum time between two notifications. 0 = send as fast as the stack accepts them.</summary>
    public int MinIntervalMs { get; set; }

    /// <summary>When true every report is written to the log file (Debug level).</summary>
    public bool LogReports { get; set; } = true;

    /// <summary>The last report sent, used to answer read requests on the characteristic.</summary>
    public byte[] LastValue
    {
        get
        {
            lock (_lock)
            {
                return (byte[])_lastValue.Clone();
            }
        }
    }

    public void Enqueue(byte[] report)
    {
        if (_characteristic.SubscribedClients.Count == 0)
        {
            // Nobody has enabled notifications yet (iPhone not connected / not subscribed).
            var dropped = Interlocked.Increment(ref _dropped);
            if (dropped == 1 || dropped % 100 == 0)
            {
                Log.Warn($"{_name}: no subscribed clients, report dropped (total dropped: {dropped}).");
            }

            return;
        }

        lock (_lock)
        {
            if (_merge is not null && _queue.Last is { } tail)
            {
                var merged = _merge(tail.Value, report);
                if (merged is not null)
                {
                    tail.Value = merged;
                    return;
                }
            }

            _queue.AddLast(report);
            if (_pumping)
            {
                return;
            }

            _pumping = true;
        }

        _ = PumpAsync();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            var wait = MinIntervalMs - (int)_sinceLastSend.ElapsedMilliseconds;
            if (wait > 0)
            {
                // Reports arriving meanwhile are queued (and possibly merged).
                await Task.Delay(wait).ConfigureAwait(false);
            }

            byte[] report;
            lock (_lock)
            {
                if (_queue.First is not { } head)
                {
                    _pumping = false;
                    return;
                }

                report = head.Value;
                _queue.RemoveFirst();
                _lastValue = report;
            }

            _sinceLastSend.Restart();
            var n = Interlocked.Increment(ref _sent);
            if (LogReports)
            {
                Log.Debug($"{_name} #{n}: {Convert.ToHexString(report)}");
            }

            try
            {
                var results = await _characteristic.NotifyValueAsync(report.AsBuffer()).AsTask().ConfigureAwait(false);
                foreach (var result in results)
                {
                    if (result.Status != GattCommunicationStatus.Success)
                    {
                        Log.Warn($"{_name} #{n}: notify to {result.SubscribedClient?.Session?.DeviceId?.Id} " +
                                 $"failed: {result.Status} (protocol error: {result.ProtocolError})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{_name} #{n}: NotifyValueAsync threw", ex);
            }
        }
    }
}
