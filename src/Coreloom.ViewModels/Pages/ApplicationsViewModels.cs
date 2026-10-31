using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Apps;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Optimize;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>Which apps the Applications page lists.</summary>
public enum AppFilter
{
    All,
    Desktop,
    Store,
    Startup,
}

/// <summary>How the Applications page orders its list.</summary>
public enum AppSort
{
    Size,
    Name,
    Installed,
    Publisher,
}

/// <summary>
/// One line of the Applications page: an installed app (with its size, and its startup switch when it starts with
/// Windows), or a startup entry that belongs to no installed app. Uninstalling asks first, then runs the app's own
/// uninstaller.
/// </summary>
public sealed class AppRowViewModel : ObservableObject
{
    private readonly ApplicationsViewModel _page;
    private InstalledApp? _app;
    private StartupApp? _startup;
    private string? _icon;
    private bool _isConfirming;
    private bool _isBusy;
    private bool _isRemoved;
    private string? _message;

    public AppRowViewModel(ApplicationsViewModel page, InstalledApp? app, StartupApp? startup)
    {
        _page = page;
        _app = app;
        _startup = startup;
        UninstallCommand = new RelayCommand(() => IsConfirming = true, () => CanUninstall && !_isBusy);
        ConfirmUninstallCommand = new AsyncRelayCommand(UninstallAsync, () => CanUninstall && !_isBusy);
        CancelUninstallCommand = new RelayCommand(() => IsConfirming = false);
        OpenFolderCommand = new RelayCommand(() => _page.OpenFolder(_app!.InstallLocation!), () => HasFolder);
    }

    public InstalledApp? App => _app;

    public StartupApp? Startup => _startup;

    public string Id => _app?.Id ?? "startup:" + _startup!.Id;

    public string Name => _app?.Name ?? _startup!.Name;

    /// <summary>The picture's stand-in: the first letters of the first two words ("Visual Studio" → VS; a number counts only first).</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
            if (words.Count == 0)
            {
                return "?";
            }

            string first = char.ToUpperInvariant(words[0][0]).ToString();
            return words.Count > 1 && char.IsLetter(words[1][0]) ? first + char.ToUpperInvariant(words[1][0]) : first;
        }
    }

    public string Publisher => _app?.Publisher ?? _startup?.Publisher ?? string.Empty;

    /// <summary>"Publisher · 1.2.3 · Installed Oct 3, 2026" (what's known of it).</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher))
            {
                parts.Add(Publisher);
            }

            if (_app?.Version is { } version)
            {
                parts.Add(version);
            }

            if (_app?.Installed is { } date)
            {
                parts.Add("Installed " + date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
            }

            if (_app is null && _startup is not null)
            {
                parts.Add(_startup.ScopeText);
            }

            return string.Join(" · ", parts);
        }
    }

    public string KindText => _app is null ? "Startup only" : _app.Kind == AppKind.Store ? "Microsoft Store" : "Desktop app";

    public bool IsStore => _app?.Kind == AppKind.Store;

    public long Size => _app?.SizeBytes ?? 0;

    public string SizeText => _app?.SizeBytes is { } bytes ? _page.FormatBytes(bytes) : _app is null ? string.Empty : "—";

    /// <summary>The size against the largest app's (0–1), for the bar under the size.</summary>
    public double SizeShare => _page.LargestSize > 0 ? Math.Clamp(Size / (double)_page.LargestSize, 0, 1) : 0;

    public string? Icon
    {
        get => _icon;
        internal set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public bool HasStartup => _startup is not null;

    /// <summary>The startup switch: whether it starts with Windows (the switch Task Manager has).</summary>
    public bool StartsWithWindows
    {
        get => _startup?.Enabled ?? false;
        set
        {
            if (_startup is null || value == _startup.Enabled)
            {
                return;
            }

            if (_page.SetStartup(_startup, value))
            {
                _startup = _startup with { Enabled = value };
                Message = value ? "Starts with Windows again." : "Won't start with Windows any more.";
            }
            else
            {
                Message = "Windows didn't accept the change.";
            }

            OnPropertyChanged();
            _page.StartupChanged();
        }
    }

    public bool CanUninstall => _app?.CanUninstall == true && !_isRemoved;

    public bool HasFolder => _app?.InstallLocation is { Length: > 0 } && !_isRemoved;

    public bool IsConfirming
    {
        get => _isConfirming;
        set => SetProperty(ref _isConfirming, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UninstallCommand.NotifyCanExecuteChanged();
                ConfirmUninstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRemoved
    {
        get => _isRemoved;
        private set => SetProperty(ref _isRemoved, value);
    }

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

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>What uninstalling does, in the confirmation.</summary>
    public string ConfirmText => IsStore
        ? $"Uninstall {Name}? Windows removes the app and its data for your account."
        : $"Uninstall {Name}? Coreloom opens its own uninstaller; follow it to the end.";

    public IRelayCommand UninstallCommand { get; }

    public IAsyncRelayCommand ConfirmUninstallCommand { get; }

    public IRelayCommand CancelUninstallCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || Publisher.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    internal void SetSize(long bytes)
    {
        if (_app is not null)
        {
            _app = _app with { SizeBytes = bytes };
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    internal void ShareChanged() => OnPropertyChanged(nameof(SizeShare));

    private async Task UninstallAsync()
    {
        if (_app is null)
        {
            return;
        }

        IsConfirming = false;
        IsBusy = true;
        Message = IsStore ? "Removing…" : "Waiting for the uninstaller…";
        AppUninstallResult result;
        try
        {
            result = await _page.UninstallAsync(_app);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            result = new AppUninstallResult(false, "Couldn't start its uninstaller: " + e.Message);
        }

        IsBusy = false;
        Message = result.Message;
        if (result.Removed)
        {
            IsRemoved = true;
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(HasFolder));
            _page.Removed(this);
        }

        UninstallCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// The Applications page (Windows, since 1.8.6): every installed app (desktop and Microsoft Store) with its size,
/// publisher and install date, the apps that start with Windows with their switches, filters, sorting and search, and
/// uninstalling with the app's own uninstaller after asking.
/// </summary>
public sealed class ApplicationsViewModel : PageViewModel
{
    private readonly IAppService _service;
    private readonly IOptimizeService _startupService;
    private readonly List<AppRowViewModel> _all = [];
    private bool _isLoaded;
    private bool _isLoading;
    private bool _isMeasuring;
    private string _search = string.Empty;
    private string _searchText = string.Empty;
    private AppFilter _filter = AppFilter.All;
    private AppSort _sort = AppSort.Size;

    public ApplicationsViewModel(PageContext context)
        : base("applications", "Applications", "Icon.Apps", context)
    {
        _service = context.Tools.Apps;
        _startupService = context.Tools.Optimizer;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => IsSupported && !_isLoading);
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        SetFilterCommand = new RelayCommand<AppFilter>(f => Filter = f);
        Subtitle = "Installed apps, their sizes, and what starts with Windows";
        FilterOptions =
        [
            new(AppFilter.All, "All apps"),
            new(AppFilter.Desktop, "Desktop"),
            new(AppFilter.Store, "Microsoft Store"),
            new(AppFilter.Startup, "Startup"),
        ];
        SortOptions =
        [
            new(AppSort.Size, "Largest first"),
            new(AppSort.Name, "Name"),
            new(AppSort.Installed, "Recently installed"),
            new(AppSort.Publisher, "Publisher"),
        ];
    }

    public bool IsSupported => _service.IsSupported;

    public string UnsupportedText => "Installed apps are read from Windows' installed-apps list, which isn't available here.";

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
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

    /// <summary>Folders of apps that don't say their size are being measured.</summary>
    public bool IsMeasuring
    {
        get => _isMeasuring;
        private set => SetProperty(ref _isMeasuring, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IRelayCommand<AppFilter> SetFilterCommand { get; }

    /// <summary>The apps that pass the filter and search, in the chosen order.</summary>
    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    public string Search
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _searchText, value))
            {
                _search = value.Trim();
                Apply();
                OnPropertyChanged(nameof(SearchSuggestions));
            }
        }
    }

    public IReadOnlyList<SearchSuggestion> SearchSuggestions =>
        Infrastructure.SearchSuggestions.Rank(_all, _search, a => a.Name, a => a.Matches(_search),
            a => new SearchSuggestion(a.Name, string.Join(" · ", new[] { a.KindText, a.SizeText }.Where(s => s.Length > 0)), "Icon.Apps"));

    public AppFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Apply();
                OnPropertyChanged(nameof(IsAll));
                OnPropertyChanged(nameof(IsDesktop));
                OnPropertyChanged(nameof(IsStoreFilter));
                OnPropertyChanged(nameof(IsStartupFilter));
            }
        }
    }

    public bool IsAll => _filter == AppFilter.All;

    public bool IsDesktop => _filter == AppFilter.Desktop;

    public bool IsStoreFilter => _filter == AppFilter.Store;

    public bool IsStartupFilter => _filter == AppFilter.Startup;

    public IReadOnlyList<Option<AppFilter>> FilterOptions { get; }

    public IReadOnlyList<Option<AppSort>> SortOptions { get; }

    public AppSort Sort
    {
        get => _sort;
        set
        {
            if (SetProperty(ref _sort, value))
            {
                Apply();
            }
        }
    }

    public int AppCount => _all.Count(a => a.App is not null && !a.IsRemoved);

    public int DesktopCount => _all.Count(a => a.App?.Kind == AppKind.Desktop && !a.IsRemoved);

    public int StoreCount => _all.Count(a => a.App?.Kind == AppKind.Store && !a.IsRemoved);

    /// <summary>Startup entries that are on (start with Windows now).</summary>
    public int StartupOnCount => _all.Count(a => a.Startup?.Enabled == true);

    public int StartupCount => _all.Count(a => a.HasStartup);

    public string TotalSizeText => FormatBytes(_all.Where(a => !a.IsRemoved).Sum(a => a.Size));

    public string StartupText => $"{StartupOnCount.ToString(CultureInfo.CurrentCulture)} of {StartupCount.ToString(CultureInfo.CurrentCulture)}";

    internal long LargestSize { get; private set; }

    public string EmptyText => _isLoading && _all.Count == 0 ? "Reading your apps…"
        : _search.Length > 0 ? "No apps match your search."
        : _filter == AppFilter.Startup ? "Nothing starts with Windows." : "No apps to show.";

    public string CountText => _filter == AppFilter.Startup
        ? $"{Apps.Count.ToString(CultureInfo.CurrentCulture)} start with Windows · {StartupOnCount.ToString(CultureInfo.CurrentCulture)} on"
        : $"{InstalledApps.Apps(Apps.Count)} · {FormatBytes(Apps.Sum(a => a.Size))}";

    protected override void OnUpdate(TelemetryFrame frame)
    {
    }

    protected override void OnActivated()
    {
        if (!_isLoaded && !_isLoading && IsSupported)
        {
            _ = LoadAsync();
        }
    }

    /// <summary>Reads the installed apps and startup entries, then measures the apps that don't say their size.</summary>
    public async Task LoadAsync()
    {
        if (_isLoading || !IsSupported)
        {
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(EmptyText));
        IReadOnlyList<InstalledApp> apps;
        IReadOnlyList<StartupApp> startup;
        try
        {
            apps = await _service.InstalledAsync(CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log.Warn("Applications", "Reading the installed apps failed.", e);
            apps = [];
        }

        try
        {
            startup = _startupService.StartupApps();
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log.Warn("Applications", "Reading the startup apps failed.", e);
            startup = [];
        }

        _all.Clear();
        var byApp = new Dictionary<string, StartupApp>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<StartupApp>();
        foreach (var entry in startup)
        {
            if (InstalledApps.ForStartup(apps, entry) is { } owner && !byApp.ContainsKey(owner.Id))
            {
                byApp[owner.Id] = entry;
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        foreach (var app in apps)
        {
            _all.Add(new AppRowViewModel(this, app, byApp.GetValueOrDefault(app.Id)) { Icon = SafeIcon(app) });
        }

        foreach (var entry in unmatched)
        {
            _all.Add(new AppRowViewModel(this, null, entry));
        }

        IsLoading = false;
        IsLoaded = true;
        Totals();
        Apply();
        Log.Info("Applications", $"Listed {InstalledApps.Apps(apps.Count)} ({apps.Count(a => a.Kind == AppKind.Store).ToString(CultureInfo.InvariantCulture)} from the Store) and {startup.Count.ToString(CultureInfo.InvariantCulture)} startup entries.");
        await MeasureAsync();
    }

    private async Task MeasureAsync()
    {
        var unknown = _all.Where(a => a.App is { SizeBytes: null, InstallLocation.Length: > 0 }).Select(a => a.App!).ToList();
        if (unknown.Count == 0)
        {
            return;
        }

        IsMeasuring = true;
        try
        {
            var sizes = await _service.MeasureAsync(unknown, CancellationToken.None);
            foreach (var row in _all)
            {
                if (row.App is { } app && sizes.TryGetValue(app.Id, out long bytes))
                {
                    row.SetSize(bytes);
                }
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log.Warn("Applications", "Measuring app folders failed.", e);
        }
        finally
        {
            IsMeasuring = false;
        }

        Totals();
        Apply();
    }

    internal string FormatBytes(long bytes) => Formatter.Bytes(bytes).ToString();

    internal bool SetStartup(StartupApp app, bool enabled) => _startupService.SetStartupEnabled(app, enabled);

    internal void StartupChanged()
    {
        OnPropertyChanged(nameof(StartupOnCount));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(CountText));
    }

    internal Task<AppUninstallResult> UninstallAsync(InstalledApp app)
    {
        Log.Info("Applications", $"Uninstalling {app.Name} ({app.Kind}).");
        return _service.UninstallAsync(app, CancellationToken.None);
    }

    internal void Removed(AppRowViewModel row)
    {
        Log.Info("Applications", $"{row.Name} was uninstalled.");
        Totals();
        OnPropertyChanged(nameof(CountText));
    }

    internal void OpenFolder(string path) => _service.OpenFolder(path);

    private string? SafeIcon(InstalledApp app)
    {
        try
        {
            return _service.IconFor(app);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return null;
        }
    }

    private void Apply()
    {
        IEnumerable<AppRowViewModel> rows = _filter switch
        {
            AppFilter.Desktop => _all.Where(a => a.App?.Kind == AppKind.Desktop),
            AppFilter.Store => _all.Where(a => a.App?.Kind == AppKind.Store),
            AppFilter.Startup => _all.Where(a => a.HasStartup),
            _ => _all.Where(a => a.App is not null),
        };
        if (_search.Length > 0)
        {
            rows = rows.Where(a => a.Matches(_search));
        }

        rows = _sort switch
        {
            AppSort.Name => rows.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            AppSort.Installed => rows.OrderByDescending(a => a.App?.Installed ?? DateTime.MinValue).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            AppSort.Publisher => rows.OrderBy(a => string.IsNullOrEmpty(a.Publisher) ? 1 : 0).ThenBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderByDescending(a => a.Size).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        if (_filter == AppFilter.Startup)
        {
            rows = rows.OrderByDescending(a => a.Startup?.Enabled == true); // on first, the chosen order within
        }

        var list = rows.ToList();
        if (Apps.Count != list.Count || !Apps.SequenceEqual(list))
        {
            Apps.Clear();
            foreach (var row in list)
            {
                Apps.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CountText));
    }

    private void Totals()
    {
        LargestSize = _all.Count == 0 ? 0 : _all.Max(a => a.Size);
        foreach (var row in _all)
        {
            row.ShareChanged();
        }

        OnPropertyChanged(nameof(AppCount));
        OnPropertyChanged(nameof(DesktopCount));
        OnPropertyChanged(nameof(StoreCount));
        OnPropertyChanged(nameof(StartupOnCount));
        OnPropertyChanged(nameof(StartupCount));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(TotalSizeText));
    }

    private static bool IsExpected(Exception e) =>
        e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException or ArgumentException;
}
