using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Coreloom.App.Behaviors;

/// <summary>
/// The 1.9.0 motion. A page's content comes in from a blur to sharp while its cards rise into place one after another
/// (<see cref="Stagger"/>); an element that appears (the toast) rises from below (<see cref="Reveal"/>); a page's title
/// types itself in (<see cref="TypeIn"/>). Nothing moves with reduced motion.
/// </summary>
public static class Stagger
{
    /// <summary>How long one card takes to settle.</summary>
    public static readonly TimeSpan ItemDuration = TimeSpan.FromMilliseconds(300);

    /// <summary>The gap between one card starting and the next.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(42);

    /// <summary>Cards after this many all start with the last one (a long page doesn't keep the eye waiting).</summary>
    public const int MaxSteps = 10;

    /// <summary>
    /// Plays the entrance on <paramref name="root"/>: the whole panel from blurred and a little low to sharp and in place,
    /// and its children (or their children, for a plain panel of cards) rising in turn.
    /// </summary>
    public static void Play(FrameworkElement root)
    {
        if (Motion.ReducedMotion || !root.IsVisible)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // The panel: a blur that clears as it rises. The effect is dropped when done so text goes back to ClearType.
        var blur = new BlurEffect { Radius = 8, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        root.Effect = blur;
        var clear = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        clear.Completed += (_, _) =>
        {
            if (ReferenceEquals(root.Effect, blur))
            {
                root.Effect = null;
            }
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, clear);

        if (root.RenderTransform is not TranslateTransform translate || translate.IsFrozen)
        {
            translate = new TranslateTransform(0, 0);
            root.RenderTransform = translate;
        }

        root.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
        };
        root.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });

        int i = 0;
        foreach (var item in Items(root))
        {
            var delay = TimeSpan.FromMilliseconds(Step.TotalMilliseconds * Math.Min(i, MaxSteps));
            Rise(item, delay, 14, ease);
            i++;
        }
    }

    /// <summary>The things to stagger: each child of the panel, or the children of a child that is itself just a panel.</summary>
    private static IEnumerable<FrameworkElement> Items(FrameworkElement root)
    {
        if (root is not Panel panel)
        {
            yield break;
        }

        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement element || element.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (element is Panel inner && inner.Children.Count > 1 && inner.Children.Count <= 24)
            {
                foreach (UIElement grandchild in inner.Children)
                {
                    if (grandchild is FrameworkElement card && card.Visibility == Visibility.Visible)
                    {
                        yield return card;
                    }
                }
            }
            else
            {
                yield return element;
            }
        }
    }

    /// <summary>One element rising into place after <paramref name="delay"/>, from <paramref name="from"/> points low and clear.</summary>
    internal static void Rise(FrameworkElement element, TimeSpan delay, double from, IEasingFunction ease)
    {
        if (element.RenderTransform is not TranslateTransform translate || translate.IsFrozen)
        {
            translate = new TranslateTransform(0, 0);
            element.RenderTransform = translate;
        }

        element.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, ItemDuration) { BeginTime = delay, EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, ItemDuration) { BeginTime = delay, EasingFunction = ease });
    }
}

/// <summary>An element that appears (Visibility → Visible) rises from a few points below and fades in.</summary>
public static class Reveal
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty FromBelowProperty = DependencyProperty.RegisterAttached(
        "FromBelow", typeof(bool), typeof(Reveal), new PropertyMetadata(false));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetFromBelow(DependencyObject element) => (bool)element.GetValue(FromBelowProperty);

    public static void SetFromBelow(DependencyObject element, bool value) => element.SetValue(FromBelowProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;
        if ((bool)e.NewValue)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!(bool)e.NewValue || Motion.ReducedMotion)
        {
            return;
        }

        Stagger.Rise(element, TimeSpan.Zero, GetFromBelow(element) ? 18 : -10, new CubicEase { EasingMode = EasingMode.EaseOut });
    }
}

/// <summary>
/// A TextBlock whose text types itself in when it changes (the page's title as you move between pages): bind
/// <c>TypeIn.Text</c> instead of Text, and the letters appear one after another over about a quarter of a second, the
/// block already as wide as the whole word so nothing beside it moves.
/// </summary>
public static class TypeIn
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(TypeIn), new PropertyMetadata(null, OnTextChanged));

    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(TypeIn), new PropertyMetadata(null));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        string text = (string?)e.NewValue ?? string.Empty;
        if (block.GetValue(TimerProperty) is DispatcherTimer old)
        {
            old.Stop();
            block.SetValue(TimerProperty, null);
        }

        if (Motion.ReducedMotion || !block.IsLoaded || text.Length < 2 || string.IsNullOrEmpty(block.Text))
        {
            block.ClearValue(FrameworkElement.MinWidthProperty);
            block.Text = text; // the first title, or none to animate from
            return;
        }

        // Hold the full width from the start, so the line beside it doesn't move while the letters arrive.
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        double pixelsPerDip = VisualTreeHelper.GetDpi(block).PixelsPerDip;
        var measured = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, block.FontSize, Brushes.Black, pixelsPerDip);
        block.MinWidth = Math.Ceiling(measured.WidthIncludingTrailingWhitespace) + 2;

        int shown = 1;
        double perLetter = Math.Clamp(240.0 / text.Length, 12, 36);
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(perLetter) };
        block.SetValue(TimerProperty, timer);
        block.Text = text[..shown];
        timer.Tick += (_, _) =>
        {
            shown++;
            if (shown >= text.Length)
            {
                timer.Stop();
                block.SetValue(TimerProperty, null);
                block.Text = text;
                block.ClearValue(FrameworkElement.MinWidthProperty);
                return;
            }

            block.Text = text[..shown];
        };
        timer.Start();
    }
}
