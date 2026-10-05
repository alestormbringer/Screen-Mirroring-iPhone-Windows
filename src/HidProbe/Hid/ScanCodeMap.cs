namespace IPhoneMirror.HidProbe.Hid;

/// <summary>
/// Translates Windows keyboard events into HID usages.
/// HID keyboard usages are positional: the iPhone decides which character a key produces
/// according to its own hardware keyboard layout (Settings > General > Keyboard > Hardware Keyboard),
/// so we forward the physical key position (scan code) and never the character.
/// </summary>
internal static class ScanCodeMap
{
    // Usage IDs from the USB HID Usage Tables, Keyboard/Keypad page (0x07).
    public const byte UsageNone = 0x00;
    public const byte UsageGraveAccent = 0x35;   // ANSI ` ~  | ISO top-left key (CH: § °)
    public const byte UsageNonUsBackslash = 0x64; // ISO key between left Shift and Z (CH: < >)

    private const int Extended = 0xE000;

    // Set 1 scan codes (as reported by Windows) -> HID usage.
    private static readonly Dictionary<int, byte> s_scanToUsage = new()
    {
        [0x01] = 0x29, // Escape
        [0x02] = 0x1E, // 1
        [0x03] = 0x1F, // 2
        [0x04] = 0x20, // 3
        [0x05] = 0x21, // 4
        [0x06] = 0x22, // 5
        [0x07] = 0x23, // 6
        [0x08] = 0x24, // 7
        [0x09] = 0x25, // 8
        [0x0A] = 0x26, // 9
        [0x0B] = 0x27, // 0
        [0x0C] = 0x2D, // - (CH: ' ?)
        [0x0D] = 0x2E, // = (CH: ^ `)
        [0x0E] = 0x2A, // Backspace
        [0x0F] = 0x2B, // Tab
        [0x10] = 0x14, // Q
        [0x11] = 0x1A, // W
        [0x12] = 0x08, // E
        [0x13] = 0x15, // R
        [0x14] = 0x17, // T
        [0x15] = 0x1C, // Y (CH: Z)
        [0x16] = 0x18, // U
        [0x17] = 0x0C, // I
        [0x18] = 0x12, // O
        [0x19] = 0x13, // P
        [0x1A] = 0x2F, // [ (CH: ü è)
        [0x1B] = 0x30, // ] (CH: ¨ !)
        [0x1C] = 0x28, // Enter
        [0x1D] = 0xE0, // Left Control
        [0x1E] = 0x04, // A
        [0x1F] = 0x16, // S
        [0x20] = 0x07, // D
        [0x21] = 0x09, // F
        [0x22] = 0x0A, // G
        [0x23] = 0x0B, // H
        [0x24] = 0x0D, // J
        [0x25] = 0x0E, // K
        [0x26] = 0x0F, // L
        [0x27] = 0x33, // ; (CH: ö é)
        [0x28] = 0x34, // ' (CH: ä à)
        [0x29] = UsageGraveAccent, // ` (CH: § °)
        [0x2A] = 0xE1, // Left Shift
        // ANSI "\" or the ISO key left of Enter (CH: $ £). Apple keyboards report this
        // position as 0x31 and Apple platforms treat 0x32 (Non-US #) as an alias of it.
        [0x2B] = 0x31,
        [0x2C] = 0x1D, // Z (CH: Y)
        [0x2D] = 0x1B, // X
        [0x2E] = 0x06, // C
        [0x2F] = 0x19, // V
        [0x30] = 0x05, // B
        [0x31] = 0x11, // N
        [0x32] = 0x10, // M
        [0x33] = 0x36, // ,
        [0x34] = 0x37, // .
        [0x35] = 0x38, // / (CH: - _)
        [0x36] = 0xE5, // Right Shift
        [0x37] = 0x55, // Keypad *
        [0x38] = 0xE2, // Left Alt (Option on iOS)
        [0x39] = 0x2C, // Space
        [0x3A] = 0x39, // Caps Lock
        [0x3B] = 0x3A, // F1
        [0x3C] = 0x3B, // F2
        [0x3D] = 0x3C, // F3
        [0x3E] = 0x3D, // F4
        [0x3F] = 0x3E, // F5
        [0x40] = 0x3F, // F6
        [0x41] = 0x40, // F7
        [0x42] = 0x41, // F8
        [0x43] = 0x42, // F9
        [0x44] = 0x43, // F10
        [0x46] = 0x47, // Scroll Lock
        [0x47] = 0x5F, // Keypad 7
        [0x48] = 0x60, // Keypad 8
        [0x49] = 0x61, // Keypad 9
        [0x4A] = 0x56, // Keypad -
        [0x4B] = 0x5C, // Keypad 4
        [0x4C] = 0x5D, // Keypad 5
        [0x4D] = 0x5E, // Keypad 6
        [0x4E] = 0x57, // Keypad +
        [0x4F] = 0x59, // Keypad 1
        [0x50] = 0x5A, // Keypad 2
        [0x51] = 0x5B, // Keypad 3
        [0x52] = 0x62, // Keypad 0
        [0x53] = 0x63, // Keypad .
        [0x56] = UsageNonUsBackslash, // ISO < > (CH: < >)
        [0x57] = 0x44, // F11
        [0x58] = 0x45, // F12
        [0x59] = 0x67, // Keypad =
        [0x64] = 0x68, // F13
        [0x65] = 0x69, // F14
        [0x66] = 0x6A, // F15
        [0x67] = 0x6B, // F16
        [0x68] = 0x6C, // F17
        [0x69] = 0x6D, // F18
        [0x6A] = 0x6E, // F19
        [0x6B] = 0x6F, // F20
        [0x6C] = 0x70, // F21
        [0x6D] = 0x71, // F22
        [0x6E] = 0x72, // F23 (sent by the Copilot key together with Win+Shift)
        [0x70] = 0x88, // Katakana/Hiragana
        [0x73] = 0x87, // International1 (Ro)
        [0x76] = 0x73, // F24
        [0x79] = 0x8A, // Henkan
        [0x7B] = 0x8B, // Muhenkan
        [0x7D] = 0x89, // International3 (Yen)
        [0x7E] = 0x85, // Keypad , (Brazil)

        [Extended | 0x1C] = 0x58, // Keypad Enter
        [Extended | 0x1D] = 0xE4, // Right Control
        [Extended | 0x35] = 0x54, // Keypad /
        [Extended | 0x37] = 0x46, // Print Screen
        [Extended | 0x38] = 0xE6, // Right Alt / AltGr (Right Option on iOS)
        [Extended | 0x47] = 0x4A, // Home
        [Extended | 0x48] = 0x52, // Up
        [Extended | 0x49] = 0x4B, // Page Up
        [Extended | 0x4B] = 0x50, // Left
        [Extended | 0x4D] = 0x4F, // Right
        [Extended | 0x4F] = 0x4D, // End
        [Extended | 0x50] = 0x51, // Down
        [Extended | 0x51] = 0x4E, // Page Down
        [Extended | 0x52] = 0x49, // Insert
        [Extended | 0x53] = 0x4C, // Delete
        [Extended | 0x5B] = 0xE3, // Left Windows (Command on iOS)
        [Extended | 0x5C] = 0xE7, // Right Windows (Command on iOS)
        [Extended | 0x5D] = 0x65, // Application / Menu
    };

    // Virtual-key codes whose scan code is ambiguous (Pause/NumLock share 0x45) or special.
    private static readonly Dictionary<uint, byte> s_virtualKeyToUsage = new()
    {
        [0x13] = 0x48, // VK_PAUSE
        [0x90] = 0x53, // VK_NUMLOCK
        [0x2C] = 0x46, // VK_SNAPSHOT (Print Screen)
    };

    // Laptop media keys -> Consumer page (0x0C) usages.
    private static readonly Dictionary<uint, ushort> s_virtualKeyToConsumer = new()
    {
        [0xAD] = 0x00E2, // VK_VOLUME_MUTE -> Mute
        [0xAE] = 0x00EA, // VK_VOLUME_DOWN -> Volume Decrement
        [0xAF] = 0x00E9, // VK_VOLUME_UP -> Volume Increment
        [0xB0] = 0x00B5, // VK_MEDIA_NEXT_TRACK -> Scan Next Track
        [0xB1] = 0x00B6, // VK_MEDIA_PREV_TRACK -> Scan Previous Track
        [0xB2] = 0x00B7, // VK_MEDIA_STOP -> Stop
        [0xB3] = 0x00CD, // VK_MEDIA_PLAY_PAUSE -> Play/Pause
    };

    /// <summary>Returns the HID keyboard usage for a key, or <see cref="UsageNone"/> if unmapped.</summary>
    /// <param name="swapIsoKeys">
    /// Swap the two ISO keys (§ and &lt;). macOS is known to swap them for some non-Apple ISO
    /// keyboards; this lets the test check whether iOS does the same.
    /// </param>
    public static byte ToKeyboardUsage(uint scanCode, bool extended, uint virtualKey, bool swapIsoKeys)
    {
        if (!s_virtualKeyToUsage.TryGetValue(virtualKey, out var usage) &&
            !s_scanToUsage.TryGetValue((int)(scanCode & 0xFF) | (extended ? Extended : 0), out usage))
        {
            return UsageNone;
        }

        if (swapIsoKeys)
        {
            usage = usage switch
            {
                UsageGraveAccent => UsageNonUsBackslash,
                UsageNonUsBackslash => UsageGraveAccent,
                _ => usage,
            };
        }

        return usage;
    }

    /// <summary>Returns the Consumer page usage for a media key, or 0 if the key is not a media key.</summary>
    public static ushort ToConsumerUsage(uint virtualKey) =>
        s_virtualKeyToConsumer.TryGetValue(virtualKey, out var usage) ? usage : (ushort)0;

    public static bool IsModifier(byte usage) => usage is >= 0xE0 and <= 0xE7;
}
