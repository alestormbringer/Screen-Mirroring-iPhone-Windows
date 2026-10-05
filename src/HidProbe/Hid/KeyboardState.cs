namespace IPhoneMirror.HidProbe.Hid;

/// <summary>Tracks pressed keys and produces the 8-byte boot-style keyboard input report.</summary>
internal sealed class KeyboardState
{
    private const int MaxKeys = 6;

    private readonly List<byte> _keys = new(MaxKeys);
    private byte _modifiers;

    public bool IsEmpty => _modifiers == 0 && _keys.Count == 0;

    /// <summary>Marks a key as pressed. Returns false if nothing changed (auto-repeat, rollover).</summary>
    public bool Press(byte usage)
    {
        if (ScanCodeMap.IsModifier(usage))
        {
            var bit = ModifierBit(usage);
            if ((_modifiers & bit) != 0)
            {
                return false;
            }

            _modifiers |= bit;
            return true;
        }

        if (_keys.Contains(usage) || _keys.Count >= MaxKeys)
        {
            return false;
        }

        _keys.Add(usage);
        return true;
    }

    /// <summary>Marks a key as released. Returns false if the key was not pressed.</summary>
    public bool Release(byte usage)
    {
        if (ScanCodeMap.IsModifier(usage))
        {
            var bit = ModifierBit(usage);
            if ((_modifiers & bit) == 0)
            {
                return false;
            }

            _modifiers &= (byte)~bit;
            return true;
        }

        return _keys.Remove(usage);
    }

    public void Clear()
    {
        _modifiers = 0;
        _keys.Clear();
    }

    public byte[] ToReport() => BuildReport(_modifiers, _keys);

    public static byte[] BuildReport(byte modifiers, IReadOnlyList<byte> keys)
    {
        var report = new byte[HidReportMaps.KeyboardInputLength];
        report[0] = modifiers;
        for (var i = 0; i < keys.Count && i < MaxKeys; i++)
        {
            report[2 + i] = keys[i];
        }

        return report;
    }

    public static byte ModifierBit(byte usage) => (byte)(1 << (usage - 0xE0));
}

/// <summary>Modifier bits of byte 0 of the keyboard report.</summary>
[Flags]
internal enum KeyModifiers : byte
{
    None = 0,
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftGui = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightGui = 0x80,
}
