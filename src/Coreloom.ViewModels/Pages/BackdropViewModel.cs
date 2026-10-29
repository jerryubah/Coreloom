using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Appearance;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// Settings › Appearance › Background: what fills the window behind the glass. Suggested dark colors, gradients and
/// pictures (or your own color or picture), how strongly a picture shows through its frosted layer and how much that
/// layer blurs it, or the desktop seen through the window. Every change applies at once and is saved.
/// </summary>
public sealed class BackdropViewModel : ObservableObject
{
    /// <summary>Pictures larger than this aren't copied (a camera's raw file, say).</summary>
    public const long MaxImageBytes = 60L * 1024 * 1024;

    private readonly SettingsService _settings;
    private readonly IShellService _shell;
    private readonly IThemeService _theme;
    private readonly bool _isMac;
    private string? _customColor;
    private string? _message;

    public BackdropViewModel(SettingsService settings, IShellService shell, IThemeService theme, bool isMac)
    {
        _settings = settings;
        _shell = shell;
        _theme = theme;
        _isMac = isMac;
        var kinds = new List<Option<BackdropKind>>
        {
            new(BackdropKind.Gradient, "Gradients"),
            new(BackdropKind.Color, "Colors"),
            new(BackdropKind.Image, "Pictures"),
        };
        if (theme.IsMicaSupported)
        {
            kinds.Add(new(BackdropKind.SeeThrough, isMac ? "Translucent" : "Mica"));
        }

        Kinds = kinds;
        SelectColorCommand = new RelayCommand<string>(hex =>
        {
            if (AccentPalette.IsValidHex(hex))
            {
                _customColor = null;
                Change(b =>
                {
                    b.Kind = BackdropKind.Color;
                    b.Color = hex!.ToUpperInvariant();
                });
            }
        });
        SelectGradientCommand = new RelayCommand<string>(id =>
        {
            if (BackdropPresets.FindGradient(id) is not null)
            {
                Change(b =>
                {
                    b.Kind = BackdropKind.Gradient;
                    b.Gradient = id!;
                });
            }
        });
        SelectImageCommand = new RelayCommand<string>(id =>
        {
            if (id == BackdropSettings.CustomImageId ? Current.CustomImage is not null : BackdropPresets.FindImage(id) is not null)
            {
                Change(b =>
                {
                    b.Kind = BackdropKind.Image;
                    b.Image = id!;
                });
            }
        });
        ChooseImageCommand = new RelayCommand(ChooseImage, () => CanChooseImage);
    }

    private BackdropSettings Current => _settings.Current.Appearance.Backdrop;

    public IReadOnlyList<Option<BackdropKind>> Kinds { get; }

    public IReadOnlyList<BackdropColor> Colors => BackdropPresets.Colors;

    public IReadOnlyList<BackdropGradient> Gradients => BackdropPresets.Gradients;

    public IReadOnlyList<BackdropPicture> Images => BackdropPresets.Images;

    public BackdropKind Kind
    {
        get => Current.Kind;
        set
        {
            if (Current.Kind != value && Kinds.Any(k => k.Value == value))
            {
                Change(b => b.Kind = value);
            }
        }
    }

    public bool IsColor => Kind == BackdropKind.Color;

    public bool IsGradient => Kind == BackdropKind.Gradient;

    public bool IsImage => Kind == BackdropKind.Image;

    public bool IsSeeThrough => Kind == BackdropKind.SeeThrough;

    public string SeeThroughText => _isMac
        ? "Your desktop shows through the whole window, blurred by macOS and darkened to the theme; the glass cards sit on it. macOS shows a solid background instead when Reduce transparency is on in Accessibility settings."
        : "Windows 11's Mica: your desktop's colors tint the whole window; the glass cards sit on it.";

    /// <summary>The chosen color (#RRGGBB).</summary>
    public string Color => Current.Color;

    /// <summary>The chosen gradient's id.</summary>
    public string Gradient => Current.Gradient;

    /// <summary>The chosen picture's id (or "custom").</summary>
    public string Image => Current.Image;

    /// <summary>The name of the chosen color, or the hex value for any other.</summary>
    public string ColorName => BackdropPresets.FindColor(Color)?.Name ?? Color;

    /// <summary>Free-form color entry (#RRGGBB). Invalid input is kept in the box but not applied.</summary>
    public string CustomColor
    {
        get => _customColor ?? Color;
        set
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 6 && !text.StartsWith('#'))
            {
                text = "#" + text;
            }

            _customColor = text;
            OnPropertyChanged();
            if (AccentPalette.IsValidHex(text) && !string.Equals(text, Color, StringComparison.OrdinalIgnoreCase))
            {
                Change(b =>
                {
                    b.Kind = BackdropKind.Color;
                    b.Color = text.ToUpperInvariant();
                });
            }
        }
    }

    /// <summary>Your own picture's file (full path), for its swatch; null when none was chosen.</summary>
    public string? CustomImagePath =>
        Current.CustomImage is { } name && _theme.BackdropFolder is { } folder ? Path.Combine(folder, name) : null;

    public bool HasCustomImage => CustomImagePath is { } path && File.Exists(path);

    public bool CanChooseImage => _theme.BackdropFolder is not null;

    /// <summary>How strongly the picture shows through its frosted layer (10–100 %).</summary>
    public double ImageOpacity
    {
        get => Current.ImageOpacity;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, BackdropSettings.MinimumOpacity, 100));
            if (v != Current.ImageOpacity)
            {
                Change(b => b.ImageOpacity = v);
            }
        }
    }

    /// <summary>How much the frosted layer blurs the picture (0–100 %).</summary>
    public double Blur
    {
        get => Current.Blur;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, 0, 100));
            if (v != Current.Blur)
            {
                Change(b => b.Blur = v);
            }
        }
    }

    public string ImageOpacityText => $"{Current.ImageOpacity} %";

    public string BlurText => Current.Blur == 0 ? "Sharp" : $"{Current.Blur} %";

    /// <summary>Why your own picture couldn't be used (shown under the pictures until the next choice).</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message is not null;

    public IRelayCommand<string> SelectColorCommand { get; }

    public IRelayCommand<string> SelectGradientCommand { get; }

    public IRelayCommand<string> SelectImageCommand { get; }

    public IRelayCommand ChooseImageCommand { get; }

    /// <summary>Asks for a picture and keeps a copy in Coreloom's folder (so it stays put if the original moves).</summary>
    private void ChooseImage()
    {
        if (_theme.BackdropFolder is not { } folder)
        {
            return;
        }

        string filter = _isMac
            ? "Pictures (*.jpg;*.jpeg;*.png;*.heic;*.webp)|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp"
            : "Pictures (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp";
        string? path = _shell.PickOpenFile("Choose a picture for Coreloom's background", filter,
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (path is null)
        {
            return;
        }

        Message = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                Message = "That picture couldn't be found.";
                return;
            }

            if (info.Length > MaxImageBytes)
            {
                Message = "That picture is too large (over 60 MB). Try a smaller copy of it.";
                return;
            }

            Directory.CreateDirectory(folder);
            string name = $"custom-{DateTime.UtcNow:yyyyMMddHHmmss}{info.Extension.ToLowerInvariant()}";
            File.Copy(path, Path.Combine(folder, name), overwrite: true);
            string? previous = Current.CustomImage;
            Change(b =>
            {
                b.Kind = BackdropKind.Image;
                b.Image = BackdropSettings.CustomImageId;
                b.CustomImage = name;
            });

            if (previous is not null && previous != name)
            {
                TryDelete(Path.Combine(folder, previous));
            }

            Log.Info("Appearance", $"Background picture chosen ({info.Length / 1024} KB).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Warn("Appearance", "Copying the background picture failed.", e);
            Message = "Coreloom couldn't read that picture. Try another, or a copy of it in your Pictures folder.";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An old copy left behind is harmless.
        }
    }

    private void Change(Action<BackdropSettings> mutate)
    {
        _settings.Update("Appearance", s => mutate(s.Appearance.Backdrop));
        Refresh();
    }

    /// <summary>Every property again (after a change here or elsewhere).</summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        ChooseImageCommand.NotifyCanExecuteChanged();
    }
}
