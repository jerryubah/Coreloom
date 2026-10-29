using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Coreloom.Hardware.Native;

/// <summary>
/// A small see-through window drawn from a block of pixels: a per-pixel-alpha layered window that clicks pass through,
/// that never takes focus and isn't in the taskbar or Alt+Tab. The overlay's frame-time graph is drawn into one from its
/// own thread, so it redraws at an even pace however busy the app's window thread is.
/// <para>
/// The window is created, placed, shown, hidden and destroyed on the thread of its owner window, which keeps it just
/// above the owner. <see cref="Present"/> may be called from one other thread: it replaces the pixels, naming the
/// position and size the window already has (so nothing moves), which never waits on the window's thread, so neither
/// thread can hold the other up (both checked on Windows by the hardware tests, the pixels on screen too). Nothing is
/// drawn on the graphics card: Windows only blends the finished pixels over what is behind.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class LayeredSurface : IDisposable
{
    private const string ClassName = "CoreloomLayeredSurface";
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint UlwAlpha = 0x00000002;
    private const ushort ErrorClassAlreadyExists = 1410;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private static readonly object ClassGate = new();
    private static bool _registered;

    private readonly object _gate = new();
    private readonly IntPtr _hwnd;
    private IntPtr _dc;
    private IntPtr _bitmap;
    private IntPtr _previous;
    private uint* _bits;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private bool _disposed;

    private LayeredSurface(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>The window's handle.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>
    /// Creates the window (hidden), owned by <paramref name="owner"/> (zero for none). Call on the owner's thread, which
    /// must run a message loop.
    /// </summary>
    public static LayeredSurface Create(IntPtr owner)
    {
        IntPtr instance = GetModuleHandleW(IntPtr.Zero);
        RegisterClass(instance);
        IntPtr hwnd;
        fixed (char* name = ClassName)
        fixed (char* title = "Coreloom")
        {
            hwnd = CreateWindowExW(
                WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost,
                name, title, WsPopup, 0, 0, 1, 1, owner, IntPtr.Zero, instance, IntPtr.Zero);
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't create the layered window.");
        }

        MakeFrameless(hwnd);
        return new LayeredSurface(hwnd);
    }

    /// <summary>The size given to <see cref="Place"/> (pixels).</summary>
    public (int Width, int Height) Size
    {
        get
        {
            lock (_gate)
            {
                return (_width, _height);
            }
        }
    }

    /// <summary>
    /// Moves and sizes the window, in screen pixels (on the owner's thread). This is done with UpdateLayeredWindow
    /// itself, giving the position and size along with the pixels (clear after a resize).
    /// </summary>
    public void Place(int x, int y, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _x = x;
            _y = y;
            _width = width;
            _height = height;
            if (EnsureBitmap(width, height))
            {
                var position = new Point { X = x, Y = y };
                var size = new NativeSize { Width = width, Height = height };
                var source = default(Point);
                var blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (UpdateLayeredWindow(_hwnd, IntPtr.Zero, &position, &size, _dc, &source, 0, &blend, UlwAlpha))
                {
                    return;
                }
            }
        }

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>Shows or hides the window without activating it (on the owner's thread).</summary>
    public void SetVisible(bool visible)
    {
        if (!_disposed)
        {
            ShowWindow(_hwnd, visible ? SwShowNoActivate : SwHide);
        }
    }

    /// <summary>
    /// Puts <paramref name="pixels"/> (premultiplied BGRA, <paramref name="width"/> × <paramref name="height"/>, rows top
    /// to bottom) on screen. The size must be the one last given to <see cref="Place"/>; returns false when it isn't (the
    /// window was just resized: draw again at <see cref="Size"/>), when Windows refused, or after disposal.
    /// <para>
    /// The update names the window's position and size (the ones it already has, so nothing moves and the window's
    /// thread isn't involved): an update that leaves them out is accepted but doesn't change what is shown, as found
    /// with the on-screen test (1.4.3 shipped that way, and the graph never appeared).
    /// </para>
    /// </summary>
    public bool Present(ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || width != _width || height != _height || !EnsureBitmap(width, height))
            {
                return false;
            }

            pixels[..(width * height)].CopyTo(new Span<uint>(_bits, width * height));
            var source = default(Point);
            var blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            var position = new Point { X = _x, Y = _y };
            var size = new NativeSize { Width = width, Height = height };
            return UpdateLayeredWindow(_hwnd, IntPtr.Zero, &position, &size, _dc, &source, 0, &blend, UlwAlpha);
        }
    }

    /// <summary>Destroys the window (on the owner's thread, once nothing presents any more).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseBitmap();
            if (_dc != IntPtr.Zero)
            {
                DeleteDC(_dc);
                _dc = IntPtr.Zero;
            }
        }

        DestroyWindow(_hwnd);
    }

    private bool EnsureBitmap(int width, int height)
    {
        if (_bitmap != IntPtr.Zero && width == _bitmapWidth && height == _bitmapHeight)
        {
            return true;
        }

        if (_dc == IntPtr.Zero)
        {
            _dc = CreateCompatibleDC(IntPtr.Zero);
            if (_dc == IntPtr.Zero)
            {
                return false;
            }
        }

        ReleaseBitmap();
        var info = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // top-down rows
            Planes = 1,
            BitCount = 32,
            Compression = 0, // BI_RGB
        };
        void* bits;
        IntPtr bitmap = CreateDIBSection(_dc, &info, 0, &bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == null)
        {
            return false;
        }

        _bitmap = bitmap;
        _bits = (uint*)bits;
        _previous = SelectObject(_dc, bitmap);
        _bitmapWidth = width;
        _bitmapHeight = height;
        return true;
    }

    private void ReleaseBitmap()
    {
        if (_bitmap == IntPtr.Zero)
        {
            return;
        }

        SelectObject(_dc, _previous);
        DeleteObject(_bitmap);
        _bitmap = IntPtr.Zero;
        _bits = null;
        _bitmapWidth = _bitmapHeight = 0;
    }

    private static void RegisterClass(IntPtr instance)
    {
        lock (ClassGate)
        {
            if (_registered)
            {
                return;
            }

            IntPtr user32 = NativeLibrary.Load("user32.dll");
            IntPtr defWindowProc = NativeLibrary.GetExport(user32, "DefWindowProcW");
            fixed (char* name = ClassName)
            {
                var wc = new WindowClassEx
                {
                    Size = (uint)sizeof(WindowClassEx),
                    WndProc = defWindowProc,
                    Instance = instance,
                    ClassName = name,
                };
                if (RegisterClassExW(&wc) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't register the layered window class.");
                }
            }

            _registered = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public char* MenuName;
        public char* ClassName;
        public IntPtr IconSmall;
    }

    /// <summary>No border, round corners or non-client drawing from DWM (Windows 11 may otherwise frame the window).</summary>
    private static void MakeFrameless(IntPtr hwnd)
    {
        try
        {
            int value = 1; // DWMNCRP_DISABLED
            DwmSetWindowAttribute(hwnd, 2, ref value, sizeof(int));
            value = 1; // DWMWCP_DONOTROUND
            DwmSetWindowAttribute(hwnd, 33, ref value, sizeof(int));
            value = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(hwnd, 34, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Cosmetic only (older Windows).
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetModuleHandleW(IntPtr name);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WindowClassEx* windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dcDestination, Point* position, NativeSize* size, IntPtr dcSource, Point* source, uint colorKey, BlendFunction* blend, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(IntPtr dc, BitmapInfoHeader* info, uint usage, void** bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);
}
