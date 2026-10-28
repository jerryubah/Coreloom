using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Coreloom.Mac.Controls;

/// <summary>
/// The window's one background, flowing all around the floating sidebar. When the sidebar is translucent, it leaves a
/// hole the exact shape of the sidebar's glass, so the desktop's vibrancy (behind the window) shows through the glass
/// only, and nowhere else.
/// </summary>
public sealed class WindowBackdrop : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<WindowBackdrop>();

    public static readonly StyledProperty<SquircleBorder?> GlassProperty = AvaloniaProperty.Register<WindowBackdrop, SquircleBorder?>(nameof(Glass));

    public static readonly StyledProperty<bool> SeeThroughGlassProperty = AvaloniaProperty.Register<WindowBackdrop, bool>(nameof(SeeThroughGlass));

    static WindowBackdrop()
    {
        AffectsRender<WindowBackdrop>(BackgroundProperty, SeeThroughGlassProperty);
        GlassProperty.Changed.AddClassHandler<WindowBackdrop>((backdrop, e) => backdrop.Follow(e.OldValue as SquircleBorder, e.NewValue as SquircleBorder));
    }

    public WindowBackdrop()
    {
        IsHitTestVisible = false;
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The sidebar's glass, whose shape the hole takes.</summary>
    public SquircleBorder? Glass
    {
        get => GetValue(GlassProperty);
        set => SetValue(GlassProperty, value);
    }

    /// <summary>Leave the glass's shape open (the translucent sidebar).</summary>
    public bool SeeThroughGlass
    {
        get => GetValue(SeeThroughGlassProperty);
        set => SetValue(SeeThroughGlassProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is not { } background || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (SeeThroughGlass && Glass is { IsVisible: true } glass && glass.TranslatePoint(default, this) is { } origin && glass.Bounds.Width > 0)
        {
            var shape = Squircle.Create(new Rect(origin, glass.Bounds.Size), glass.CornerRadius, glass.Smoothing);
            context.DrawGeometry(background, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), shape));
            return;
        }

        context.DrawRectangle(background, null, bounds);
    }

    private void Follow(SquircleBorder? old, SquircleBorder? glass)
    {
        if (old is not null)
        {
            old.LayoutUpdated -= OnGlassLayoutUpdated;
        }

        if (glass is not null)
        {
            glass.LayoutUpdated += OnGlassLayoutUpdated;
        }

        InvalidateVisual();
    }

    private void OnGlassLayoutUpdated(object? sender, EventArgs e)
    {
        if (SeeThroughGlass)
        {
            InvalidateVisual();
        }
    }
}
