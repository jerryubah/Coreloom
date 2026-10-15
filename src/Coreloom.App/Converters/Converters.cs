using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Coreloom.App.Themes;
using Coreloom.Core.Formatting;

namespace Coreloom.App.Converters;

/// <summary>true → Visible. Pass "Invert" as parameter to invert.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>Non-null / non-empty string / non-empty collection → Visible ("Invert" to invert).</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
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

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            has = !has;
        }

        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Non-null / non-empty string / non-empty collection → true (e.g. to play an animation when a message appears).</summary>
public sealed class HasValueToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => HasValueToVisibilityConverter.HasValue(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>value.ToString() == parameter → Visible.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>value.ToString() == parameter → true (for radio-style selection).</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Semantic color key ("Cpu", "Gpu"...) → theme series brush.</summary>
public sealed class ColorKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeService.Instance?.GetSeriesBrush(value as string ?? parameter as string) ?? Brushes.SteelBlue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// (ThresholdLevel, ColorKey) → brush. Normal uses the series color; elevated/critical use warning colors.
/// With ConverterParameter="Text", normal values use the primary text color instead.
/// </summary>
public sealed class LevelToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var theme = ThemeService.Instance;
        if (theme is null)
        {
            return Brushes.Gray;
        }

        var level = values.Length > 0 && values[0] is ThresholdLevel l ? l : ThresholdLevel.Normal;
        string? colorKey = values.Length > 1 ? values[1] as string : null;
        return level switch
        {
            ThresholdLevel.Critical => theme.GetBrush("Brush.Critical"),
            ThresholdLevel.Elevated => theme.GetBrush("Brush.Elevated"),
            _ => string.Equals(parameter as string, "Text", StringComparison.OrdinalIgnoreCase) ? theme.GetBrush("Brush.Text") : theme.GetSeriesBrush(colorKey),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Icon resource key → Geometry.</summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) as Geometry : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Fraction (0..1) → percentage (0..100) for progress bars.</summary>
public sealed class FractionToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d && double.IsFinite(d) ? Math.Clamp(d * 100, 0, 100) : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Hex color string → SolidColorBrush (accent swatches).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            return value is string hex ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) : Brushes.Transparent;
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Converts between an int and a double (for sliders bound to int properties).</summary>
public sealed class IntToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i ? (double)i : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d ? (int)Math.Round(d) : 0;
}

/// <summary>(a, b) → Visible when both values are equal as strings (case-insensitive). Used for the selected accent ring.</summary>
public sealed class MultiEqualsToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
