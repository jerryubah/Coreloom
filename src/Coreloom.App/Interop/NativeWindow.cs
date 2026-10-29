using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Coreloom.App.Interop;

/// <summary>
/// DWM integration: immersive dark title bar, rounded corners and the Windows 11 Mica backdrop, plus the window
/// tricks the startup splash handoff needs (cloaking, tool-window style, raising without activation).
/// </summary>
internal static partial class NativeWindow
{
    private const int DwmwaCloak = 13;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRound = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2; // Mica

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr64(IntPtr hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    /// <summary>System backdrop types (Mica) require Windows 11 22H2 (build 22621) or later.</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static void ApplyBackdrop(Window window, bool dark, bool mica)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return; // applied again from SourceInitialized
        }

        try
        {
            int darkValue = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref darkValue, sizeof(int));
            }

            if (IsWindows11)
            {
                int corner = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            }

            // Mica needs the frame extended over the whole client area (GlassFrameThickness = -1) and
            // transparent client pixels where it should show through. Without Mica, no frame extension is
            // used, so DWM never draws its own caption buttons behind Coreloom's.
            if (WindowChrome.GetWindowChrome(window) is { } chrome)
            {
                var desired = mica ? new Thickness(-1) : new Thickness(0);
                if (chrome.GlassFrameThickness != desired)
                {
                    var updated = (WindowChrome)chrome.Clone();
                    updated.GlassFrameThickness = desired;
                    WindowChrome.SetWindowChrome(window, updated);
                }
            }

            var source = HwndSource.FromHwnd(hwnd);
            if (mica)
            {
                if (source?.CompositionTarget is { } target)
                {
                    target.BackgroundColor = Colors.Transparent;
                }

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int type = DwmsbtMainWindow;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
            }
            else if (IsMicaSupported)
            {
                int type = DwmsbtNone;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // DWM attributes are cosmetic; ignore on unsupported systems.
        }
    }

    /// <summary>
    /// Asks DWM to draw nothing of its own around a window that is drawn to its edges, such as the in-game overlay: no
    /// border (Windows 11 draws a thin one, grey in the dark theme, around windows with round corners), no round
    /// corners, and no non-client rendering. Ignored where these attributes don't exist (Windows 10).
    /// </summary>
    public static void MakeFrameless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int policy = DwmncrpDisabled;
            DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
            if (IsWindows11)
            {
                int corner = DwmwcpDoNotRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
                int none = DwmwaColorNone;
                DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Cosmetic only.
        }
    }

    /// <summary>
    /// Hides (cloaks) or reveals a window at the compositor level. A cloaked window is shown, laid out and rendered
    /// normally but not displayed, so the main window can paint its first frame out of sight and then appear
    /// complete, with no blank or white frame. Returns false where cloaking isn't available (the caller then simply
    /// shows the window as before).
    /// </summary>
    public static bool SetCloaked(IntPtr hwnd, bool cloaked)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            int value = cloaked ? 1 : 0;
            return DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int)) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Tool-window, non-activating style for the splash: no taskbar button, not in Alt+Tab, never takes focus.</summary>
    public static void MakeSplash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (IntPtr.Size == 8)
            {
                long style = GetWindowLongPtr64(hwnd, GwlExStyle);
                style = (style | WsExToolWindow | WsExNoActivate) & ~(long)WsExAppWindow;
                SetWindowLongPtr64(hwnd, GwlExStyle, (nint)style);
            }
            else
            {
                int style = GetWindowLong32(hwnd, GwlExStyle);
                style = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
                SetWindowLong32(hwnd, GwlExStyle, style);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Cosmetic: without it the splash could appear in Alt+Tab for a moment.
        }
    }

    /// <summary>
    /// Puts <paramref name="hwnd"/> at the top of the normal (non-topmost) z-order without activating it. Used to keep
    /// the splash above the main window while it fades out. Safe to call for a window owned by another thread.
    /// </summary>
    public static bool BringToTopWithoutActivating(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SetWindowPos(hwnd, IntPtr.Zero /* HWND_TOP */, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool IsForeground(IntPtr hwnd) => hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    /// <summary>
    /// Margin that keeps content on-screen when a WindowChrome window is maximized (Windows extends the
    /// window by the resize border on every side).
    /// </summary>
    public static Thickness MaximizedInset(Window window)
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        double padded = GetSystemMetrics(92 /* SM_CXPADDEDBORDER */) / (scale <= 0 ? 1 : scale);
        return new Thickness(resize.Left + padded, resize.Top + padded, resize.Right + padded, resize.Bottom + padded);
    }
}
