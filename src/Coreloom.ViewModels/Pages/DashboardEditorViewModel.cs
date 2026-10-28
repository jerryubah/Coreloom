using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>Dashboard card visibility/order editor row.</summary>
public sealed class DashboardCardSettingViewModel : ObservableObject
{
    private readonly DashboardEditorViewModel _owner;
    private bool _isVisible;

    public DashboardCardSettingViewModel(DashboardEditorViewModel owner, string id, bool visible)
    {
        _owner = owner;
        Id = id;
        _isVisible = visible;
        Name = DashboardCatalog.NameOf(id);
        Kind = DashboardCatalog.IsSummary(id) ? "Summary card" : "Panel";
        MoveUpCommand = new RelayCommand(() => _owner.MoveCard(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.MoveCard(this, +1));
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _owner.SaveCards();
            }
        }
    }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }
}

/// <summary>
/// The Overview's Customize card: whether the header shows the PC picture and its lighting color, which cards and panels
/// the dashboard shows and in what order, and whether cards show min/max/avg. Every change is saved and applied right
/// away, so the dashboard behind the card updates as you edit. (Cards are also moved and resized on the dashboard itself.)
/// </summary>
public sealed class DashboardEditorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private bool _isOpen;

    public DashboardEditorViewModel(SettingsService settings, SystemSummaryViewModel specs)
    {
        _settings = settings;
        Specs = specs;
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        ResetLayoutCommand = new RelayCommand(ResetLayout);
        AllowDesktopPictureCommand = new RelayCommand(AllowDesktopPicture);
        LoadCards();
    }

    /// <summary>The header's view model, which holds the lighting colors.</summary>
    public SystemSummaryViewModel Specs { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public ObservableCollection<DashboardCardSettingViewModel> Cards { get; } = [];

    /// <summary>The picture of the PC on the left of the header; on by default.</summary>
    public bool ShowPcPicture
    {
        get => _settings.Current.Dashboard.ShowPcPicture;
        set
        {
            if (value != _settings.Current.Dashboard.ShowPcPicture)
            {
                _settings.Update("Dashboard", s => s.Dashboard.ShowPcPicture = value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// macOS: the MacBook in the header shows the Mac's own desktop picture. Turning it on here also counts as choosing it
    /// (a picture in a folder macOS guards is then read; see <see cref="DashboardSettings.DesktopPictureChosen"/>).
    /// </summary>
    public bool ShowDesktopPicture
    {
        get => _settings.Current.Dashboard.ShowDesktopPicture;
        set
        {
            if (value != _settings.Current.Dashboard.ShowDesktopPicture)
            {
                _settings.Update("Dashboard", s =>
                {
                    s.Dashboard.ShowDesktopPicture = value;
                    s.Dashboard.DesktopPictureChosen = value;
                });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// macOS: shows the desktop picture even though it's kept in a folder macOS guards (the person chose it; macOS may then
    /// ask them once).
    /// </summary>
    public IRelayCommand AllowDesktopPictureCommand { get; }

    /// <summary>Minimum, maximum and average on the summary cards.</summary>
    public bool ShowStatistics
    {
        get => _settings.Current.Dashboard.ShowStatistics;
        set
        {
            if (value != _settings.Current.Dashboard.ShowStatistics)
            {
                _settings.Update("Dashboard", s => s.Dashboard.ShowStatistics = value);
                OnPropertyChanged();
            }
        }
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand ResetLayoutCommand { get; }

    /// <summary>Re-reads everything from settings (after settings were replaced, e.g. imported).</summary>
    public void Reload()
    {
        LoadCards();
        OnPropertyChanged(nameof(ShowStatistics));
        OnPropertyChanged(nameof(ShowPcPicture));
        OnPropertyChanged(nameof(ShowDesktopPicture));
    }

    private void AllowDesktopPicture()
    {
        _settings.Update("Dashboard", s =>
        {
            s.Dashboard.ShowDesktopPicture = true;
            s.Dashboard.DesktopPictureChosen = true;
        });
        OnPropertyChanged(nameof(ShowDesktopPicture));
    }

    internal void MoveCard(DashboardCardSettingViewModel card, int delta)
    {
        int index = Cards.IndexOf(card);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Cards.Count)
        {
            return;
        }

        Cards.Move(index, target);
        SaveCards();
    }

    /// <summary>Saves visibility and order; sizes (set on the dashboard) stay as they are.</summary>
    internal void SaveCards() =>
        _settings.Update("Dashboard", s =>
        {
            var sizes = s.Dashboard.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            s.Dashboard.Cards = Cards.Select(c => new DashboardCardSetting
            {
                Id = c.Id,
                Visible = c.IsVisible,
                Columns = sizes.GetValueOrDefault(c.Id)?.Columns,
                Rows = sizes.GetValueOrDefault(c.Id)?.Rows,
            }).ToList();
        });

    private void Open()
    {
        Reload();
        IsOpen = true;
    }

    private void LoadCards()
    {
        Cards.Clear();
        foreach (var card in _settings.Current.Dashboard.Cards)
        {
            Cards.Add(new DashboardCardSettingViewModel(this, card.Id, card.Visible));
        }
    }

    private void ResetLayout()
    {
        _settings.Update("Dashboard", s => s.Dashboard.Cards = DashboardCatalog.Defaults());
        LoadCards();
    }
}
