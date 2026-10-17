using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>An entry in the log viewer.</summary>
public sealed class LogLineViewModel(LogRecord record)
{
    public LogRecord Record { get; } = record;

    /// <summary>"Oct 16 22:41:05.123".</summary>
    public string TimeText { get; } = record.Time.ToString("MMM d  HH:mm:ss.fff", CultureInfo.CurrentCulture);

    public string LevelText { get; } = record.Level switch
    {
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warning => "Warning",
        _ => "Error",
    };

    /// <summary>"Debug", "Info", "Warning" or "Error", for the level's color.</summary>
    public string LevelKey => LevelText;

    public string Category => Record.Category;

    public string Message => Record.Message;

    public string? Details => Record.Details;

    public bool HasDetails => Record.Details is not null;
}

/// <summary>
/// The log viewer (Settings › Logs): every entry in Coreloom's log files, newest first, with search, a level filter,
/// a category filter and timestamps, and a button to open the log folder.
/// </summary>
public sealed class LogViewerViewModel : ObservableObject
{
    /// <summary>The most entries shown at once (the newest that match); search to narrow down.</summary>
    public const int MaxShown = 1500;

    public const string AllCategories = "All categories";

    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellService _shell;
    private IReadOnlyList<LogRecord> _records = [];
    private string _search = string.Empty;
    private LogLevel _minimumLevel = LogLevel.Debug;
    private string _category = AllCategories;
    private bool _isLoading;
    private string _summary = "Not loaded yet.";
    private LogLineViewModel? _selected;

    public LogViewerViewModel(IDiagnosticsService diagnostics, IShellService shell)
    {
        _diagnostics = diagnostics;
        _shell = shell;
        LevelOptions =
        [
            new(LogLevel.Debug, "All levels"),
            new(LogLevel.Info, "Info and above"),
            new(LogLevel.Warning, "Warnings and errors"),
            new(LogLevel.Error, "Errors only"),
        ];
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        OpenFolderCommand = new RelayCommand(() => _shell.OpenFolder(_diagnostics.LogDirectory));
        ClearFiltersCommand = new RelayCommand(() => Apply(null, null), () => HasFilters);
        CopyCommand = new RelayCommand(Copy, () => Lines.Count > 0);
    }

    public IReadOnlyList<Option<LogLevel>> LevelOptions { get; }

    public ObservableCollection<string> Categories { get; } = [AllCategories];

    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public IRelayCommand ClearFiltersCommand { get; }

    /// <summary>Copies the selected entry, or every entry shown when none is selected.</summary>
    public IRelayCommand CopyCommand { get; }

    public string LogDirectory => _diagnostics.LogDirectory;

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                Filter();
            }
        }
    }

    public LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set
        {
            if (SetProperty(ref _minimumLevel, value))
            {
                Filter();
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, string.IsNullOrEmpty(value) ? AllCategories : value))
            {
                Filter();
            }
        }
    }

    public LogLineViewModel? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Showing 1,500 of 12,345 entries · Oct 14 21:02 to Oct 16 22:41".</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool HasFilters => _search.Length > 0 || _minimumLevel != LogLevel.Debug || _category != AllCategories;

    public bool IsEmpty => Lines.Count == 0 && !IsLoading;

    /// <summary>Sets the filters (as when opened from another page, e.g. Smart Optimize's memory results).</summary>
    public void Apply(string? category, string? search)
    {
        _category = string.IsNullOrEmpty(category) ? AllCategories : category;
        _search = search ?? string.Empty;
        _minimumLevel = LogLevel.Debug;
        if (!Categories.Contains(_category))
        {
            Categories.Add(_category);
        }

        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Search));
        OnPropertyChanged(nameof(MinimumLevel));
        Filter();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var diagnostics = _diagnostics;
            _records = await Task.Run(() => diagnostics.ReadLog());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _records = [];
        }
        finally
        {
            IsLoading = false;
        }

        var categories = _records.Select(r => r.Category).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (_category != AllCategories && !categories.Contains(_category, StringComparer.OrdinalIgnoreCase))
        {
            categories.Add(_category);
        }

        string keep = _category;
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (string c in categories)
        {
            Categories.Add(c);
        }

        _category = keep;
        OnPropertyChanged(nameof(Category));
        Filter();
    }

    private void Filter()
    {
        string? category = _category == AllCategories ? null : _category;
        var shown = new List<LogLineViewModel>();
        int matches = 0;
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var record = _records[i];
            if (!LogFile.Matches(record, _search, _minimumLevel, category))
            {
                continue;
            }

            matches++;
            if (shown.Count < MaxShown)
            {
                shown.Add(new LogLineViewModel(record));
            }
        }

        Lines.Clear();
        foreach (var line in shown)
        {
            Lines.Add(line);
        }

        Selected = null;
        Summary = _records.Count == 0
            ? "No log entries yet."
            : (matches > shown.Count
                ? $"Showing the newest {shown.Count.ToString("N0", CultureInfo.CurrentCulture)} of {matches.ToString("N0", CultureInfo.CurrentCulture)} matching entries"
                : $"{matches.ToString("N0", CultureInfo.CurrentCulture)} of {_records.Count.ToString("N0", CultureInfo.CurrentCulture)} entries")
              + $" · {_records[0].Time.ToString("MMM d HH:mm", CultureInfo.CurrentCulture)} to {_records[^1].Time.ToString("MMM d HH:mm", CultureInfo.CurrentCulture)}";
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(IsEmpty));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }

    private void Copy()
    {
        IEnumerable<LogLineViewModel> lines = Selected is { } one ? [one] : Lines;
        string text = string.Join(Environment.NewLine, lines.Select(l =>
            $"{l.Record.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {l.LevelText} [{l.Category}] {l.Record.Text}"));
        _shell.CopyToClipboard(text);
    }
}
