#if !ANDROID
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Haven.Desktop.Overlay;

/// <summary>Registers one explicit system-wide Overlay shortcut without keyboard hooks or unrelated input capture.</summary>
internal sealed class OverlayGlobalHotkey : IAsyncDisposable
{
    // Win32 application hotkey identifiers must be in the 0x0000-0xBFFF range.
    private const int HotkeyId = 0x4841;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyH = 0x48;
    private const uint WmHotkey = 0x0312;
    private const uint WmUser = 0x0400;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _hotkeyId;
    private readonly uint _modifiers;
    private readonly uint _virtualKey;
    private Thread? _thread;
    private uint _threadId;
    private bool _started;
    private int _disposed;

    public OverlayGlobalHotkey() : this(HotkeyId, ModControl | ModShift | ModNoRepeat, VirtualKeyH)
    {
    }

    internal OverlayGlobalHotkey(int hotkeyId, uint modifiers, uint virtualKey)
    {
        _hotkeyId = hotkeyId;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
    }

    public event EventHandler? Pressed;
    public bool IsRegistered { get; private set; }
    public string ShortcutLabel => "Ctrl+Shift+H";
    public string? UnavailableReason { get; private set; }

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (_started) return IsRegistered;
        _started = true;

        if (!OperatingSystem.IsWindows())
        {
            UnavailableReason = "The desktop Overlay global shortcut requires Windows.";
            _ready.Set();
            return false;
        }

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "Haven Overlay hotkey"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(2)))
        {
            UnavailableReason = "Windows did not initialise the Overlay shortcut in time.";
            return false;
        }

        return IsRegistered;
    }

    private void MessageLoop()
    {
        _threadId = Native.GetCurrentThreadId();

        // PostThreadMessage and thread-scoped WM_HOTKEY delivery require this thread to own
        // a Win32 message queue before callers are told the listener is ready.
        _ = Native.PeekMessage(out _, IntPtr.Zero, WmUser, WmUser, PmNoRemove);

        if (!Native.RegisterHotKey(IntPtr.Zero, _hotkeyId, _modifiers, _virtualKey))
        {
            UnavailableReason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _ready.Set();
            return;
        }

        IsRegistered = true;
        _ready.Set();
        try
        {
            while (true)
            {
                var result = Native.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == 0) break;
                if (result < 0)
                {
                    UnavailableReason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    break;
                }

                if (message.Message == WmHotkey && message.WParam == (nint)_hotkeyId)
                    Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            Native.UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            IsRegistered = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        if (_threadId != 0) Native.PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        if (_thread is { IsAlive: true }) _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
#endif
