using System.Windows;
using System.Windows.Media;
using Coreloom.App.Services;

namespace Coreloom.App.Controls;

/// <summary>
/// The window's one background, behind everything and flowing all around the floating sidebar: the color, gradient or
/// picture chosen in Settings › Appearance (<see cref="Backdrop"/>), stretched to the window. The glass sidebar and cards
/// measure their place against this element to show the frosted part of it that's behind them. With Mica (see-through),
/// it draws only <see cref="Background"/>, the theme's color mostly clear over the desktop.
/// </summary>
public sealed class WindowBackdrop : FrameworkElement
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(WindowBackdrop), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public WindowBackdrop()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) =>
        {
            Backdrop.Changed += OnBackdropChanged;
            Backdrop.Attach(this);
        };
        Unloaded += (_, _) =>
        {
            Backdrop.Changed -= OnBackdropChanged;
            Backdrop.Detach(this);
        };
        SizeChanged += (_, _) => Backdrop.HostResized();
    }

    /// <summary>What shows until the background is drawn, and the tint over the desktop with Mica.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    private void OnBackdropChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (!Backdrop.IsSeeThrough && Backdrop.Background is { } picture)
        {
            dc.DrawImage(picture, bounds);
            return;
        }

        if (Background is { } background)
        {
            dc.DrawRectangle(background, null, bounds);
        }
    }
}
