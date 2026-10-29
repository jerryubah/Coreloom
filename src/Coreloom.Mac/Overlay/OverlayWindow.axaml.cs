using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.Mac.Overlay;

/// <summary>
/// The readings window: a small window over every Space (full-screen games included) that clicks pass through and that
/// never takes focus. It is an ordinary window drawn by Coreloom, laid over the screen by macOS; nothing is added to the
/// game. While it is being moved (<see cref="OverlayViewModel.IsMoving"/>) it takes clicks: dragging moves it, the round
/// handle scales it, and <see cref="Adjusted"/> tells the service to save where it ended up.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel? _overlay;
    private bool _movable;
    private bool _dragging;
    private bool _resizing;
    private double _resizeStartX;
    private double _resizeStartScale;
    private double _resizeStartWidth;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public OverlayWindow(OverlayViewModel overlay)
        : this()
    {
        _overlay = overlay;
        DataContext = overlay;
        ResizeGrip.PointerPressed += OnGripPressed;
        ResizeGrip.PointerMoved += OnGripMoved;
        ResizeGrip.PointerReleased += OnGripReleased;
        Stack.SizeChanged += (_, _) => ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        Opened += (_, _) => OverlayNative.Configure(NativeHandle, clickThrough: !_movable);
    }

    /// <summary>The overlay was dragged or resized: the service saves its new place (and size).</summary>
    public event EventHandler? Adjusted;

    /// <summary>The readings took a new size (lines turned on or off, a new scale): the service sizes and places the window.</summary>
    public event EventHandler? ContentSizeChanged;

    /// <summary>The readings' own size (at the window's top left), whatever the window's size.</summary>
    public Size ContentSize => Stack.Bounds.Size is { Width: > 0 } arranged ? arranged : Stack.DesiredSize;

    /// <summary>The NSWindow, or 0 before it is shown.</summary>
    public nint NativeHandle => TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } handle ? handle.Handle : 0;

    /// <summary>Being dragged or resized right now (the service doesn't move it meanwhile).</summary>
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
        OverlayNative.SetClickThrough(NativeHandle, !movable);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_movable || e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        try
        {
            BeginMoveDrag(e); // AppKit tracks the drag until the button is released
        }
        finally
        {
            _dragging = false;
            Adjusted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_overlay is null || !_movable)
        {
            return;
        }

        _resizing = true;
        _resizeStartX = ScreenX(e);
        _resizeStartScale = _overlay.LiveScale ?? _overlay.OwnScale;
        _resizeStartWidth = Math.Max(40, ContentSize.Width);
        e.Pointer.Capture((IInputElement)sender!);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizing || _overlay is null)
        {
            return;
        }

        // Screen positions (not the handle's own, which moves as the window grows), in this window's units.
        double dx = (ScreenX(e) - _resizeStartX) / Math.Max(1, RenderScaling);
        _overlay.LiveScale = _resizeStartScale * Math.Max(0.25, (_resizeStartWidth + dx) / _resizeStartWidth);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        e.Pointer.Capture(null);
        Adjusted?.Invoke(this, EventArgs.Empty);
    }

    private double ScreenX(PointerEventArgs e) => this.PointToScreen(e.GetPosition(this)).X;
}
