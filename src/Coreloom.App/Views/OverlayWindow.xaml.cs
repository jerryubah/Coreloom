using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.App.Views;

/// <summary>
/// The in-game overlay: a small topmost window that clicks pass through, that never takes focus and that isn't in the
/// taskbar or Alt+Tab. Shown over windowed and borderless games (and over exclusive-fullscreen games where Windows'
/// fullscreen optimizations apply).
/// <para>
/// It is drawn on the processor, not the graphics card: a see-through WPF window drawn on the graphics card has every
/// changed frame copied back from the card for Windows to blend, which makes the app wait on a card the game is keeping
/// busy. Drawn on the processor, the overlay never queues work on the graphics card or waits for it; Windows only
/// blends the finished picture over the game. The window is small and changes only twice a second (and the frame-time
/// graph while a game draws), so this costs little.
/// </para>
/// <para>
/// While it is being moved (<see cref="OverlayViewModel.IsMoving"/>) it takes clicks: dragging it moves the window, the
/// round handle scales it, and <see cref="Adjusted"/> tells the app to save where it ended up.
/// </para>
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExAppWindow = 0x00040000;

    private readonly OverlayViewModel _overlay;
    private bool _movable;
    private bool _dragging;
    private bool _resizing;
    private int _resizeStartX;
    private double _resizeStartScale;
    private double _resizeStartWidth;

    public OverlayWindow(OverlayViewModel overlay)
    {
        InitializeComponent();
        _overlay = overlay;
        DataContext = overlay;
    }

    /// <summary>The overlay was dragged or resized: the app saves its new place (and size).</summary>
    public event EventHandler? Adjusted;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Being dragged or resized right now (the app doesn't move it meanwhile).</summary>
    public bool IsAdjusting => _dragging || _resizing;

    /// <summary>Takes clicks (to be moved) or lets them through to the game.</summary>
    public void SetMovable(bool movable)
    {
        if (movable == _movable)
        {
            return;
        }

        _movable = movable;
        IsHitTestVisible = movable;
        IntPtr hwnd = Handle;
        if (hwnd != IntPtr.Zero)
        {
            nint style = GetWindowLongPtr(hwnd, GwlExStyle);
            style = movable ? style & ~(nint)WsExTransparent : style | (nint)WsExTransparent;
            SetWindowLongPtr(hwnd, GwlExStyle, style);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = Handle;
        nint style = GetWindowLongPtr(hwnd, GwlExStyle);
        style |= (nint)(WsExToolWindow | WsExLayered | WsExNoActivate);
        style = _movable ? style & ~(nint)WsExTransparent : style | (nint)WsExTransparent;
        style &= ~(nint)WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, style);

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.RenderMode = RenderMode.SoftwareOnly;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_movable || e.Handled || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        _dragging = true;
        try
        {
            DragMove(); // until the button is released
        }
        catch (InvalidOperationException)
        {
            // the button was already up
        }
        finally
        {
            _dragging = false;
            Adjusted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        _resizing = true;
        _resizeStartX = CursorX();
        _resizeStartScale = _overlay.LiveScale ?? _overlay.OwnScale;
        _resizeStartWidth = Math.Max(40, ActualWidth);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        // Screen pixels (not the handle's own coordinates, which move as the overlay grows) turned into this window's units.
        double dpi = VisualTreeHelperDpi();
        double dx = (CursorX() - _resizeStartX) / dpi;
        _overlay.LiveScale = _resizeStartScale * Math.Max(0.25, (_resizeStartWidth + dx) / _resizeStartWidth);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizing = false;
        Adjusted?.Invoke(this, EventArgs.Empty);
    }

    private double VisualTreeHelperDpi() => System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX is > 0 and var scale ? scale : 1;

    private static int CursorX() => GetCursorPos(out var point) ? point.X : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);
}
