using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Media;
using Coreloom.Core.Formatting;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>Non-null / non-empty string / positive number / non-empty collection → true ("Invert" to invert).</summary>
public sealed class HasValueConverter : IValueConverter
{
    internal static bool HasValue(object? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        int i => i > 0,
        ICollection c => c.Count > 0,
        _ => true,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = HasValue(value);
        return string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase) ? !has : has;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>value.ToString() == parameter → true (radio-style selection, and showing one part for one state).</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = parameter?.ToString();
        if (text is not null && text.StartsWith('!'))
        {
            return !string.Equals(value?.ToString(), text[1..], StringComparison.OrdinalIgnoreCase);
        }

        return text is not null && text.Contains('|')
            ? text.Split('|').Any(t => string.Equals(value?.ToString(), t, StringComparison.OrdinalIgnoreCase))
            : string.Equals(value?.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Semantic color key ("Cpu", "Gpu"...) → theme series brush.</summary>
public sealed class ColorKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeService.Instance?.GetSeriesBrush(value as string ?? parameter as string) ?? Brushes.SteelBlue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>
/// (ThresholdLevel, ColorKey) → brush. Normal uses the series color; elevated/critical use warning colors.
/// With ConverterParameter="Text", normal values use the primary text color instead.
/// </summary>
public sealed class LevelToBrushConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var theme = ThemeService.Instance;
        if (theme is null)
        {
            return Brushes.Gray;
        }

        var level = values.Count > 0 && values[0] is ThresholdLevel l ? l : ThresholdLevel.Normal;
        string? colorKey = values.Count > 1 ? values[1] as string : null;
        return level switch
        {
            ThresholdLevel.Critical => theme.GetBrush("Brush.Critical"),
            ThresholdLevel.Elevated => theme.GetBrush("Brush.Elevated"),
            _ => string.Equals(parameter as string, "Text", StringComparison.OrdinalIgnoreCase) ? theme.GetBrush("Brush.Text") : theme.GetSeriesBrush(colorKey),
        };
    }
}

/// <summary>Icon resource key → Geometry.</summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var resource) ? resource as Geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Fraction (0..1) → percentage (0..100) for progress bars.</summary>
public sealed class FractionToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d && double.IsFinite(d) ? Math.Clamp(d * 100, 0, 100) : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Hex color string → brush (accent swatches, overlay colors).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Converts between an int and a double (for sliders bound to int properties).</summary>
public sealed class IntToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i ? (double)i : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d ? (int)Math.Round(d) : 0;
}

/// <summary>(a, b) → true when both values are equal as strings (case-insensitive). Used for the selected accent ring.</summary>
public sealed class MultiEqualsConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count >= 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A health status ("Excellent", "Good", "Attention") → the glow color key of a card (SquircleBorder.GlowKey).</summary>
public sealed class StatusToGlowKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Excellent" => "Success",
        "Good" => "Info",
        "Attention" or "Problem" => "Critical",
        "Notice" => "Elevated",
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>
/// A health status ("Excellent" green, "Good" blue, "Notice" amber, "Attention" or "Problem" red) → its brush; anything
/// else → the brush named by the parameter (Brush.TextSecondary when none).
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = (value as string) switch
        {
            "Excellent" => "Brush.Success",
            "Good" => "Brush.Info",
            "Attention" or "Problem" => "Brush.Critical",
            "Notice" => "Brush.Elevated",
            _ => parameter as string ?? "Brush.TextSecondary",
        };
        return ThemeService.Instance?.GetBrush(key) ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>A health check's state ("Ok", "Notice", "Problem", "Unknown") → its icon.</summary>
public sealed class CheckStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = (value as string) switch
        {
            "Notice" or "Unknown" => "Icon.Info",
            "Problem" => "Icon.Warning",
            _ => "Icon.Check",
        };
        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var resource) ? resource : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>A health check's state → its icon's brush (green, amber, red or quiet grey).</summary>
public sealed class CheckStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = (value as string) switch
        {
            "Notice" => "Brush.Elevated",
            "Problem" => "Brush.Critical",
            "Unknown" => "Brush.TextTertiary",
            _ => "Brush.Success",
        };
        return ThemeService.Instance?.GetBrush(key) ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>A theme brush by key ("Brush.Accent") when the value is true, another (the parameter "A|B") when false.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var keys = (parameter as string ?? "Brush.Accent|Brush.TextSecondary").Split('|');
        string key = value is true ? keys[0] : keys.Length > 1 ? keys[1] : keys[0];
        return key == "Transparent" ? Brushes.Transparent : ThemeService.Instance?.GetBrush(key) ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>true → the first number of the parameter ("1|0.45"), false → the second (opacity, sizes, font weights).</summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "1|0").Split('|');
        string pick = value is true ? parts[0] : parts.Length > 1 ? parts[1] : "0";
        return double.Parse(pick, CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>true → the first thickness of the parameter ("0;14,0,10,0"), false → the second.</summary>
public sealed class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "0;0").Split(';');
        return Thickness.Parse(value is true ? parts[0] : parts.Length > 1 ? parts[1] : parts[0]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>
/// true → the first value of the parameter ("SemiBold|Normal", "Center|Left", "0|14,0,10,0"), false → the second, converted
/// to the target property's type (font weights, alignments, margins, numbers).
/// </summary>
public sealed class BoolToValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? string.Empty).Split('|');
        string pick = value is true ? parts[0] : parts.Length > 1 ? parts[1] : parts[0];
        return Parse(pick, targetType);
    }

    internal static object? Parse(string text, Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(Thickness))
        {
            return Thickness.Parse(text);
        }

        if (type == typeof(CornerRadius))
        {
            return CornerRadius.Parse(text);
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, text, ignoreCase: true);
        }

        if (type == typeof(double))
        {
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        if (type == typeof(GridLength))
        {
            return GridLength.Parse(text);
        }

        return DefaultValueConverter.Instance.Convert(text, targetType, null, CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>true → the overlay's text shadow (on the text only), false → none.</summary>
public sealed class TextShadowConverter : IValueConverter
{
    private static readonly DropShadowEffect Shadow = new() { BlurRadius = 4, OffsetX = 0, OffsetY = 1, Opacity = 0.85, Color = Colors.Black };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Shadow : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>
/// An overlay position ("TopRight") to its alignment in the preview: parameter "H" for the horizontal alignment, "V" for
/// the vertical one.
/// </summary>
public sealed class PositionToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string position = value?.ToString() ?? string.Empty;
        if (string.Equals(parameter as string, "V", StringComparison.OrdinalIgnoreCase))
        {
            return position.StartsWith("Top", StringComparison.Ordinal) ? Avalonia.Layout.VerticalAlignment.Top
                : position.StartsWith("Bottom", StringComparison.Ordinal) ? Avalonia.Layout.VerticalAlignment.Bottom
                : Avalonia.Layout.VerticalAlignment.Center;
        }

        return position.EndsWith("Left", StringComparison.Ordinal) ? Avalonia.Layout.HorizontalAlignment.Left
            : position.EndsWith("Right", StringComparison.Ordinal) ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Center;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>
/// View-model values the XAML can't take as they are: "12,8" → a Thickness, 16 → a uniform CornerRadius (WPF converted
/// these by itself; Avalonia bindings don't).
/// </summary>
public sealed class ToValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return value switch
        {
            null => null,
            double d when type == typeof(CornerRadius) => new CornerRadius(d),
            double d when type == typeof(Thickness) => new Thickness(d),
            int i when type == typeof(CornerRadius) => new CornerRadius(i),
            string s => BoolToValueConverter.Parse(s, targetType),
            _ => value,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
