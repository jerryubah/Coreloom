using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Widens a scroll bar while the pointer is over it, so it is easy to grab, and narrows it again as soon as the mouse wheel
/// is used (you're scrolling, not grabbing) until the pointer moves on. Templates read <c>IsExpanded</c>.
/// </summary>
public static class ScrollBarHover
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollBarHover), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.RegisterAttached(
        "IsExpanded", typeof(bool), typeof(ScrollBarHover), new PropertyMetadata(false));

    /// <summary>Where the wheel was last used over the bar; it stays narrow until the pointer moves away from there.</summary>
    private static readonly DependencyProperty WheelPointProperty = DependencyProperty.RegisterAttached(
        "WheelPoint", typeof(Point?), typeof(ScrollBarHover), new PropertyMetadata(null));

    private const double MoveToExpand = 6;

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    public static bool GetIsExpanded(DependencyObject d) => (bool)d.GetValue(IsExpandedProperty);

    public static void SetIsExpanded(DependencyObject d, bool value) => d.SetValue(IsExpandedProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollBar bar)
        {
            return;
        }

        bar.MouseEnter -= OnMouseEnter;
        bar.MouseMove -= OnMouseMove;
        bar.MouseLeave -= OnMouseLeave;
        bar.RemoveHandler(UIElement.PreviewMouseWheelEvent, WheelHandler);
        bar.LostMouseCapture -= OnLostCapture;
        if ((bool)e.NewValue)
        {
            bar.MouseEnter += OnMouseEnter;
            bar.MouseMove += OnMouseMove;
            bar.MouseLeave += OnMouseLeave;
            // Also when the page's smooth scrolling has already taken the wheel (SmoothScroll marks it handled).
            bar.AddHandler(UIElement.PreviewMouseWheelEvent, WheelHandler, handledEventsToo: true);
            bar.LostMouseCapture += OnLostCapture;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        var bar = (ScrollBar)sender;
        bar.ClearValue(WheelPointProperty);
        SetIsExpanded(bar, true);
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var bar = (ScrollBar)sender;
        if (GetIsExpanded(bar))
        {
            return;
        }

        if (bar.GetValue(WheelPointProperty) is Point wheel && (e.GetPosition(bar) - wheel).Length < MoveToExpand)
        {
            return;
        }

        bar.ClearValue(WheelPointProperty);
        SetIsExpanded(bar, true);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        var bar = (ScrollBar)sender;
        if (bar.Track?.Thumb is { IsDragging: true })
        {
            return; // still dragging the thumb, just outside the bar
        }

        bar.ClearValue(WheelPointProperty);
        SetIsExpanded(bar, false);
    }

    private static readonly MouseWheelEventHandler WheelHandler = OnWheel;

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var bar = (ScrollBar)sender;
        if (bar.Track?.Thumb is { IsDragging: true })
        {
            return;
        }

        bar.SetValue(WheelPointProperty, e.GetPosition(bar));
        SetIsExpanded(bar, false); // the wheel still scrolls the page: the event carries on to the scroll viewer
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        var bar = (ScrollBar)sender;
        if (!bar.IsMouseOver)
        {
            SetIsExpanded(bar, false);
        }
    }
}
