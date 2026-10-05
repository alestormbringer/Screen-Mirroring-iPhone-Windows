using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IPhoneMirror.HidProbe.Input;

internal readonly record struct RawKeyEvent(uint VirtualKey, uint ScanCode, bool Extended, bool IsUp, bool Injected);

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). Unlike WM_KEYDOWN it also sees keys that
/// Windows would otherwise act on (Windows key, Alt+Tab) and lets us swallow them while the
/// probe forwards keys to the iPhone. The callback runs on the thread that installed the hook.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLKHF_UP = 0x80;

    private readonly Func<RawKeyEvent, bool> _handler;
    private readonly HookProc _proc; // Keep a reference so the delegate is not garbage collected.
    private IntPtr _hook;

    /// <param name="handler">Returns true to swallow the key (Windows and other apps will not see it).</param>
    public LowLevelKeyboardHook(Func<RawKeyEvent, bool> handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    public void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_KEYBOARD_LL) failed");
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var e = new RawKeyEvent(
                data.vkCode,
                data.scanCode,
                (data.flags & LLKHF_EXTENDED) != 0,
                (data.flags & LLKHF_UP) != 0,
                (data.flags & LLKHF_INJECTED) != 0);
            try
            {
                if (_handler(e))
                {
                    return 1;
                }
            }
            catch (Exception)
            {
                // Never let an exception escape into the OS hook chain.
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
