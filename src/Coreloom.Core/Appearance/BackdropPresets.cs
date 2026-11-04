using System.Globalization;

namespace Coreloom.Core.Appearance;

/// <summary>A color with channels from 0 to 1 (sRGB).</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Parse(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            return new Rgb(0, 0, 0);
        }

        return new Rgb(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
    }

    public static Rgb FromBytes(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f);

    public Rgb Mix(Rgb other, double t)
    {
        float k = (float)Math.Clamp(t, 0, 1);
        return new Rgb(R + ((other.R - R) * k), G + ((other.G - G) * k), B + ((other.B - B) * k));
    }

    /// <summary>Relative luminance-ish brightness (0–1), enough to tell light from dark.</summary>
    public double Luma => (0.2126 * R) + (0.7152 * G) + (0.0722 * B);

    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}");

    /// <summary>The color with its hue turned by <paramref name="degrees"/> and its lightness moved by <paramref name="lighten"/>.</summary>
    public Rgb Shift(double degrees, double lighten = 0)
    {
        double r = R, g = G, b = B;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0, d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h *= 60;
        }

        h = (((h + degrees) % 360) + 360) % 360;
        l = Math.Clamp(l + lighten, 0, 1);
        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        double p = (2 * l) - q;
        return new Rgb((float)Channel(p, q, h + 120), (float)Channel(p, q, h), (float)Channel(p, q, h - 120));

        static double Channel(double p, double q, double hue)
        {
            hue = ((hue % 360) + 360) % 360;
            return hue < 60 ? p + ((q - p) * hue / 60) : hue < 180 ? q : hue < 240 ? p + ((q - p) * (240 - hue) / 60) : p;
        }
    }

    private static int Byte(float v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
}

/// <summary>A suggested background color.</summary>
public sealed record BackdropColor(string Id, string Name, string Hex);

/// <summary>A soft light in a gradient: where it sits (0–1 across and down), how far it reaches (in heights) and how bright.</summary>
/// <param name="Color">#RRGGBB, or "accent", "accent-cool", "accent-warm" for the accent color and its neighbours.</param>
public sealed record BackdropGlow(double X, double Y, double RadiusX, double RadiusY, string Color, double Strength);

/// <summary>
/// A suggested dark gradient: a base, a linear sweep (<paramref name="Angle"/> in degrees, 0 = left to right, 90 = top
/// to bottom) and a few soft lights on top.
/// </summary>
public sealed record BackdropGradient(string Id, string Name, double Angle, string From, string To, IReadOnlyList<BackdropGlow> Glows)
{
    /// <summary>Whether its lights follow the accent color.</summary>
    public bool UsesAccent => Glows.Any(g => g.Color.StartsWith("accent", StringComparison.Ordinal));
}

/// <summary>A suggested picture (assets/backdrops/<c>Id</c>.jpg and <c>Id</c>-thumb.jpg, drawn by build/backdrops/make-backdrops.py).</summary>
public sealed record BackdropPicture(string Id, string Name);

/// <summary>The suggested dark colors, gradients and pictures for the window's background.</summary>
public static class BackdropPresets
{
    public const string DefaultColor = "#0A0B0E";
    public const string DefaultGradient = "coreloom";
    public const string DefaultImage = "silk";

    public static IReadOnlyList<BackdropColor> Colors { get; } =
    [
        new("black", "Coreloom black", DefaultColor),
        new("midnight", "Midnight", "#0B1020"),
        new("navy", "Navy", "#0A1630"),
        new("plum", "Plum", "#170E22"),
        new("forest", "Forest", "#0A1611"),
        new("espresso", "Espresso", "#17110D"),
        new("slate", "Slate", "#121820"),
        new("charcoal", "Charcoal", "#18191C"),
    ];

    public static IReadOnlyList<BackdropGradient> Gradients { get; } =
    [
        new(DefaultGradient, "Coreloom", 115, "#0A0D17", "#06070B",
        [
            new(0.10, -0.05, 0.95, 0.80, "accent", 0.30),
            new(0.98, 1.02, 0.80, 0.70, "accent-warm", 0.22),
            new(0.62, 0.38, 0.45, 0.35, "accent-cool", 0.06),
        ]),
        new("midnight", "Midnight", 100, "#0B1226", "#05060B",
        [
            new(0.88, 0.02, 0.90, 0.75, "#3B5BFF", 0.24),
            new(0.08, 0.95, 0.80, 0.65, "#1E3A8A", 0.30),
        ]),
        new("aurora", "Aurora", 90, "#061012", "#04080A",
        [
            new(0.15, 0.08, 0.90, 0.70, "#10B981", 0.26),
            new(0.86, 0.25, 0.70, 0.60, "#0EA5E9", 0.20),
            new(0.70, 1.05, 0.80, 0.60, "#6D28D9", 0.18),
        ]),
        new("nebula", "Nebula", 120, "#0E0818", "#07050C",
        [
            new(0.18, 0.12, 0.85, 0.75, "#A21CAF", 0.26),
            new(0.86, 0.62, 0.90, 0.75, "#6D28D9", 0.28),
            new(0.55, 1.08, 0.60, 0.50, "#EC4899", 0.14),
        ]),
        new("ember", "Ember", 110, "#120A08", "#080505",
        [
            new(0.92, -0.02, 0.90, 0.75, "#EA580C", 0.24),
            new(0.08, 0.98, 0.80, 0.70, "#B91C1C", 0.24),
            new(0.50, 0.50, 0.40, 0.30, "#F59E0B", 0.05),
        ]),
        new("ocean", "Ocean", 100, "#06121C", "#03080D",
        [
            new(0.10, 0.00, 1.00, 0.80, "#0284C7", 0.28),
            new(0.92, 0.92, 0.85, 0.70, "#0F766E", 0.26),
        ]),
        new("forest", "Forest", 95, "#08110B", "#050906",
        [
            new(0.15, 0.05, 0.90, 0.75, "#15803D", 0.24),
            new(0.90, 0.88, 0.80, 0.65, "#4D7C0F", 0.18),
        ]),
        new("graphite", "Graphite", 90, "#15171B", "#08090B",
        [
            new(0.50, -0.15, 1.20, 0.65, "#FFFFFF", 0.05),
        ]),
    ];

    public static IReadOnlyList<BackdropPicture> Images { get; } =
    [
        new(DefaultImage, "Silk"),
        new("aurora", "Aurora"),
        new("nebula", "Nebula"),
        new("dunes", "Dunes"),
        new("bloom", "Bloom"),
        new("lines", "Light trails"),
    ];

    public static BackdropGradient? FindGradient(string? id) => Gradients.FirstOrDefault(g => g.Id == id);

    public static BackdropPicture? FindImage(string? id) => Images.FirstOrDefault(i => i.Id == id);

    public static BackdropColor? FindColor(string? hex) => Colors.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));

    /// <summary>A light's color: a hex color, or the accent ("accent") and its cooler and warmer neighbours.</summary>
    public static Rgb Resolve(string color, Rgb accent) => color switch
    {
        "accent" => accent,
        "accent-cool" => accent.Shift(-24, 0.06),
        "accent-warm" => accent.Shift(42),
        _ => Rgb.Parse(color),
    };

    /// <summary>
    /// A background color as the theme shows it: as chosen in the dark theme; in the light theme, a pale tint of it on the
    /// light background (so the light theme stays light whatever was picked).
    /// </summary>
    public static Rgb ForTheme(Rgb color, bool dark, Rgb lightBase) => dark ? color : lightBase.Mix(color.Shift(0, 0.12), 0.10);
}
