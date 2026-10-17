using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;

namespace Coreloom.App.Overlay;

/// <summary>
/// A system-wide hotkey that works while a game has the keyboard. It watches key presses with a low-level keyboard
/// hook on its own thread (so a busy UI never delays anyone's typing) and never swallows the key: the game or app in
/// front still gets it. (RegisterHotKey would take the key away from every other program, which is no good for a
/// single key like End.)
/// </summary>
internal sealed unsafe partial class GlobalHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly Action _pressed;
    private readonly Action? _pressedWithShift;
    private readonly HookProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private volatile int _key;
    private volatile int _modifiers;
    private volatile bool _paused;
    private bool _down;

    /// <param name="pressed">Called on the hook thread when the hotkey goes down: keep it short (post to the UI).</param>
    /// <param name="pressedWithShift">Called when the hotkey goes down with Shift added (a hotkey that has no Shift of its own).</param>
    public GlobalHotkey(Action pressed, Action? pressedWithShift = null)
    {
        _pressed = pressed;
        _pressedWithShift = pressedWithShift;
        _proc = OnKey; // kept in a field: Windows calls it for as long as the hook is installed
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>Ignores the hotkey for a while (e.g. while a new one is being typed in Coreloom).</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>Watches for this key and modifiers; key 0 removes the hook.</summary>
    public void Set(int key, HotkeyModifiers modifiers)
    {
        _modifiers = (int)modifiers;
        _key = key;
        if (key == 0)
        {
            Stop();
        }
        else if (_thread is null)
        {
            Start();
        }
    }

    public void Dispose() => Stop();

    private void Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Coreloom hotkey" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Stop()
    {
        var thread = _thread;
        if (thread is null)
        {
            return;
        }

        _thread = null;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        thread.Join(TimeSpan.FromSeconds(2));
        _threadId = 0;
    }

    private void Run(ManualResetEventSlim ready)
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WhKeyboardLl, Marshal.GetFunctionPointerForDelegate(_proc), GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                Log.Warn("Overlay", $"The overlay hotkey couldn't be installed (error {Marshal.GetLastPInvokeError()}).");
                return;
            }
        }
        finally
        {
            ready.Set();
        }

        try
        {
            Message message;
            while (GetMessage(&message, IntPtr.Zero, 0, 0) > 0)
            {
                // Nothing to dispatch: the loop only lets Windows call the hook.
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            int key = _key;
            if (code >= 0 && key != 0 && ((KeyboardInfo*)lParam)->VirtualKey == key)
            {
                int message = (int)wParam;
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    if (!_down)
                    {
                        _down = true; // auto-repeat while held doesn't count again
                        var wanted = (HotkeyModifiers)_modifiers;
                        if (!_paused && ModifiersMatch(wanted))
                        {
                            _pressed();
                        }
                        else if (!_paused && _pressedWithShift is not null && !wanted.HasFlag(HotkeyModifiers.Shift)
                            && ModifiersMatch(wanted | HotkeyModifiers.Shift))
                        {
                            _pressedWithShift();
                        }
                    }
                }
                else if (message is WmKeyUp or WmSysKeyUp)
                {
                    _down = false;
                }
            }
        }
        catch
        {
            // Never let an exception unwind into Windows' keyboard handling.
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool ModifiersMatch(HotkeyModifiers wanted) =>
        IsDown(VkControl) == wanted.HasFlag(HotkeyModifiers.Control)
        && IsDown(VkMenu) == wanted.HasFlag(HotkeyModifiers.Alt)
        && IsDown(VkShift) == wanted.HasFlag(HotkeyModifiers.Shift)
        && (IsDown(VkLWin) || IsDown(VkRWin)) == wanted.HasFlag(HotkeyModifiers.Windows);

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInfo
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(int hookId, IntPtr proc, IntPtr module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(Message* message, IntPtr window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle(string? name);
}
