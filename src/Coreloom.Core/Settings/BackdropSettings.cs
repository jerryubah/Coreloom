using Coreloom.Core.Appearance;

namespace Coreloom.Core.Settings;

/// <summary>What fills the window behind the glass sidebar and cards (since 1.8.2).</summary>
public enum BackdropKind
{
    /// <summary>One flat color.</summary>
    Color,

    /// <summary>A dark gradient with soft lights (Coreloom's own follows the accent color).</summary>
    Gradient,

    /// <summary>A picture: one of Coreloom's, or your own, under a frosted layer.</summary>
    Image,

    /// <summary>The desktop through the window (macOS vibrancy, Windows 11 Mica), darkened to the theme.</summary>
    SeeThrough,
}

/// <summary>
/// The window's background: a color, a gradient or a picture spanning the whole window, which the glass sidebar and
/// cards frost. A picture sits under a frosted layer whose blur and the picture's strength are adjustable.
/// </summary>
public sealed class BackdropSettings
{
    public const int MinimumOpacity = 10;

    /// <summary>The <see cref="Image"/> value for your own picture (kept as <see cref="CustomImage"/>).</summary>
    public const string CustomImageId = "custom";

    public BackdropKind Kind { get; set; } = BackdropKind.Gradient;

    /// <summary>The color, as #RRGGBB (one of <see cref="BackdropPresets.Colors"/> or any other).</summary>
    public string Color { get; set; } = BackdropPresets.DefaultColor;

    /// <summary>The gradient's id (<see cref="BackdropPresets.Gradients"/>).</summary>
    public string Gradient { get; set; } = BackdropPresets.DefaultGradient;

    /// <summary>The picture's id (<see cref="BackdropPresets.Images"/>), or <see cref="CustomImageId"/>.</summary>
    public string Image { get; set; } = BackdropPresets.DefaultImage;

    /// <summary>Your own picture: a copy Coreloom keeps in its own folder (the file name only).</summary>
    public string? CustomImage { get; set; }

    /// <summary>How strongly the picture shows through the frosted layer, in percent (10–100).</summary>
    public int ImageOpacity { get; set; } = 75;

    /// <summary>How much the frosted layer blurs the picture, in percent (0–100).</summary>
    public int Blur { get; set; } = 20;

    /// <summary>The background blur in points (0–48).</summary>
    public double BlurPoints => Math.Clamp(Blur, 0, 100) * 0.48;

    /// <summary>How much the glass cards and sidebar blur what is behind them, in points: always well frosted.</summary>
    public double FrostPoints => 30 + (BlurPoints * 0.5);

    internal void Normalize()
    {
        if (!Enum.IsDefined(Kind))
        {
            Kind = BackdropKind.Gradient;
        }

        if (!AccentPalette.IsValidHex(Color))
        {
            Color = BackdropPresets.DefaultColor;
        }

        Color = Color.ToUpperInvariant();
        if (BackdropPresets.FindGradient(Gradient) is null)
        {
            Gradient = BackdropPresets.DefaultGradient;
        }

        if (string.IsNullOrWhiteSpace(CustomImage) || CustomImage.IndexOfAny(['/', '\\']) >= 0 || CustomImage.Contains("..", StringComparison.Ordinal))
        {
            CustomImage = null; // only a file name in Coreloom's own folder
        }

        if (Image == CustomImageId ? CustomImage is null : BackdropPresets.FindImage(Image) is null)
        {
            Image = BackdropPresets.DefaultImage;
        }

        ImageOpacity = Math.Clamp(ImageOpacity, MinimumOpacity, 100);
        Blur = Math.Clamp(Blur, 0, 100);
    }
}
