using Coreloom.Core.Settings;

namespace Coreloom.Core.Appearance;

/// <summary>An opaque picture as BGRA bytes, four per pixel, row after row (alpha always 255).</summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height, byte[]? pixels = null)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Bgra = pixels ?? new byte[Width * Height * 4];
        if (Bgra.Length < Width * Height * 4)
        {
            throw new ArgumentException("Too few pixels for the size.", nameof(pixels));
        }
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }

    public int Stride => Width * 4;

    /// <summary>The color at a pixel (for tests and the average).</summary>
    public Rgb At(int x, int y)
    {
        int i = ((Math.Clamp(y, 0, Height - 1) * Width) + Math.Clamp(x, 0, Width - 1)) * 4;
        return Rgb.FromBytes(Bgra[i + 2], Bgra[i + 1], Bgra[i]);
    }

    /// <summary>The average color, from a sparse sample.</summary>
    public Rgb Average()
    {
        double r = 0, g = 0, b = 0;
        int n = 0, step = Math.Max(1, (int)Math.Sqrt((double)Width * Height / 4096));
        for (int y = step / 2; y < Height; y += step)
        {
            for (int x = step / 2; x < Width; x += step)
            {
                var c = At(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
                n++;
            }
        }

        return n == 0 ? default : new Rgb((float)(r / n), (float)(g / n), (float)(b / n));
    }
}

/// <summary>What to draw behind the window's glass, at what size.</summary>
public sealed record BackdropRequest
{
    public BackdropKind Kind { get; init; } = BackdropKind.Gradient;

    /// <summary>The window's size in device pixels.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Device pixels per point (2 on a Retina screen), so blurs look the same everywhere.</summary>
    public double Scale { get; init; } = 1;

    public bool Dark { get; init; } = true;

    /// <summary>The theme's own background (dark or light), under a picture and for the light theme's tints.</summary>
    public Rgb ThemeBase { get; init; }

    public Rgb Accent { get; init; }

    public Rgb Color { get; init; }

    public BackdropGradient? Gradient { get; init; }

    /// <summary>The picture, decoded (any size; it is scaled to cover the window).</summary>
    public PixelImage? Image { get; init; }

    /// <summary>How strongly the picture shows over the theme's background (0.1–1).</summary>
    public double ImageOpacity { get; init; } = 0.6;

    public double BlurPoints { get; init; }

    public double FrostPoints { get; init; } = 30;
}

/// <summary>
/// The window's background, and the frosted copy of it the glass cards and sidebar show (smaller and heavily blurred;
/// it is stretched to the window's size and each card draws the part behind it).
/// </summary>
public sealed record BackdropLayers(PixelImage Background, PixelImage Frost);

/// <summary>
/// Draws the window's background from <see cref="BackdropSettings"/>: colors, gradients (dithered, so dark gradients
/// don't band), pictures scaled to cover the window and laid over the theme's background, and the frosted copy for the
/// glass. Pure pixel work, run off the UI thread; both apps show the result as two bitmaps.
/// </summary>
public static class BackdropRenderer
{
    /// <summary>A sharp background is drawn at most this many pixels (bigger windows stretch it a little).</summary>
    public const int MaxBackgroundPixels = 4_200_000;

    /// <summary>How quickly a light fades with distance (at its radius it is down to about a tenth).</summary>
    private const double GlowFalloff = 2.2;

    /// <summary>Null for <see cref="BackdropKind.SeeThrough"/>, which the system draws.</summary>
    public static BackdropLayers? Render(BackdropRequest request)
    {
        int width = Math.Max(1, request.Width);
        int height = Math.Max(1, request.Height);
        double scale = request.Scale > 0 ? request.Scale : 1;
        double frostPixels = Math.Max(1, request.FrostPoints * scale);
        // At most a quarter of the window's pixels each way (since 1.8.6; it was up to 1/32): stretched back up, a
        // smaller copy showed blocks and steps behind the glass.
        int frostFactor = Math.Clamp((int)Math.Round(frostPixels / 6), 2, 4);
        int frostWidth = Math.Max(4, (int)Math.Ceiling((double)width / frostFactor));
        int frostHeight = Math.Max(4, (int)Math.Ceiling((double)height / frostFactor));
        int frostRadius = Math.Max(1, (int)Math.Round(frostPixels / frostFactor));

        switch (request.Kind)
        {
            case BackdropKind.SeeThrough:
                return null;

            case BackdropKind.Gradient when request.Gradient is { } gradient:
            {
                var (w, h) = Fit(width, height, MaxBackgroundPixels);
                var background = Gradient(gradient, w, h, request.Accent, request.Dark, request.ThemeBase, dither: true);
                var frost = Gradient(gradient, frostWidth, frostHeight, request.Accent, request.Dark, request.ThemeBase, dither: false);
                Blur(frost, frostRadius);
                return new BackdropLayers(background, frost);
            }

            case BackdropKind.Image when request.Image is { } image:
            {
                double opacity = Math.Clamp(request.ImageOpacity, 0.05, 1) * (request.Dark ? 1 : 0.55);
                double blurPixels = Math.Max(0, request.BlurPoints * scale);
                int factor = blurPixels < 1 ? 1 : Math.Clamp((int)Math.Floor(blurPixels / 5), 1, 32);
                var (w, h) = factor == 1
                    ? Fit(width, height, MaxBackgroundPixels)
                    : (Math.Max(4, (int)Math.Ceiling((double)width / factor)), Math.Max(4, (int)Math.Ceiling((double)height / factor)));
                var background = Cover(image, w, h);
                Over(background, request.ThemeBase, opacity);
                if (blurPixels >= 1)
                {
                    Blur(background, Math.Max(1, (int)Math.Round(blurPixels / factor)));
                }

                var frost = Cover(image, frostWidth, frostHeight);
                Over(frost, request.ThemeBase, opacity);
                Saturate(frost, 1.15);
                Blur(frost, frostRadius); // last: its dithered result is what's shown
                return new BackdropLayers(background, frost);
            }

            default:
            {
                // A color (or a gradient or picture that's missing): one flat color.
                var color = request.Kind == BackdropKind.Color
                    ? BackdropPresets.ForTheme(request.Color, request.Dark, request.ThemeBase)
                    : request.ThemeBase;
                return new BackdropLayers(Solid(color, 8, 8), Solid(color, 8, 8));
            }
        }
    }

    /// <summary>The largest size with the same shape and at most <paramref name="maxPixels"/> pixels.</summary>
    public static (int Width, int Height) Fit(int width, int height, int maxPixels)
    {
        double area = (double)width * height;
        if (area <= maxPixels)
        {
            return (width, height);
        }

        double k = Math.Sqrt(maxPixels / area);
        return (Math.Max(1, (int)(width * k)), Math.Max(1, (int)(height * k)));
    }

    public static PixelImage Solid(Rgb color, int width, int height)
    {
        var image = new PixelImage(width, height);
        byte b = Byte(color.B), g = Byte(color.G), r = Byte(color.R);
        var bgra = image.Bgra;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = 255;
        }

        return image;
    }

    /// <summary>
    /// Rasterizes a gradient: its linear sweep, then its lights (added in the dark theme; in the light theme they tint the
    /// pale background instead). With <paramref name="dither"/>, a little noise below one step of 8-bit color keeps the
    /// long dark ramps from banding.
    /// </summary>
    public static PixelImage Gradient(BackdropGradient gradient, int width, int height, Rgb accent, bool dark, Rgb lightBase, bool dither)
    {
        var image = new PixelImage(width, height);
        width = image.Width;
        height = image.Height;
        double aspect = (double)width / height;
        var from = Rgb.Parse(gradient.From);
        var to = Rgb.Parse(gradient.To);
        if (!dark)
        {
            from = lightBase.Mix(from.Shift(0, 0.2), 0.07);
            to = lightBase.Mix(to.Shift(0, 0.2), 0.04);
        }

        // The sweep: t runs 0 → 1 along the angle across the whole frame (measured in heights, so it isn't squashed).
        double angle = gradient.Angle * Math.PI / 180;
        double dx = Math.Cos(angle), dy = Math.Sin(angle);
        double t0 = Math.Min(Math.Min(0, aspect * dx), Math.Min(dy, (aspect * dx) + dy));
        double t1 = Math.Max(Math.Max(0, aspect * dx), Math.Max(dy, (aspect * dx) + dy));
        double span = Math.Max(1e-6, t1 - t0);

        var glows = gradient.Glows.Select(g => (Glow: g, Color: BackdropPresets.Resolve(g.Color, accent))).ToArray();
        var across = new float[glows.Length][];
        var down = new float[glows.Length][];
        for (int i = 0; i < glows.Length; i++)
        {
            var g = glows[i].Glow;
            across[i] = new float[width];
            down[i] = new float[height];
            for (int x = 0; x < width; x++)
            {
                double u = ((x + 0.5) / height) - (g.X * aspect);
                across[i][x] = (float)Math.Exp(-GlowFalloff * (u * u) / (g.RadiusX * g.RadiusX));
            }

            for (int y = 0; y < height; y++)
            {
                double v = ((y + 0.5) / height) - g.Y;
                down[i][y] = (float)Math.Exp(-GlowFalloff * (v * v) / (g.RadiusY * g.RadiusY));
            }
        }

        var bgra = image.Bgra;
        Parallel.For(0, height, y =>
        {
            uint seed = (uint)((y * 2654435761u) ^ 0x9E3779B9u) | 1u;
            double fy = (y + 0.5) / height;
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                double fx = (x + 0.5) / height;
                float t = (float)((((fx * dx) + (fy * dy)) - t0) / span);
                float r = from.R + ((to.R - from.R) * t);
                float g = from.G + ((to.G - from.G) * t);
                float b = from.B + ((to.B - from.B) * t);
                for (int i = 0; i < glows.Length; i++)
                {
                    float f = across[i][x] * down[i][y] * (float)glows[i].Glow.Strength;
                    var c = glows[i].Color;
                    if (dark)
                    {
                        r += c.R * f;
                        g += c.G * f;
                        b += c.B * f;
                    }
                    else
                    {
                        float k = f * 0.45f;
                        r += (c.R - r) * k;
                        g += (c.G - g) * k;
                        b += (c.B - b) * k;
                    }
                }

                float noise = 0;
                if (dither)
                {
                    noise = (Next(ref seed) - Next(ref seed)) / 255f;
                }

                int p = row + (x * 4);
                bgra[p] = Byte(b + noise);
                bgra[p + 1] = Byte(g + noise);
                bgra[p + 2] = Byte(r + noise);
                bgra[p + 3] = 255;
            }
        });

        return image;
    }

    /// <summary>The picture scaled (keeping its shape) to cover the size, centered, cropping what overflows.</summary>
    public static PixelImage Cover(PixelImage source, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        double scale = Math.Max((double)width / source.Width, (double)height / source.Height);

        // Shrinking a lot: average blocks first, so the bilinear step below doesn't skip pixels (and shimmer).
        int shrink = (int)Math.Floor(1 / (scale * 1.5));
        if (shrink >= 2)
        {
            source = Downsample(source, shrink);
            scale = Math.Max((double)width / source.Width, (double)height / source.Height);
        }

        double offsetX = (source.Width - (width / scale)) / 2;
        double offsetY = (source.Height - (height / scale)) / 2;
        var image = new PixelImage(width, height);
        var src = source.Bgra;
        var dst = image.Bgra;
        int sw = source.Width, sh = source.Height;
        Parallel.For(0, height, y =>
        {
            double sy = Math.Clamp(offsetY + ((y + 0.5) / scale) - 0.5, 0, sh - 1);
            int y0 = (int)sy, y1 = Math.Min(sh - 1, y0 + 1);
            float fy = (float)(sy - y0);
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp(offsetX + ((x + 0.5) / scale) - 0.5, 0, sw - 1);
                int x0 = (int)sx, x1 = Math.Min(sw - 1, x0 + 1);
                float fx = (float)(sx - x0);
                int a = ((y0 * sw) + x0) * 4, b = ((y0 * sw) + x1) * 4, c = ((y1 * sw) + x0) * 4, d = ((y1 * sw) + x1) * 4;
                int o = ((y * width) + x) * 4;
                for (int ch = 0; ch < 3; ch++)
                {
                    float top = src[a + ch] + ((src[b + ch] - src[a + ch]) * fx);
                    float bottom = src[c + ch] + ((src[d + ch] - src[c + ch]) * fx);
                    dst[o + ch] = (byte)Math.Clamp((int)(top + ((bottom - top) * fy) + 0.5f), 0, 255);
                }

                dst[o + 3] = 255;
            }
        });

        return image;
    }

    /// <summary>A smaller copy: each pixel the average of a <paramref name="factor"/> × <paramref name="factor"/> block.</summary>
    public static PixelImage Downsample(PixelImage source, int factor)
    {
        if (factor <= 1)
        {
            return source;
        }

        int width = Math.Max(1, source.Width / factor);
        int height = Math.Max(1, source.Height / factor);
        var image = new PixelImage(width, height);
        var src = source.Bgra;
        var dst = image.Bgra;
        int sw = source.Width;
        int count = factor * factor;
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0;
                for (int j = 0; j < factor; j++)
                {
                    int row = (((y * factor) + j) * sw) + (x * factor);
                    for (int i = 0; i < factor; i++)
                    {
                        int p = (row + i) * 4;
                        b += src[p];
                        g += src[p + 1];
                        r += src[p + 2];
                    }
                }

                int o = ((y * width) + x) * 4;
                dst[o] = (byte)((b + (count / 2)) / count);
                dst[o + 1] = (byte)((g + (count / 2)) / count);
                dst[o + 2] = (byte)((r + (count / 2)) / count);
                dst[o + 3] = 255;
            }
        });

        return image;
    }

    /// <summary>Lays the picture over a color: the picture at <paramref name="opacity"/>, the color showing through the rest.</summary>
    public static void Over(PixelImage image, Rgb under, double opacity)
    {
        float a = (float)Math.Clamp(opacity, 0, 1);
        float ub = under.B * 255 * (1 - a), ug = under.G * 255 * (1 - a), ur = under.R * 255 * (1 - a);
        var bgra = image.Bgra;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)Math.Clamp((int)((bgra[i] * a) + ub + 0.5f), 0, 255);
            bgra[i + 1] = (byte)Math.Clamp((int)((bgra[i + 1] * a) + ug + 0.5f), 0, 255);
            bgra[i + 2] = (byte)Math.Clamp((int)((bgra[i + 2] * a) + ur + 0.5f), 0, 255);
            bgra[i + 3] = 255;
        }
    }

    /// <summary>More (above 1) or less color, around each pixel's own brightness.</summary>
    public static void Saturate(PixelImage image, double amount)
    {
        float k = (float)amount;
        var bgra = image.Bgra;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            float b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            float l = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
            bgra[i] = (byte)Math.Clamp((int)(l + ((b - l) * k) + 0.5f), 0, 255);
            bgra[i + 1] = (byte)Math.Clamp((int)(l + ((g - l) * k) + 0.5f), 0, 255);
            bgra[i + 2] = (byte)Math.Clamp((int)(l + ((r - l) * k) + 0.5f), 0, 255);
        }
    }

    /// <summary>
    /// A Gaussian-like blur: three passes of a box blur across and down (edges extend outward), worked out in floating
    /// point and written back with half a step of noise (since 1.8.6), so a wide blur of a dark gradient doesn't come out
    /// in steps. A flat color stays exactly the same.
    /// </summary>
    public static void Blur(PixelImage image, int radius, int passes = 3)
    {
        if (radius < 1)
        {
            return;
        }

        int width = image.Width, height = image.Height;
        var bgra = image.Bgra;
        var a = new float[width * height * 3];
        var b = new float[a.Length];
        for (int i = 0, p = 0; i < a.Length; i += 3, p += 4)
        {
            a[i] = bgra[p];
            a[i + 1] = bgra[p + 1];
            a[i + 2] = bgra[p + 2];
        }

        for (int pass = 0; pass < passes; pass++)
        {
            BoxAcross(a, b, width, height, radius);
            BoxDown(b, a, width, height, radius);
        }

        Parallel.For(0, height, y =>
        {
            uint seed = (uint)(y * 2654435761u) | 1u;
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 3;
                int p = ((y * width) + x) * 4;
                float noise = Next(ref seed) - 0.5f;
                bgra[p] = (byte)Math.Clamp((int)(a[i] + noise + 0.5f), 0, 255);
                bgra[p + 1] = (byte)Math.Clamp((int)(a[i + 1] + noise + 0.5f), 0, 255);
                bgra[p + 2] = (byte)Math.Clamp((int)(a[i + 2] + noise + 0.5f), 0, 255);
                bgra[p + 3] = 255;
            }
        });
    }

    private static void BoxAcross(float[] src, float[] dst, int width, int height, int radius)
    {
        float window = (2 * radius) + 1; // divided, not multiplied by 1/window: a flat color stays exact
        Parallel.For(0, height, y =>
        {
            int row = y * width * 3;
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    sum += src[row + (Math.Clamp(i, 0, width - 1) * 3) + c];
                }

                for (int x = 0; x < width; x++)
                {
                    dst[row + (x * 3) + c] = sum / window;
                    sum += src[row + (Math.Min(width - 1, x + radius + 1) * 3) + c] - src[row + (Math.Max(0, x - radius) * 3) + c];
                }
            }
        });
    }

    private static void BoxDown(float[] src, float[] dst, int width, int height, int radius)
    {
        float window = (2 * radius) + 1;
        int stride = width * 3;
        Parallel.For(0, width, x =>
        {
            int column = x * 3;
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    sum += src[(Math.Clamp(i, 0, height - 1) * stride) + column + c];
                }

                for (int y = 0; y < height; y++)
                {
                    dst[(y * stride) + column + c] = sum / window;
                    sum += src[(Math.Min(height - 1, y + radius + 1) * stride) + column + c] - src[(Math.Max(0, y - radius) * stride) + column + c];
                }
            }
        });
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)((value * 255) + 0.5f), 0, 255);

    /// <summary>A uniform number in [0, 1) (xorshift; fast and good enough for dithering).</summary>
    private static float Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / 16777216f;
    }
}
