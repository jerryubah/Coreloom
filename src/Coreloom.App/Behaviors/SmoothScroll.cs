using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Eased scrolling for the mouse wheel and touchpad: instead of jumping a few lines per notch, the view glides to where
/// the wheel sends it and slows smoothly into place; turning the wheel again while it moves adds to the trip. The innermost
/// scroll area that can still move takes the wheel (a list inside a page scrolls first, then the page). With reduced motion
/// it jumps straight there. Set <c>b:SmoothScroll.IsEnabled="True"</c> on a ScrollViewer (the page styles do).
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Pixels per wheel notch (Windows' 3 lines of 16 px, a touch more so long pages move briskly).</summary>
    private const double NotchPixels = 56;

    /// <summary>How quickly the view closes on its target (per second, exponential): about 90 % in 165 ms.</summary>
    private const double Sharpness = 14;

    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        viewer.ScrollChanged -= OnScrollChanged;
        viewer.Unloaded -= OnUnloaded;
        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.ScrollChanged += OnScrollChanged;
            viewer.Unloaded += OnUnloaded;
        }
        else if (Glides.TryGetValue(viewer, out var glide))
        {
            glide.Stop();
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer || e.Delta == 0)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 || viewer.CanContentScroll)
        {
            return; // zoom, sideways scrolling and item-by-item lists keep Windows' own behaviour
        }

        bool up = e.Delta > 0;
        if (InnerCanScroll(e.OriginalSource as DependencyObject, viewer, up))
        {
            return; // the area under the pointer still has room: it scrolls (smoothly, if it has this behaviour too)
        }

        if (!CanScroll(viewer, up))
        {
            return; // at the end: leave the wheel to whatever contains this one
        }

        e.Handled = true;
        var glide = Glides.GetValue(viewer, v => new Glide(v));
        glide.Add(-e.Delta / 120.0 * NotchPixels * Math.Max(1, SystemParameters.WheelScrollLines) / 3);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer && Glides.TryGetValue(viewer, out var glide))
        {
            glide.OnScrolled();
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer && Glides.TryGetValue(viewer, out var glide))
        {
            glide.Stop();
        }
    }

    private static bool CanScroll(ScrollViewer viewer, bool up) =>
        viewer.ScrollableHeight > 0.5 && (up ? viewer.VerticalOffset > 0.5 : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5);

    /// <summary>Whether a scroll area between the pointer and <paramref name="outer"/> can still move that way.</summary>
    private static bool InnerCanScroll(DependencyObject? source, ScrollViewer outer, bool up)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, outer); node = Parent(node))
        {
            if (node is ScrollViewer inner && inner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && CanScroll(inner, up))
            {
                return true;
            }
        }

        return false;

        static DependencyObject? Parent(DependencyObject node) =>
            node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    }

    /// <summary>One scroll area's glide toward its target offset, advanced once per frame.</summary>
    private sealed class Glide(ScrollViewer viewer)
    {
        private double _current;   // where the glide has the view now
        private double _target;    // where it is heading
        private double _lastSet = double.NaN;
        private bool _running;
        private TimeSpan _lastFrame;

        public void Add(double pixels)
        {
            if (!_running)
            {
                _current = viewer.VerticalOffset;
                _target = _current;
            }

            _target = Math.Clamp(_target + pixels, 0, viewer.ScrollableHeight);
            if (Motion.ReducedMotion || !viewer.IsVisible)
            {
                Stop();
                viewer.ScrollToVerticalOffset(_target);
                return;
            }

            if (!_running)
            {
                _running = true;
                _lastFrame = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        /// <summary>Something else moved the view (the scroll bar, the keyboard, bring-into-view): let it.</summary>
        public void OnScrolled()
        {
            if (_running && !double.IsNaN(_lastSet) && Math.Abs(viewer.VerticalOffset - _lastSet) > 1.5)
            {
                Stop();
            }
        }

        public void Stop()
        {
            if (_running)
            {
                _running = false;
                CompositionTarget.Rendering -= OnRendering;
            }

            _lastSet = double.NaN;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
            if (now == _lastFrame)
            {
                return;
            }

            double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0.001, 0.05);
            _lastFrame = now;
            _target = Math.Clamp(_target, 0, viewer.ScrollableHeight);   // the content may have changed size
            _current += (_target - _current) * (1 - Math.Exp(-Sharpness * dt));
            if (Math.Abs(_target - _current) < 0.4)
            {
                _current = _target;
            }

            _lastSet = _current;
            viewer.ScrollToVerticalOffset(_current);
            if (_current == _target)
            {
                Stop();
            }
        }
    }
}
