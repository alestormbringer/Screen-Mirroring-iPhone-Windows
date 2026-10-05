namespace IPhoneMirror.HidProbe.Hid;

internal enum PointerMode
{
    /// <summary>Standard mouse: relative dx/dy (-127..127). Known to work on iOS via AssistiveTouch.</summary>
    Relative,

    /// <summary>Mouse usage with absolute X/Y (0..<see cref="HidReportMaps.AbsoluteMax"/>). Unverified on iOS.</summary>
    Absolute,
}

/// <summary>
/// HID report descriptors ("Report Map", characteristic 0x2A4B) for a single composite
/// device: keyboard + mouse + consumer control in one HID service, like real BLE
/// keyboard/mouse combos. The iPhone caches the report map at pairing time, so after
/// switching <see cref="PointerMode"/> the PC must be forgotten on the iPhone and paired again.
/// </summary>
internal static class HidReportMaps
{
    public const byte KeyboardReportId = 1;
    public const byte MouseReportId = 2;
    public const byte ConsumerReportId = 3;

    public const int KeyboardInputLength = 8;
    public const int KeyboardOutputLength = 1;
    public const int MouseRelativeLength = 4;
    public const int MouseAbsoluteLength = 6;
    public const int ConsumerLength = 2;

    /// <summary>Logical maximum of absolute X/Y (same convention as common "USB tablet" devices).</summary>
    public const ushort AbsoluteMax = 32767;

    public static int MouseLength(PointerMode mode) =>
        mode == PointerMode.Absolute ? MouseAbsoluteLength : MouseRelativeLength;

    public static byte[] Build(PointerMode mode) =>
        [.. Keyboard, .. (mode == PointerMode.Absolute ? MouseAbsolute : MouseRelative), .. Consumer];

    // Input (8 bytes): [modifiers, reserved, key1..key6]. Output (1 byte): LED bitmap.
    private static readonly byte[] Keyboard =
    [
        0x05, 0x01,       // Usage Page (Generic Desktop)
        0x09, 0x06,       // Usage (Keyboard)
        0xA1, 0x01,       // Collection (Application)
        0x85, KeyboardReportId, // Report ID (1)
        0x05, 0x07,       //   Usage Page (Keyboard/Keypad)
        0x19, 0xE0,       //   Usage Minimum (Left Control)
        0x29, 0xE7,       //   Usage Maximum (Right GUI)
        0x15, 0x00,       //   Logical Minimum (0)
        0x25, 0x01,       //   Logical Maximum (1)
        0x75, 0x01,       //   Report Size (1)
        0x95, 0x08,       //   Report Count (8)
        0x81, 0x02,       //   Input (Data, Var, Abs)        ; modifier bits
        0x75, 0x08,       //   Report Size (8)
        0x95, 0x01,       //   Report Count (1)
        0x81, 0x01,       //   Input (Const)                 ; reserved byte
        0x05, 0x08,       //   Usage Page (LEDs)
        0x19, 0x01,       //   Usage Minimum (Num Lock)
        0x29, 0x05,       //   Usage Maximum (Kana)
        0x75, 0x01,       //   Report Size (1)
        0x95, 0x05,       //   Report Count (5)
        0x91, 0x02,       //   Output (Data, Var, Abs)       ; LED bits
        0x75, 0x03,       //   Report Size (3)
        0x95, 0x01,       //   Report Count (1)
        0x91, 0x01,       //   Output (Const)                ; LED padding
        0x05, 0x07,       //   Usage Page (Keyboard/Keypad)
        0x19, 0x00,       //   Usage Minimum (0)
        0x29, 0xFF,       //   Usage Maximum (255)
        0x15, 0x00,       //   Logical Minimum (0)
        0x26, 0xFF, 0x00, //   Logical Maximum (255)
        0x75, 0x08,       //   Report Size (8)
        0x95, 0x06,       //   Report Count (6)
        0x81, 0x00,       //   Input (Data, Array, Abs)      ; up to 6 pressed keys
        0xC0,             // End Collection
    ];

    // Input (4 bytes): [buttons, dx, dy, wheel], all relative except buttons.
    private static readonly byte[] MouseRelative =
    [
        0x05, 0x01,       // Usage Page (Generic Desktop)
        0x09, 0x02,       // Usage (Mouse)
        0xA1, 0x01,       // Collection (Application)
        0x85, MouseReportId, // Report ID (2)
        0x09, 0x01,       //   Usage (Pointer)
        0xA1, 0x00,       //   Collection (Physical)
        0x05, 0x09,       //     Usage Page (Button)
        0x19, 0x01,       //     Usage Minimum (Button 1)
        0x29, 0x03,       //     Usage Maximum (Button 3)
        0x15, 0x00,       //     Logical Minimum (0)
        0x25, 0x01,       //     Logical Maximum (1)
        0x75, 0x01,       //     Report Size (1)
        0x95, 0x03,       //     Report Count (3)
        0x81, 0x02,       //     Input (Data, Var, Abs)      ; left, right, middle
        0x75, 0x05,       //     Report Size (5)
        0x95, 0x01,       //     Report Count (1)
        0x81, 0x01,       //     Input (Const)               ; padding
        0x05, 0x01,       //     Usage Page (Generic Desktop)
        0x09, 0x30,       //     Usage (X)
        0x09, 0x31,       //     Usage (Y)
        0x09, 0x38,       //     Usage (Wheel)
        0x15, 0x81,       //     Logical Minimum (-127)
        0x25, 0x7F,       //     Logical Maximum (127)
        0x75, 0x08,       //     Report Size (8)
        0x95, 0x03,       //     Report Count (3)
        0x81, 0x06,       //     Input (Data, Var, Rel)      ; dx, dy, wheel
        0xC0,             //   End Collection
        0xC0,             // End Collection
    ];

    // Input (6 bytes): [buttons, X lo, X hi, Y lo, Y hi, wheel]. X/Y absolute, wheel relative.
    private static readonly byte[] MouseAbsolute =
    [
        0x05, 0x01,       // Usage Page (Generic Desktop)
        0x09, 0x02,       // Usage (Mouse)
        0xA1, 0x01,       // Collection (Application)
        0x85, MouseReportId, // Report ID (2)
        0x09, 0x01,       //   Usage (Pointer)
        0xA1, 0x00,       //   Collection (Physical)
        0x05, 0x09,       //     Usage Page (Button)
        0x19, 0x01,       //     Usage Minimum (Button 1)
        0x29, 0x03,       //     Usage Maximum (Button 3)
        0x15, 0x00,       //     Logical Minimum (0)
        0x25, 0x01,       //     Logical Maximum (1)
        0x75, 0x01,       //     Report Size (1)
        0x95, 0x03,       //     Report Count (3)
        0x81, 0x02,       //     Input (Data, Var, Abs)      ; left, right, middle
        0x75, 0x05,       //     Report Size (5)
        0x95, 0x01,       //     Report Count (1)
        0x81, 0x01,       //     Input (Const)               ; padding
        0x05, 0x01,       //     Usage Page (Generic Desktop)
        0x09, 0x30,       //     Usage (X)
        0x09, 0x31,       //     Usage (Y)
        0x15, 0x00,       //     Logical Minimum (0)
        0x26, (byte)(AbsoluteMax & 0xFF), (byte)(AbsoluteMax >> 8), // Logical Maximum (32767)
        0x75, 0x10,       //     Report Size (16)
        0x95, 0x02,       //     Report Count (2)
        0x81, 0x02,       //     Input (Data, Var, Abs)      ; X, Y
        0x09, 0x38,       //     Usage (Wheel)
        0x15, 0x81,       //     Logical Minimum (-127)
        0x25, 0x7F,       //     Logical Maximum (127)
        0x75, 0x08,       //     Report Size (8)
        0x95, 0x01,       //     Report Count (1)
        0x81, 0x06,       //     Input (Data, Var, Rel)      ; wheel
        0xC0,             //   End Collection
        0xC0,             // End Collection
    ];

    // Input (2 bytes): one 16-bit Consumer usage (array), 0 = released.
    private static readonly byte[] Consumer =
    [
        0x05, 0x0C,       // Usage Page (Consumer)
        0x09, 0x01,       // Usage (Consumer Control)
        0xA1, 0x01,       // Collection (Application)
        0x85, ConsumerReportId, // Report ID (3)
        0x19, 0x00,       //   Usage Minimum (0)
        0x2A, 0xFF, 0x03, //   Usage Maximum (0x3FF)
        0x15, 0x00,       //   Logical Minimum (0)
        0x26, 0xFF, 0x03, //   Logical Maximum (0x3FF)
        0x75, 0x10,       //   Report Size (16)
        0x95, 0x01,       //   Report Count (1)
        0x81, 0x00,       //   Input (Data, Array, Abs)
        0xC0,             // End Collection
    ];
}
