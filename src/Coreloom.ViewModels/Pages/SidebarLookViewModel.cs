using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// Settings › Appearance › Sidebar (since 1.9.2): what the sidebar is made of (a solid colour, liquid glass, frosted
/// glass or a translucent tint over the background), how strong its drop shadow is and how much its glass blurs what
/// is behind it. Every change applies at once and is saved.
/// </summary>
public sealed class SidebarLookViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SidebarLookViewModel(SettingsService settings)
    {
        _settings = settings;
        ResetCommand = new RelayCommand(ResetStrengths);
    }

    private SidebarSettings Current => _settings.Current.Appearance.Sidebar;

    public IReadOnlyList<Option<SidebarSurface>> Surfaces { get; } =
    [
        new(SidebarSurface.Solid, "Solid color"),
        new(SidebarSurface.LiquidGlass, "Liquid glass"),
        new(SidebarSurface.FrostedGlass, "Frosted glass"),
        new(SidebarSurface.Translucent, "Translucent blend"),
    ];

    public SidebarSurface Surface
    {
        get => Current.Surface;
        set
        {
            if (Current.Surface != value && Enum.IsDefined(value))
            {
                Change(s => s.Surface = value);
            }
        }
    }

    /// <summary>One line on the chosen surface, under the choice.</summary>
    public string SurfaceText => Current.Surface switch
    {
        SidebarSurface.Solid => "One solid color, the cards' own: nothing of the background shows through.",
        SidebarSurface.LiquidGlass => "Clear glass with a bright rim and a highlight that catches the light; the background shows through, lightly blurred.",
        SidebarSurface.Translucent => "A see-through tint with no blur that blends with the background: its colors or picture show through, sharp.",
        _ => "Frosted glass: the background blurred behind a dark tint, like the cards (the standard sidebar).",
    };

    /// <summary>The drop shadow (0–100 %; 50 is the standard shadow).</summary>
    public double Shadow
    {
        get => Current.Shadow;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, 0, 100));
            if (v != Current.Shadow)
            {
                Change(s => s.Shadow = v);
            }
        }
    }

    public string ShadowText => Current.Shadow switch
    {
        0 => "None",
        SidebarSettings.Standard => "Standard",
        _ => $"{Current.Shadow} %",
    };

    /// <summary>How much the glass blurs what is behind it (0–100 %; 50 is as much as the cards).</summary>
    public double Blur
    {
        get => Current.Blur;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, 0, 100));
            if (v != Current.Blur)
            {
                Change(s => s.Blur = v);
            }
        }
    }

    public string BlurText => !Current.Blurs ? "No blur" : Current.Blur switch
    {
        0 => "Lightest",
        SidebarSettings.Standard => "Standard",
        _ => $"{Current.Blur} %",
    };

    /// <summary>The blur only applies to glass (a solid or translucent sidebar has nothing to blur).</summary>
    public bool CanBlur => Current.Blurs;

    /// <summary>The shadow and blur back to the standard strength (the surface stays).</summary>
    public bool IsStandard => Current.Shadow == SidebarSettings.Standard && Current.Blur == SidebarSettings.Standard;

    public IRelayCommand ResetCommand { get; }

    public void ResetStrengths() => Change(s =>
    {
        s.Shadow = SidebarSettings.Standard;
        s.Blur = SidebarSettings.Standard;
    });

    private void Change(Action<SidebarSettings> mutate)
    {
        _settings.Update("Appearance", s => mutate(s.Appearance.Sidebar));
        Refresh();
    }

    /// <summary>Every property again (after a change here or elsewhere).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
