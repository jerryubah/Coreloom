using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Coreloom.App.Interop;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels;

namespace Coreloom.App.Services;

/// <summary>
/// Notices when a game is in front (a program filling its screen, or one of the library's games) and tells the shell,
/// which then reads sensors less often and at low priority and stops redrawing a window the game covers; Coreloom's
/// process also gives way to the game. Checks every two seconds with a few cheap window calls.
/// </summary>
internal sealed partial class GamingModeService : IDisposable
{
    private const string LogCategory = "Monitoring";

    private readonly ShellViewModel _shell;
    private readonly Func<System.Windows.Window?> _mainWindow;
    private readonly Func<bool> _lighterWhileGaming;
    private readonly DispatcherTimer _timer;
    private int _lastProcessId = -1;
    private string? _lastPath;
    private bool _gaming;

    public GamingModeService(ShellViewModel shell, Func<System.Windows.Window?> mainWindow, Func<bool> lighterWhileGaming, Dispatcher dispatcher)
    {
        _shell = shell;
        _mainWindow = mainWindow;
        _lighterWhileGaming = lighterWhileGaming;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Check();
    }

    /// <summary>The program in front when a game is (for the log and adaptive pacing), else null.</summary>
    public string? GameProgram { get; private set; }

    public int? GameProcessId { get; private set; }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    public void Dispose()
    {
        _timer.Stop();
        ProcessPriority.GiveWay("gaming", false);
    }

    private void Check()
    {
        try
        {
            IntPtr foreground = GetForegroundWindow();
            int pid = foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out uint id) != 0 ? (int)id : 0;
            bool other = pid != 0 && pid != Environment.ProcessId;
            bool desktop = foreground != IntPtr.Zero && IsShellWindow(foreground);
            bool fills = other && !desktop && FillsItsScreen(foreground);
            string? path = other ? PathOf(pid) : null;
            bool known = path is not null && _shell.Gaming.LibrarySnapshot() is { Count: > 0 } library
                && GameLibrary.FindRunning(library, [new RunningProcess(pid, path)]) is not null;
            bool gaming = AdaptiveMonitoring.IsGameInFront(other, fills, known, desktop);
            bool covers = gaming && fills && CoversMainWindow(foreground);

            _shell.SetGameInFront(gaming, covers);
            ProcessPriority.GiveWay("gaming", gaming && _lighterWhileGaming());
            GameProgram = gaming ? path : null;
            GameProcessId = gaming ? pid : null;
            if (gaming != _gaming)
            {
                _gaming = gaming;
                Log.Info(LogCategory, gaming
                    ? $"Game in front: {System.IO.Path.GetFileName(path) ?? "a fullscreen program"}{(_lighterWhileGaming() ? $"; lighter monitoring (sensors every {_shell.SensorInterval.TotalSeconds:0.#} s)" : string.Empty)}."
                    : $"No game in front; sensors every {_shell.SensorInterval.TotalSeconds:0.#} s.");
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ExternalException)
        {
            Log.WarnThrottled("gaming-mode", TimeSpan.FromMinutes(10), LogCategory, "Checking for a game in front failed.", e);
        }
    }

    private string? PathOf(int pid)
    {
        if (pid != _lastProcessId)
        {
            _lastProcessId = pid;
            _lastPath = GamingNative.ProgramPath(pid);
        }

        return _lastPath;
    }

    /// <summary>The desktop and the taskbar aren't games, even though they fill the screen.</summary>
    private static unsafe bool IsShellWindow(IntPtr hwnd)
    {
        char* name = stackalloc char[64];
        int length = GetClassNameW(hwnd, name, 64);
        string cls = length > 0 ? new string(name, 0, length) : string.Empty;
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static bool FillsItsScreen(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        IntPtr monitor = MonitorFromWindow(hwnd, 2); // nearest
        return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info)
            && window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
    }

    /// <summary>The game fills the screen Coreloom's window is on (so the window can't be seen).</summary>
    private bool CoversMainWindow(IntPtr game)
    {
        if (_mainWindow() is not { IsVisible: true } window || window.WindowState == System.Windows.WindowState.Minimized)
        {
            return false;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && MonitorFromWindow(handle, 2) == MonitorFromWindow(game, 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetClassNameW(IntPtr hwnd, char* name, int maxCount);
}
