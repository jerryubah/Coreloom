using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Coreloom.Core.Hardware;
using Coreloom.Mac.Themes;

namespace Coreloom.Mac.Controls;

/// <summary>
/// Finds vendor logo files. Coreloom does not draw or bundle other companies' logos: when the user supplies official
/// files they are shown, otherwise <see cref="BrandMark"/> shows a plain-text vendor badge. For key "nvidia" on a dark
/// theme it looks in the user's logo folder (~/Library/Application Support/Coreloom/Vendors) for nvidia-on-dark.png, then
/// nvidia.png; light themes look for "-on-light" first.
/// </summary>
internal static class BrandLogos
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly Dictionary<string, Bitmap?> Cache = [];
    private static string? _folder;

    public static void Configure(string? userFolder)
    {
        _folder = userFolder;
        Cache.Clear();
    }

    public static Bitmap? Get(string key, bool darkTheme)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_folder))
        {
            return null;
        }

        string cacheKey = key + (darkTheme ? "|dark" : "|light");
        if (!Cache.TryGetValue(cacheKey, out var image))
        {
            image = Load($"{key}-{(darkTheme ? "on-dark" : "on-light")}.png") ?? Load($"{key}.png");
            Cache[cacheKey] = image;
        }

        return image;
    }

    private static Bitmap? Load(string fileName)
    {
        try
        {
            var info = new FileInfo(Path.Combine(_folder!, fileName));
            if (!info.Exists || info.Length is 0 or > MaxFileBytes)
            {
                return null;
            }

            using var stream = info.OpenRead();
            return Bitmap.DecodeToHeight(stream, 256);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The vendor mark shown next to a CPU or GPU name: the supplied logo file when one is installed, otherwise a small badge
/// with the vendor's name in plain text. Hidden when the vendor is unknown.
/// </summary>
public sealed class BrandMark : Border
{
    public static readonly StyledProperty<string?> VendorProperty = AvaloniaProperty.Register<BrandMark, string?>(nameof(Vendor), string.Empty);

    public static readonly StyledProperty<double> MarkHeightProperty = AvaloniaProperty.Register<BrandMark, double>(nameof(MarkHeight), 22.0);

    private bool _attached;

    public BrandMark()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Rebuild();
    }

    /// <summary>"nvidia", "amd", "intel" or "apple" (see <see cref="HardwareVendors.Key"/>).</summary>
    public string? Vendor
    {
        get => GetValue(VendorProperty);
        set => SetValue(VendorProperty, value);
    }

    public double MarkHeight
    {
        get => GetValue(MarkHeightProperty);
        set => SetValue(MarkHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VendorProperty || change.Property == MarkHeightProperty)
        {
            Rebuild();
        }
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        if (ThemeService.Instance is { } theme)
        {
            theme.AppIconChanged += OnThemeChanged;
        }

        Rebuild();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (ThemeService.Instance is { } theme)
        {
            theme.AppIconChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var vendor = HardwareVendors.FromKey(Vendor);
        if (vendor == HardwareVendor.Unknown)
        {
            Child = null;
            IsVisible = false;
            return;
        }

        IsVisible = true;
        string name = HardwareVendors.DisplayName(vendor);
        ToolTip.SetTip(this, name);
        var logo = BrandLogos.Get(HardwareVendors.Key(vendor), ThemeService.Instance?.IsDark ?? true);
        if (logo is not null)
        {
            Background = null;
            CornerRadius = default;
            Padding = default;
            Child = new Image { Source = logo, Height = MarkHeight, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            return;
        }

        // No logo file installed: a neutral text badge in the app's own typeface and colours.
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Brush.ControlHover");
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 3, 8, 3);
        var label = new TextBlock
        {
            Text = name,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Brush.TextSecondary");
        Child = label;
    }
}
