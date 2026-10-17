using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Overlay;

/// <summary>A reading that can be shown on the overlay (a checkbox in its settings).</summary>
public sealed class OverlayItemOptionViewModel : ObservableObject
{
    private readonly OverlaySettingsViewModel _owner;

    public OverlayItemOptionViewModel(OverlaySettingsViewModel owner, string id, string name)
    {
        _owner = owner;
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsShown
    {
        get => _owner.IsShown(Id);
        set => _owner.SetShown(Id, value);
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsShown));
}

/// <summary>A line of the overlay in its settings: the readings it can show and its size (S, M or L).</summary>
public sealed class OverlayItemGroupViewModel : ObservableObject
{
    private readonly OverlaySettingsViewModel _owner;

    public OverlayItemGroupViewModel(OverlaySettingsViewModel owner, OverlayGroup group, string name, IReadOnlyList<OverlayItemOptionViewModel> items)
    {
        _owner = owner;
        Group = group;
        Name = name;
        Items = items;
    }

    public OverlayGroup Group { get; }

    public string Name { get; }

    public IReadOnlyList<OverlayItemOptionViewModel> Items { get; }

    public IReadOnlyList<Option<OverlaySize>> SizeOptions { get; } =
        [new(OverlaySize.Compact, "S"), new(OverlaySize.Standard, "M"), new(OverlaySize.Expanded, "L")];

    /// <summary>The line's size: S (compact), M (standard) or L (expanded).</summary>
    public Option<OverlaySize> SelectedSize
    {
        get => SizeOptions.First(o => o.Value == _owner.GroupSize(Group));
        set
        {
            if (value is not null)
            {
                _owner.SetGroupSize(Group, value.Value);
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(SelectedSize));
}

/// <summary>A game with a place of its own for the overlay (× forgets it).</summary>
public sealed record OverlayGamePlacementChip(string Program, string PlaceText, IRelayCommand RemoveCommand) : StaticRecord;

/// <summary>One of the eight places the overlay can sit (a cell in the position picker).</summary>
public sealed class OverlayPositionOptionViewModel : ObservableObject
{
    private readonly OverlaySettingsViewModel _owner;

    public OverlayPositionOptionViewModel(OverlaySettingsViewModel owner, OverlayPosition position, string name, int row, int column)
    {
        _owner = owner;
        Position = position;
        Name = name;
        Row = row;
        Column = column;
        SelectCommand = new RelayCommand(() => _owner.Position = Position);
    }

    public OverlayPosition Position { get; }

    public string Name { get; }

    public int Row { get; }

    public int Column { get; }

    public bool IsSelected => _owner.Position == Position;

    public IRelayCommand SelectCommand { get; }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>A color dot (text or background).</summary>
public sealed class OverlayColorOption : ObservableObject
{
    private readonly Func<string> _current;

    public OverlayColorOption(string name, string hex, Func<string> current, Action<string> select)
    {
        Name = name;
        Hex = hex;
        _current = current;
        SelectCommand = new RelayCommand(() => select(Hex));
    }

    public string Name { get; }

    public string Hex { get; }

    public bool IsSelected => string.Equals(_current(), Hex, StringComparison.OrdinalIgnoreCase);

    public IRelayCommand SelectCommand { get; }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>
/// The in-game overlay's settings card on the Graphics page: on or off, the hotkey, what it shows, where it sits and how
/// it looks. Every change is saved and shows right away, on the overlay and in the card's live preview.
/// </summary>
public sealed class OverlaySettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private bool _isOpen;
    private bool _isCapturingHotkey;
    private string? _textColorText;
    private string? _backgroundColorText;

    public OverlaySettingsViewModel(SettingsService settings, OverlayViewModel preview)
    {
        _settings = settings;
        Preview = preview;
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() =>
        {
            IsCapturingHotkey = false;
            IsOpen = false;
        });
        ChangeHotkeyCommand = new RelayCommand(() => IsCapturingHotkey = !IsCapturingHotkey);
        ClearHotkeyCommand = new RelayCommand(() =>
        {
            IsCapturingHotkey = false;
            Save(s => s.Hotkey = OverlayHotkey.None);
        });
        ResetHotkeyCommand = new RelayCommand(() =>
        {
            IsCapturingHotkey = false;
            Save(s => s.Hotkey = OverlayHotkey.Default);
        });
        SetTextColorCommand = new RelayCommand<string>(hex => TextColor = hex ?? OverlaySettings.DefaultTextColor);
        SetBackgroundColorCommand = new RelayCommand<string>(hex => BackgroundColor = hex ?? OverlaySettings.DefaultBackgroundColor);
        ResetCommand = new RelayCommand(Reset);
        ResetOffsetCommand = new RelayCommand(() => Save(s =>
        {
            s.OffsetX = 0;
            s.OffsetY = 0;
        }));
        MoveOnScreenCommand = new RelayCommand(() =>
        {
            Enabled = true;
            IsCapturingHotkey = false;
            IsOpen = false; // out of the way, so the overlay can be dragged over the screen
            Preview.IsMoving = true;
        });

        ItemGroups = Enum.GetValues<OverlayGroup>()
            .Select(g => new OverlayItemGroupViewModel(this, g, GroupName(g), OverlayCatalog.All
                .Where(i => i.Group == g)
                .Select(i => new OverlayItemOptionViewModel(this, i.Id, i.Name))
                .ToArray()))
            .ToArray();
        TextColors =
        [
            Color("White", "#FFFFFF"),
            Color("Silver", "#C9D1D9"),
            Color("Blue", "#5B8CFF"),
            Color("Cyan", "#3CC8D8"),
            Color("Green", "#45D6A0"),
            Color("Yellow", "#FFD54A"),
            Color("Orange", "#FFB454"),
            Color("Pink", "#FF7AB6"),
        ];
        BackgroundColors =
        [
            Background("Black", "#000000"),
            Background("Charcoal", "#0E1117"),
            Background("Slate", "#1C2230"),
            Background("Navy", "#0B1A33"),
            Background("Graphite", "#2A2F3A"),
            Background("White", "#FFFFFF"),
        ];
        Positions =
        [
            new(this, OverlayPosition.TopLeft, "Top left", 0, 0),
            new(this, OverlayPosition.TopCenter, "Top center", 0, 1),
            new(this, OverlayPosition.TopRight, "Top right", 0, 2),
            new(this, OverlayPosition.MiddleLeft, "Middle left", 1, 0),
            new(this, OverlayPosition.Center, "Center", 1, 1),
            new(this, OverlayPosition.MiddleRight, "Middle right", 1, 2),
            new(this, OverlayPosition.BottomLeft, "Bottom left", 2, 0),
            new(this, OverlayPosition.BottomCenter, "Bottom center", 2, 1),
            new(this, OverlayPosition.BottomRight, "Bottom right", 2, 2),
        ];

        settings.Changed += section =>
        {
            if (section is "Overlay" or "All")
            {
                Reload();
            }
        };
        preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OverlayViewModel.ScreenScale) or nameof(OverlayViewModel.Factor))
            {
                OnPropertyChanged(nameof(ScreenScaleText));
            }
        };
    }

    private OverlaySettings Options => _settings.Current.Overlay;

    /// <summary>The overlay itself, shown in the card as a live preview.</summary>
    public OverlayViewModel Preview { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>The overlay is on (the switch on the Graphics page and in the card; the hotkey flips it too).</summary>
    public bool Enabled
    {
        get => Options.Enabled;
        set => Save(s =>
        {
            s.Enabled = value;
            s.HasBeenEnabled |= value; // from now on the hotkey works
        });
    }

    public string HotkeyText => Options.Hotkey.ToString();

    public bool HasHotkey => !Options.Hotkey.IsNone;

    /// <summary>"Press End to show or hide it." for the Graphics page.</summary>
    public string HotkeyHint => HasHotkey ? $"Press {HotkeyText} to show or hide it, also in games." : "No hotkey set.";

    /// <summary>A key on its own (no Ctrl, Alt…) is also seen while typing in other apps.</summary>
    public bool HotkeyIsSingleKey => HasHotkey && Options.Hotkey.Modifiers == HotkeyModifiers.None;

    /// <summary>Waiting for the next key press to become the hotkey (the app pauses the hotkey meanwhile).</summary>
    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        private set => SetProperty(ref _isCapturingHotkey, value);
    }

    public IRelayCommand ChangeHotkeyCommand { get; }

    public IRelayCommand ClearHotkeyCommand { get; }

    public IRelayCommand ResetHotkeyCommand { get; }

    public IReadOnlyList<OverlayItemGroupViewModel> ItemGroups { get; }

    public IReadOnlyList<OverlayPositionOptionViewModel> Positions { get; }

    /// <summary>One of the nine places; picking one starts it there again (the fine-tuning offset goes back to zero).</summary>
    public OverlayPosition Position
    {
        get => Options.Position;
        set => Save(s =>
        {
            s.Position = value;
            s.OffsetX = 0;
            s.OffsetY = 0;
        });
    }

    /// <summary>Fine-tuning: pixels right of the place (negative: left), at 100 % scaling.</summary>
    public int OffsetX
    {
        get => Options.OffsetX;
        set => Save(s => s.OffsetX = value);
    }

    /// <summary>Fine-tuning: pixels below the place (negative: above), at 100 % scaling.</summary>
    public int OffsetY
    {
        get => Options.OffsetY;
        set => Save(s => s.OffsetY = value);
    }

    public IRelayCommand ResetOffsetCommand { get; }

    /// <summary>Shows the overlay so it can be dragged into place and resized by its corner (also Shift + the hotkey, in games).</summary>
    public IRelayCommand MoveOnScreenCommand { get; }

    /// <summary>"Shift + End moves it in games."</summary>
    public string MoveHint => HasHotkey ? $"In a game, Shift + {HotkeyText} does the same." : "Set a hotkey to move it from inside games too.";

    /// <summary>Keep a place for each game: moving the overlay while a game is in front saves it for that game.</summary>
    public bool PerGamePosition
    {
        get => Options.PerGamePosition;
        set => Save(s => s.PerGamePosition = value);
    }

    /// <summary>Games with a place of their own.</summary>
    public IReadOnlyList<OverlayGamePlacementChip> GamePlacements => Options.GamePlacements
        .Select(p => new OverlayGamePlacementChip(p.Program, $"{p.Program} · {PlaceName(p.Position)}",
            new RelayCommand(() => Save(s => s.GamePlacements.RemoveAll(g => string.Equals(g.Program, p.Program, StringComparison.OrdinalIgnoreCase))))))
        .ToArray();

    public bool HasGamePlacements => Options.GamePlacements.Count > 0;

    public IReadOnlyList<Option<OverlaySize>> SizeOptions { get; } =
        [new(OverlaySize.Compact, "Compact"), new(OverlaySize.Standard, "Standard"), new(OverlaySize.Expanded, "Expanded"), new(OverlaySize.Custom, "Custom")];

    /// <summary>Compact, Standard or Expanded, or Custom (its own scale, e.g. after dragging its corner).</summary>
    public Option<OverlaySize> SelectedSize
    {
        get => SizeOptions.First(o => o.Value == Options.Size);
        set
        {
            if (value is not null)
            {
                Save(s =>
                {
                    if (value.Value == OverlaySize.Custom && s.Size != OverlaySize.Custom)
                    {
                        s.Scale = (int)Math.Round(s.SizeFactor * 100); // starts from the size it had
                    }

                    s.Size = value.Value;
                });
            }
        }
    }

    /// <summary>The overlay's own scale in percent (moving it makes the size Custom).</summary>
    public int Scale
    {
        get => (int)Math.Round(Options.SizeFactor * 100);
        set => Save(s =>
        {
            s.Size = OverlaySize.Custom;
            s.Scale = value;
        });
    }

    /// <summary>Grows on big screens and shrinks on small ones.</summary>
    public bool AutoScale
    {
        get => Options.AutoScale;
        set => Save(s => s.AutoScale = value);
    }

    /// <summary>"× 1.25 on this screen" when the screen changes the size.</summary>
    public string ScreenScaleText => Options.AutoScale && Math.Abs(Preview.ScreenScale - 1) > 0.001
        ? $"× {Preview.ScreenScale.ToString("0.##", CultureInfo.CurrentCulture)} on the screen it's on"
        : "Same size on every screen";

    internal OverlaySize GroupSize(OverlayGroup group) =>
        Options.GroupSizes.TryGetValue(group.ToString(), out var size) ? size : OverlaySize.Standard;

    internal void SetGroupSize(OverlayGroup group, OverlaySize size) => Save(s =>
    {
        if (size == OverlaySize.Standard)
        {
            s.GroupSizes.Remove(group.ToString());
        }
        else
        {
            s.GroupSizes[group.ToString()] = size;
        }
    });

    public IReadOnlyList<Option<OverlayLayout>> LayoutOptions { get; } =
        [new(OverlayLayout.Vertical, "Stacked"), new(OverlayLayout.Horizontal, "One line")];

    public Option<OverlayLayout> SelectedLayout
    {
        get => LayoutOptions.First(o => o.Value == Options.Layout);
        set
        {
            if (value is not null)
            {
                Save(s => s.Layout = value.Value);
            }
        }
    }

    public IReadOnlyList<Option<OverlayFontWeight>> WeightOptions { get; } =
        [new(OverlayFontWeight.Regular, "Regular"), new(OverlayFontWeight.Medium, "Medium"), new(OverlayFontWeight.SemiBold, "Semibold"), new(OverlayFontWeight.Bold, "Bold")];

    public Option<OverlayFontWeight> SelectedWeight
    {
        get => WeightOptions.First(o => o.Value == Options.FontWeight);
        set
        {
            if (value is not null)
            {
                Save(s => s.FontWeight = value.Value);
            }
        }
    }

    public double FontSize
    {
        get => Options.FontSize;
        set => Save(s => s.FontSize = value);
    }

    public int TextOpacity
    {
        get => Options.TextOpacity;
        set => Save(s => s.TextOpacity = value);
    }

    public int BackgroundOpacity
    {
        get => Options.BackgroundOpacity;
        set => Save(s => s.BackgroundOpacity = value);
    }

    public int CornerRadius
    {
        get => Options.CornerRadius;
        set => Save(s => s.CornerRadius = value);
    }

    public int EdgeMargin
    {
        get => Options.EdgeMargin;
        set => Save(s => s.EdgeMargin = value);
    }

    public bool ShowLabels
    {
        get => Options.ShowLabels;
        set => Save(s => s.ShowLabels = value);
    }

    public bool ColorWarnings
    {
        get => Options.ColorWarnings;
        set => Save(s => s.ColorWarnings = value);
    }

    public bool TextShadow
    {
        get => Options.TextShadow;
        set => Save(s => s.TextShadow = value);
    }

    public IReadOnlyList<OverlayColorOption> TextColors { get; }

    public IReadOnlyList<OverlayColorOption> BackgroundColors { get; }

    /// <summary>Text color as #RRGGBB; anything else stays in the box without being applied.</summary>
    public string TextColor
    {
        get => _textColorText ?? Options.TextColor;
        set => SetColor(value, ref _textColorText, (s, hex) => s.TextColor = hex);
    }

    public string BackgroundColor
    {
        get => _backgroundColorText ?? Options.BackgroundColor;
        set => SetColor(value, ref _backgroundColorText, (s, hex) => s.BackgroundColor = hex);
    }

    public IRelayCommand<string> SetTextColorCommand { get; }

    public IRelayCommand<string> SetBackgroundColorCommand { get; }

    /// <summary>Restores what is shown and how it looks (not the on/off switch or the hotkey).</summary>
    public IRelayCommand ResetCommand { get; }

    /// <summary>Why frames per second can't be measured, when they can't.</summary>
    public string? FrameRateNote => Preview.FrameRates is { IsAvailable: false } source ? source.Problem : null;

    public bool IsShown(string id) => Options.Items.Contains(id);

    public void SetShown(string id, bool shown)
    {
        if (shown != IsShown(id))
        {
            Save(s => s.Items = OverlayCatalog.Normalize(shown ? s.Items.Append(id) : s.Items.Where(i => i != id)));
        }
    }

    /// <summary>
    /// The key pressed while waiting for a new hotkey. Esc cancels; a modifier on its own keeps waiting. Returns true
    /// when the key press was used.
    /// </summary>
    public bool CaptureHotkey(int key, HotkeyModifiers modifiers)
    {
        if (!IsCapturingHotkey)
        {
            return false;
        }

        if (key == 0x1B && modifiers == HotkeyModifiers.None)
        {
            IsCapturingHotkey = false;
            return true;
        }

        if (key <= 0 || key > 0xFE || Hotkeys.IsModifierKey(key))
        {
            return false;
        }

        IsCapturingHotkey = false;
        Save(s => s.Hotkey = new OverlayHotkey { Key = key, Modifiers = modifiers });
        return true;
    }

    /// <summary>Called when the frame-rate source changes (so the note under FPS updates).</summary>
    public void RefreshFrameRateNote() => OnPropertyChanged(nameof(FrameRateNote));

    private void Save(Action<OverlaySettings> change)
    {
        _settings.Update("Overlay", s =>
        {
            change(s.Overlay);
            s.Overlay.Normalize();
        });
    }

    private void SetColor(string? value, ref string? pending, Action<OverlaySettings, string> apply)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 6 && !text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (AccentPalette.IsValidHex(text))
        {
            pending = null;
            string hex = text.ToUpperInvariant();
            Save(s => apply(s, hex));
        }
        else
        {
            pending = value;
            OnPropertyChanged(string.Empty);
        }
    }

    private void Reset()
    {
        var defaults = new OverlaySettings();
        _textColorText = null;
        _backgroundColorText = null;
        Save(s =>
        {
            s.Items = defaults.Items;
            s.Position = defaults.Position;
            s.Layout = defaults.Layout;
            s.FontSize = defaults.FontSize;
            s.FontWeight = defaults.FontWeight;
            s.TextColor = defaults.TextColor;
            s.TextOpacity = defaults.TextOpacity;
            s.BackgroundColor = defaults.BackgroundColor;
            s.BackgroundOpacity = defaults.BackgroundOpacity;
            s.CornerRadius = defaults.CornerRadius;
            s.EdgeMargin = defaults.EdgeMargin;
            s.ShowLabels = defaults.ShowLabels;
            s.ColorWarnings = defaults.ColorWarnings;
            s.TextShadow = defaults.TextShadow;
            s.OffsetX = defaults.OffsetX;
            s.OffsetY = defaults.OffsetY;
            s.Size = defaults.Size;
            s.Scale = defaults.Scale;
            s.AutoScale = defaults.AutoScale;
            s.GroupSizes = [];
        });
    }

    private void Reload()
    {
        OnPropertyChanged(string.Empty);
        foreach (var item in ItemGroups.SelectMany(g => g.Items))
        {
            item.Refresh();
        }

        foreach (var group in ItemGroups)
        {
            group.Refresh();
        }

        foreach (var position in Positions)
        {
            position.Refresh();
        }

        foreach (var color in TextColors.Concat(BackgroundColors))
        {
            color.Refresh();
        }
    }

    private OverlayColorOption Color(string name, string hex) =>
        new(name, hex, () => Options.TextColor, h => TextColor = h);

    private OverlayColorOption Background(string name, string hex) =>
        new(name, hex, () => Options.BackgroundColor, h => BackgroundColor = h);

    private static string PlaceName(OverlayPosition position) => position switch
    {
        OverlayPosition.TopLeft => "top left",
        OverlayPosition.TopCenter => "top center",
        OverlayPosition.TopRight => "top right",
        OverlayPosition.MiddleLeft => "middle left",
        OverlayPosition.Center => "center",
        OverlayPosition.MiddleRight => "middle right",
        OverlayPosition.BottomLeft => "bottom left",
        OverlayPosition.BottomCenter => "bottom center",
        _ => "bottom right",
    };

    private static string GroupName(OverlayGroup group) => group switch
    {
        OverlayGroup.FrameRate => "Frame rate",
        OverlayGroup.Graphics => "Graphics card",
        OverlayGroup.Processor => "Processor",
        _ => "Memory",
    };
}
