using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Coreloom.Mac.Services;

namespace Coreloom.Mac.Controls;

/// <summary>
/// The window's one background, behind everything and flowing all around the floating sidebar: the color, gradient or
/// picture chosen in Settings › Appearance (<see cref="Backdrop"/>), stretched to the window. The glass sidebar and cards
/// measure their place against this control to show the frosted part of it that's behind them. See-through, it draws
/// only <see cref="Background"/> (the theme's color, mostly clear) over the desktop macOS shows through the window.
/// </summary>
public sealed class WindowBackdrop : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<WindowBackdrop>();

    static WindowBackdrop()
    {
        AffectsRender<WindowBackdrop>(BackgroundProperty);
    }

    public WindowBackdrop()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    /// <summary>What shows until the background is drawn, and the tint over the desktop when see-through.</summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Backdrop.Changed += OnBackdropChanged;
        Backdrop.Attach(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Backdrop.Changed -= OnBackdropChanged;
        Backdrop.Detach(this);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Backdrop.HostResized();
    }

    private void OnBackdropChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (!Backdrop.IsSeeThrough && Backdrop.Background is { } picture)
        {
            context.DrawImage(picture, new Rect(picture.Size), bounds);
            return;
        }

        if (Background is { } background)
        {
            context.DrawRectangle(background, null, bounds);
        }
    }
}
