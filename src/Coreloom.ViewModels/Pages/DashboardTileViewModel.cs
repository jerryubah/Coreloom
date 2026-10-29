using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Settings;

namespace Coreloom.ViewModels.Pages;

/// <summary>A card dropped at a new place among the dashboard's visible cards (0 = first).</summary>
public sealed record DashboardTileMove(DashboardTileViewModel Tile, int Index);

/// <summary>A card resized on the dashboard, in grid columns and rows.</summary>
public sealed record DashboardTileResize(DashboardTileViewModel Tile, int Columns, int Rows);

/// <summary>
/// One card or panel on the Overview's dashboard grid: what it shows (<see cref="Content"/>), where it is in the order,
/// and how many columns and rows it covers. Cards are moved by dragging and resized by dragging their corner (or with
/// the commands, which the card's right-click menu uses); every change is saved right away.
/// </summary>
public sealed class DashboardTileViewModel : ObservableObject
{
    private readonly OverviewViewModel _owner;
    private int _order;
    private int _columnSpan;
    private int _rowSpan;

    internal DashboardTileViewModel(OverviewViewModel owner, string id, object content)
    {
        _owner = owner;
        Id = id;
        Content = content;
        Name = DashboardCatalog.NameOf(id);
        var size = DashboardCatalog.SizeOf(id);
        MinColumns = size.MinColumns;
        MinRows = size.MinRows;
        _columnSpan = size.DefaultColumns;
        _rowSpan = size.DefaultRows;

        WiderCommand = new RelayCommand(() => _owner.ResizeTile(this, ColumnSpan + 1, RowSpan), () => ColumnSpan < MaxColumns);
        NarrowerCommand = new RelayCommand(() => _owner.ResizeTile(this, ColumnSpan - 1, RowSpan), () => ColumnSpan > MinColumns);
        TallerCommand = new RelayCommand(() => _owner.ResizeTile(this, ColumnSpan, RowSpan + 1), () => RowSpan < MaxRows);
        ShorterCommand = new RelayCommand(() => _owner.ResizeTile(this, ColumnSpan, RowSpan - 1), () => RowSpan > MinRows);
        ResetSizeCommand = new RelayCommand(() => _owner.ResizeTile(this, size.DefaultColumns, size.DefaultRows), () => !IsDefaultSize);
        HideCommand = new RelayCommand(() => _owner.HideTile(this));
    }

    public string Id { get; }

    /// <summary>The card's name, as in the Customize card.</summary>
    public string Name { get; }

    /// <summary>The summary card or panel shown (<see cref="SummaryCardViewModel"/> or <see cref="DashboardPanelViewModel"/>).</summary>
    public object Content { get; }

    public int MinColumns { get; }

    public int MinRows { get; }

    /// <summary>As tall as its content (System health): see <see cref="DashboardCatalog.FitsContent"/>.</summary>
    public bool FitsContent => DashboardCatalog.FitsContent(Id);

    public int MaxColumns => DashboardCatalog.MaxColumns;

    public int MaxRows => DashboardCatalog.MaxRows;

    /// <summary>Position among the visible cards (0 = first).</summary>
    public int Order
    {
        get => _order;
        internal set => SetProperty(ref _order, value);
    }

    public int ColumnSpan
    {
        get => _columnSpan;
        internal set
        {
            if (SetProperty(ref _columnSpan, value))
            {
                SizeChanged();
            }
        }
    }

    public int RowSpan
    {
        get => _rowSpan;
        internal set
        {
            if (SetProperty(ref _rowSpan, value))
            {
                SizeChanged();
            }
        }
    }

    public bool IsDefaultSize
    {
        get
        {
            var size = DashboardCatalog.SizeOf(Id);
            return ColumnSpan == size.DefaultColumns && RowSpan == size.DefaultRows;
        }
    }

    public IRelayCommand WiderCommand { get; }

    public IRelayCommand NarrowerCommand { get; }

    public IRelayCommand TallerCommand { get; }

    public IRelayCommand ShorterCommand { get; }

    public IRelayCommand ResetSizeCommand { get; }

    public IRelayCommand HideCommand { get; }

    private void SizeChanged()
    {
        OnPropertyChanged(nameof(IsDefaultSize));
        WiderCommand.NotifyCanExecuteChanged();
        NarrowerCommand.NotifyCanExecuteChanged();
        TallerCommand.NotifyCanExecuteChanged();
        ShorterCommand.NotifyCanExecuteChanged();
        ResetSizeCommand.NotifyCanExecuteChanged();
    }
}
