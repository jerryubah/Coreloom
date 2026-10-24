using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Coreloom.Mac.Behaviors;

/// <summary>
/// Tweens a TextBlock's number between readings: bind <c>Value</c> (raw double) and <c>Formatter</c>
/// (Func&lt;double,string&gt;). The text eases to each new value over ~350 ms. Off with "Reduced motion".
/// </summary>
public static class AnimatedText
{
    public static bool IsGloballyEnabled { get; set; } = true;

    public static readonly AttachedProperty<double> ValueProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, double>("Value", typeof(AnimatedText), double.NaN);

    public static readonly AttachedProperty<Func<double, string>?> FormatterProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Func<double, string>?>("Formatter", typeof(AnimatedText));

    private static readonly AttachedProperty<double> CurrentProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, double>("Current", typeof(AnimatedText), double.NaN);

    private static readonly AttachedProperty<Animator.Tween?> TweenProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Animator.Tween?>("Tween", typeof(AnimatedText));

    static AnimatedText()
    {
        ValueProperty.Changed.AddClassHandler<TextBlock>(OnValueChanged);
        FormatterProperty.Changed.AddClassHandler<TextBlock>((text, _) => Render(text, text.GetValue(CurrentProperty)));
    }

    public static double GetValue(TextBlock element) => element.GetValue(ValueProperty);

    public static void SetValue(TextBlock element, double value) => element.SetValue(ValueProperty, value);

    public static Func<double, string>? GetFormatter(TextBlock element) => element.GetValue(FormatterProperty);

    public static void SetFormatter(TextBlock element, Func<double, string>? value) => element.SetValue(FormatterProperty, value);

    private static void OnValueChanged(TextBlock text, AvaloniaPropertyChangedEventArgs e)
    {
        double from = text.GetValue(CurrentProperty);
        double to = e.NewValue is double d ? d : double.NaN;
        text.GetValue(TweenProperty)?.Cancel();

        if (!double.IsFinite(to))
        {
            text.SetValue(CurrentProperty, double.NaN);
            text.Text = "—";
            return;
        }

        bool animate = IsGloballyEnabled && double.IsFinite(from) && text.IsEffectivelyVisible && Math.Abs(to - from) > 1e-9;
        if (!animate)
        {
            text.SetValue(CurrentProperty, to);
            Render(text, to);
            return;
        }

        text.SetValue(TweenProperty, Animator.Run(TimeSpan.FromMilliseconds(350), Ease.CubicOut, t =>
        {
            double value = from + ((to - from) * t);
            text.SetValue(CurrentProperty, value);
            Render(text, value);
        }));
    }

    private static void Render(TextBlock text, double value)
    {
        if (!double.IsFinite(value))
        {
            text.Text = "—";
            return;
        }

        var formatter = GetFormatter(text);
        text.Text = formatter is null ? value.ToString("0", CultureInfo.CurrentCulture) : formatter(value);
    }
}

/// <summary>Subtle entrance animation for pages and cards (fade + 8px rise). Skipped with reduced motion.</summary>
public static class Motion
{
    public static bool ReducedMotion { get; set; }

    public static readonly AttachedProperty<bool> EntranceProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Entrance", typeof(Motion));

    static Motion()
    {
        EntranceProperty.Changed.AddClassHandler<Control>((element, e) =>
        {
            element.Loaded -= OnLoaded;
            if (e.NewValue is true)
            {
                element.Loaded += OnLoaded;
            }
        });
    }

    public static bool GetEntrance(Control element) => element.GetValue(EntranceProperty);

    public static void SetEntrance(Control element, bool value) => element.SetValue(EntranceProperty, value);

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control element || ReducedMotion)
        {
            return;
        }

        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform(0, 8);
            element.RenderTransform = translate;
        }

        element.Opacity = 0;
        Animator.Run(TimeSpan.FromMilliseconds(240), Ease.CubicOut, t =>
        {
            element.Opacity = t;
            translate.Y = 8 * (1 - t);
        }, () =>
        {
            element.Opacity = 1;
            translate.Y = 0;
        });
    }
}

/// <summary>Glides a progress bar or slider to a new value instead of jumping. Instant with reduced motion.</summary>
public static class Smooth
{
    public static readonly AttachedProperty<double> ValueProperty =
        AvaloniaProperty.RegisterAttached<RangeBase, double>("Value", typeof(Smooth));

    private static readonly AttachedProperty<Animator.Tween?> TweenProperty =
        AvaloniaProperty.RegisterAttached<RangeBase, Animator.Tween?>("Tween", typeof(Smooth));

    static Smooth()
    {
        ValueProperty.Changed.AddClassHandler<RangeBase>((range, e) =>
        {
            double value = e.NewValue is double d && double.IsFinite(d) ? d : 0;
            double target = Math.Clamp(value, range.Minimum, range.Maximum);
            double from = range.Value;
            bool backwards = target < from - ((range.Maximum - range.Minimum) * 0.001);
            range.GetValue(TweenProperty)?.Cancel();
            if (Motion.ReducedMotion || !AnimatedText.IsGloballyEnabled || !range.IsEffectivelyVisible || backwards)
            {
                range.Value = target; // resets drop straight back
                return;
            }

            range.SetValue(TweenProperty, Animator.Run(TimeSpan.FromMilliseconds(380), Ease.CubicOut, t => range.Value = from + ((target - from) * t)));
        });
    }

    public static double GetValue(RangeBase element) => element.GetValue(ValueProperty);

    public static void SetValue(RangeBase element, double value) => element.SetValue(ValueProperty, value);
}

/// <summary>Turns an element round and round while <c>IsSpinning</c> is true (the scanning ring), easing to a stop.</summary>
public static class Spin
{
    public static readonly AttachedProperty<bool> IsSpinningProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("IsSpinning", typeof(Spin));

    private static readonly AttachedProperty<Animator.Tween?> TweenProperty =
        AvaloniaProperty.RegisterAttached<Visual, Animator.Tween?>("SpinTween", typeof(Spin));

    static Spin()
    {
        IsSpinningProperty.Changed.AddClassHandler<Visual>((element, e) =>
        {
            if (element.RenderTransform is not RotateTransform rotate)
            {
                rotate = new RotateTransform();
                element.RenderTransform = rotate;
                element.RenderTransformOrigin = RelativePoint.Center;
            }

            element.GetValue(TweenProperty)?.Cancel();
            double at = rotate.Angle % 360;
            if (e.NewValue is true && !Motion.ReducedMotion)
            {
                element.SetValue(TweenProperty, Animator.Loop(seconds => rotate.Angle = (at + (seconds / 1.3 * 360)) % 360));
            }
            else if (!Motion.ReducedMotion)
            {
                element.SetValue(TweenProperty, Animator.Run(TimeSpan.FromMilliseconds(500), Ease.CubicOut, t => rotate.Angle = at + (90 * t)));
            }
        });
    }

    public static bool GetIsSpinning(Visual element) => element.GetValue(IsSpinningProperty);

    public static void SetIsSpinning(Visual element, bool value) => element.SetValue(IsSpinningProperty, value);
}

/// <summary>Breathes an element's opacity while <c>IsPulsing</c> is true (the test running now); back to full when it stops.</summary>
public static class Pulse
{
    public static readonly AttachedProperty<bool> IsPulsingProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("IsPulsing", typeof(Pulse));

    private static readonly AttachedProperty<Animator.Tween?> TweenProperty =
        AvaloniaProperty.RegisterAttached<Visual, Animator.Tween?>("PulseTween", typeof(Pulse));

    static Pulse()
    {
        IsPulsingProperty.Changed.AddClassHandler<Visual>((element, e) =>
        {
            element.GetValue(TweenProperty)?.Cancel();
            if (e.NewValue is true && !Motion.ReducedMotion)
            {
                // 1 → 0.45 → 1 every 1.8 s, easing in and out.
                element.SetValue(TweenProperty, Animator.Loop(seconds =>
                {
                    double phase = (seconds % 1.8) / 1.8;
                    element.Opacity = 1 - (0.55 * Ease.SineInOut(phase < 0.5 ? phase * 2 : (1 - phase) * 2));
                }));
            }
            else
            {
                element.Opacity = 1;
            }
        });
    }

    public static bool GetIsPulsing(Visual element) => element.GetValue(IsPulsingProperty);

    public static void SetIsPulsing(Visual element, bool value) => element.SetValue(IsPulsingProperty, value);
}

/// <summary>A small "pop" (scale up from 85 % with a soft overshoot and fade in) whenever <c>Trigger</c> changes to true.</summary>
public static class Pop
{
    public static readonly AttachedProperty<bool> TriggerProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Trigger", typeof(Pop));

    static Pop()
    {
        TriggerProperty.Changed.AddClassHandler<Visual>((element, e) =>
        {
            if (e.NewValue is not true || Motion.ReducedMotion)
            {
                return;
            }

            if (element.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                element.RenderTransform = scale;
                element.RenderTransformOrigin = RelativePoint.Center;
            }

            var back = Ease.BackOut(0.35);
            Animator.Run(TimeSpan.FromMilliseconds(420), back, t =>
            {
                double s = 0.85 + (0.15 * t);
                scale.ScaleX = s;
                scale.ScaleY = s;
            });
            Animator.Run(TimeSpan.FromMilliseconds(260), Ease.Linear, t => element.Opacity = t, () => element.Opacity = 1);
        });
    }

    public static bool GetTrigger(Visual element) => element.GetValue(TriggerProperty);

    public static void SetTrigger(Visual element, bool value) => element.SetValue(TriggerProperty, value);
}
