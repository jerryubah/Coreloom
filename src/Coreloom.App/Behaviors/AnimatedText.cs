using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Tweens a TextBlock's number between readings: bind <c>Value</c> (raw double) and <c>Formatter</c>
/// (Func&lt;double,string&gt;). The displayed text eases to each new value over ~350 ms at 60 fps, which keeps
/// the motion fluid without burning CPU. Disabled globally by "Reduced motion".
/// </summary>
public static class AnimatedText
{
    public static bool IsGloballyEnabled { get; set; } = true;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(AnimatedText), new PropertyMetadata(double.NaN, OnValueChanged));

    public static readonly DependencyProperty FormatterProperty = DependencyProperty.RegisterAttached(
        "Formatter", typeof(Func<double, string>), typeof(AnimatedText), new PropertyMetadata(null, OnFormatterChanged));

    /// <summary>Internal animated value; changes re-format the text.</summary>
    private static readonly DependencyProperty CurrentProperty = DependencyProperty.RegisterAttached(
        "Current", typeof(double), typeof(AnimatedText), new PropertyMetadata(double.NaN, OnCurrentChanged));

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    public static Func<double, string>? GetFormatter(DependencyObject element) => (Func<double, string>?)element.GetValue(FormatterProperty);

    public static void SetFormatter(DependencyObject element, Func<double, string>? value) => element.SetValue(FormatterProperty, value);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
        {
            return;
        }

        double from = (double)text.GetValue(CurrentProperty);
        double to = (double)e.NewValue;

        if (!double.IsFinite(to))
        {
            text.BeginAnimation(CurrentProperty, null);
            text.SetValue(CurrentProperty, double.NaN);
            text.Text = "—";
            return;
        }

        bool animate = IsGloballyEnabled && double.IsFinite(from) && text.IsVisible && Math.Abs(to - from) > 1e-9;
        if (!animate)
        {
            text.BeginAnimation(CurrentProperty, null);
            text.SetValue(CurrentProperty, to);
            Render(text, to);
            return;
        }

        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        // Numbers re-layout the TextBlock on every tick; 60 fps is as fast as a changing digit can be read.
        Timeline.SetDesiredFrameRate(animation, 60);
        text.BeginAnimation(CurrentProperty, animation);
    }

    private static void OnFormatterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock text)
        {
            Render(text, (double)text.GetValue(CurrentProperty));
        }
    }

    private static void OnCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock text)
        {
            Render(text, (double)e.NewValue);
        }
    }

    private static void Render(TextBlock text, double value)
    {
        if (!double.IsFinite(value))
        {
            text.Text = "—";
            return;
        }

        var formatter = GetFormatter(text);
        text.Text = formatter is null ? value.ToString("0", System.Globalization.CultureInfo.CurrentCulture) : formatter(value);
    }
}

/// <summary>Subtle entrance animation for pages and cards (fade + 8px rise). Skipped with reduced motion.</summary>
public static class Motion
{
    public static bool ReducedMotion { get; set; }

    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEntranceChanged));

    public static bool GetEntrance(DependencyObject element) => (bool)element.GetValue(EntranceProperty);

    public static void SetEntrance(DependencyObject element, bool value) => element.SetValue(EntranceProperty, value);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        // The handler is a static method on the element's own event, so it keeps nothing alive. It stays attached
        // because cached pages are unloaded and loaded again as you navigate, and each return plays the entrance.
        element.Loaded -= OnLoaded;
        if ((bool)e.NewValue)
        {
            element.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Since 1.9.0 the panel clears from a blur as its cards rise into place one after another (Stagger).
        Stagger.Play((FrameworkElement)sender);
    }
}
