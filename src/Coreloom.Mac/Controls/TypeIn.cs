using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Coreloom.Mac.Behaviors;

namespace Coreloom.Mac.Controls;

/// <summary>
/// A TextBlock whose text types itself in when it changes (the page's title as you move between pages): bind
/// <c>TypeIn.Text</c> instead of Text, and the letters appear one after another over about a quarter of a second, the
/// block already as wide as the whole word so nothing beside it moves.
/// </summary>
public static class TypeIn
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(TypeIn));

    private static readonly AttachedProperty<DispatcherTimer?> TimerProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, DispatcherTimer?>("Timer", typeof(TypeIn));

    static TypeIn() => TextProperty.Changed.AddClassHandler<TextBlock>(OnTextChanged);

    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(TextBlock block, AvaloniaPropertyChangedEventArgs e)
    {
        string text = e.NewValue as string ?? string.Empty;
        if (block.GetValue(TimerProperty) is { } old)
        {
            old.Stop();
            block.SetValue(TimerProperty, null);
        }

        if (Motion.ReducedMotion || !block.IsLoaded || text.Length < 2 || string.IsNullOrEmpty(block.Text))
        {
            block.MinWidth = 0;
            block.Text = text; // the first title, or none to animate from
            return;
        }

        // Hold the full width from the start, so the line beside it doesn't move while the letters arrive.
        block.Text = text;
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        block.MinWidth = Math.Ceiling(block.DesiredSize.Width) + 2;

        // The letters come by the clock, not by the tick: when the page being built keeps the timer waiting, the title
        // catches up at once instead of arriving late one letter at a time.
        double perLetter = Math.Clamp(240.0 / text.Length, 12, 36);
        var started = System.Diagnostics.Stopwatch.StartNew();
        block.Text = text[..1];
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(perLetter) };
        block.SetValue(TimerProperty, timer);
        timer.Tick += (_, _) =>
        {
            int shown = 1 + (int)(started.Elapsed.TotalMilliseconds / perLetter);
            if (shown >= text.Length)
            {
                timer.Stop();
                block.SetValue(TimerProperty, null);
                block.Text = text;
                block.MinWidth = 0;
                return;
            }

            block.Text = text[..shown];
        };
        timer.Start();
    }
}
