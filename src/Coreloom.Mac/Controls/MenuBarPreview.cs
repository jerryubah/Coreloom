using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Coreloom.ViewModels.MenuBar;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Settings › Menu bar's preview: the modules turned on, drawn as the menu bar draws them (right-aligned on a strip the
/// color of a menu bar, beside a clock), so the choices can be seen before looking up.
/// </summary>
public sealed class MenuBarPreview : Control
{
    public static readonly StyledProperty<IReadOnlyList<MenuBarReading>?> ReadingsProperty =
        AvaloniaProperty.Register<MenuBarPreview, IReadOnlyList<MenuBarReading>?>(nameof(Readings));

    public static readonly StyledProperty<bool> IsDarkBarProperty = AvaloniaProperty.Register<MenuBarPreview, bool>(nameof(IsDarkBar), true);

    private const double Spacing = 14;

    static MenuBarPreview()
    {
        AffectsRender<MenuBarPreview>(ReadingsProperty, IsDarkBarProperty);
    }

    public IReadOnlyList<MenuBarReading>? Readings
    {
        get => GetValue(ReadingsProperty);
        set => SetValue(ReadingsProperty, value);
    }

    /// <summary>A dark menu bar (white readings) or a light one.</summary>
    public bool IsDarkBar
    {
        get => GetValue(IsDarkBarProperty);
        set => SetValue(IsDarkBarProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width, MenuBarModuleRenderer.Height + 10);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var bar = new Rect(0, 5, bounds.Width, MenuBarModuleRenderer.Height);
        bool dark = IsDarkBar;
        var ink = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF6, 0xF8) : Color.FromRgb(0x1D, 0x1E, 0x22));
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = dark
                ? [new GradientStop(Color.FromArgb(0xD8, 0x1A, 0x1C, 0x24), 0), new GradientStop(Color.FromArgb(0xE8, 0x14, 0x16, 0x1C), 1)]
                : [new GradientStop(Color.FromArgb(0xE6, 0xF1, 0xF2, 0xF5), 0), new GradientStop(Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFC), 1)],
        };
        context.DrawRectangle(fill, new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x30, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0)), 1), new RoundedRect(bar, 9));

        // The clock and Control Center at the right end, as on a real menu bar (in the bar's own ink, a little dimmer).
        var clock = new FormattedText("Tue 9:41 AM", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("avares://Coreloom/Assets/Fonts#Inter"), FontStyle.Normal, FontWeight.Medium), 12.5, ink);
        double x = bar.Right - 12 - clock.Width;
        using (context.PushOpacity(0.8))
        {
            context.DrawText(clock, new Point(x, bar.Y + ((bar.Height - clock.Height) / 2)));
        }

        var readings = Readings ?? [];
        using var clip = context.PushClip(bar.Deflate(new Thickness(10, 0)));
        for (int i = readings.Count - 1; i >= 0; i--)
        {
            double width = MenuBarModuleRenderer.Measure(readings[i]);
            x -= Spacing + width;
            MenuBarModuleRenderer.Draw(context, readings[i], new Point(x, bar.Y), ink);
        }
    }
}
