using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Drivers;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>One line of the installed drivers: a device (or several alike), its driver's provider, version and date.</summary>
public sealed class DriverRowViewModel : ObservableObject
{
    public DriverRowViewModel(DriverGroup group)
    {
        Group = group;
    }

    public DriverGroup Group { get; }

    public string Name => Group.Name;

    public string ProviderText => string.IsNullOrWhiteSpace(Group.Provider) ? "Unknown provider" : Group.Provider!;

    /// <summary>NVIDIA's own number for its graphics drivers ("581.57"), Windows' version for the rest.</summary>
    public string VersionText => Group.Category == DriverCategory.Graphics && (Group.Provider?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false)
        && DriverCatalog.NvidiaVersion(Group.Version) is { } nvidia
            ? $"{nvidia} ({Group.Version})"
            : Group.Version ?? "—";

    public string DateText => Group.Date is { } date ? date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : string.Empty;

    public string CountText => Group.Count > 1 ? $"×{Group.Count.ToString(CultureInfo.CurrentCulture)}" : string.Empty;

    public bool HasCount => Group.Count > 1;

    public bool HasProblem => Group.HasProblem;

    public bool IsMicrosoft => Group.IsMicrosoft;

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || ProviderText.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || DriverCatalog.Label(Group.Category).Contains(search, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>A group of installed drivers under its heading (Graphics, Audio, Network…).</summary>
public sealed class DriverCategoryViewModel
{
    public DriverCategoryViewModel(DriverCategory category, IReadOnlyList<DriverRowViewModel> rows)
    {
        Category = category;
        Rows = rows;
    }

    public DriverCategory Category { get; }

    public string Label => DriverCatalog.Label(Category);

    public string IconKey => DriverCatalog.IconKey(Category);

    public IReadOnlyList<DriverRowViewModel> Rows { get; }

    public string CountText => Rows.Sum(r => r.Group.Count).ToString(CultureInfo.CurrentCulture);
}

/// <summary>An update on offer: what it's for, from which version to which, its source and size, picked or not, and how installing it went.</summary>
public sealed class DriverUpdateViewModel : ObservableObject
{
    private readonly DriversViewModel _page;
    private bool _isSelected = true;
    private string _statusText;
    private bool _isWorking;
    private bool _isDone;
    private bool _failed;

    public DriverUpdateViewModel(DriversViewModel page, DriverUpdate update)
    {
        _page = page;
        Update = update;
        _statusText = "Ready to install";
        OpenNotesCommand = new RelayCommand(() => _page.OpenUrl(Update.DetailsUrl!), () => Update.DetailsUrl is not null);
    }

    public DriverUpdate Update { get; }

    public string Name => Update.Name;

    public string IconKey => DriverCatalog.IconKey(Update.Category);

    public string CategoryText => Update.Source == DriverSource.DirectX ? "DirectX" : DriverCatalog.Label(Update.Category);

    public string SourceText => Update.Source switch
    {
        DriverSource.Nvidia => "From NVIDIA",
        DriverSource.DirectX => "From Microsoft",
        _ => "From Windows Update",
    };

    public string VersionText => Update.CurrentVersion is { } current && Update.NewVersion is { } next
        ? $"{current}  →  {next}"
        : Update.NewVersion ?? string.Empty;

    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Update.Provider))
            {
                parts.Add(Update.Provider!);
            }

            if (Update.Date is { } date)
            {
                parts.Add(date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
            }

            if (Update.SizeText is { } size)
            {
                parts.Add(size);
            }
            else if (Update.SizeBytes is { } bytes)
            {
                parts.Add(_page.FormatBytes(bytes));
            }

            return string.Join(" · ", parts);
        }
    }

    public bool IsNvidia => Update.Source == DriverSource.Nvidia;

    public bool HasNotes => Update.DetailsUrl is not null;

    public IRelayCommand OpenNotesCommand { get; }

    /// <summary>Picked for "Update selected" (every update starts picked).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _page.SelectionChanged();
            }
        }
    }

    public bool CanSelect => !_isDone && !_isWorking;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
            {
                OnPropertyChanged(nameof(CanSelect));
            }
        }
    }

    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (SetProperty(ref _isDone, value))
            {
                OnPropertyChanged(nameof(CanSelect));
            }
        }
    }

    public bool Failed
    {
        get => _failed;
        private set => SetProperty(ref _failed, value);
    }

    /// <summary>"Tone.Cool" once installed, "Tone.Hot" when it failed, else neutral.</summary>
    public string StatusColorKey => _failed ? ColorKeys.ToneHot : _isDone ? ColorKeys.ToneCool : ColorKeys.Neutral;

    internal void Working(string text)
    {
        IsWorking = true;
        StatusText = text;
    }

    internal void Finished(DriverInstallResult result)
    {
        IsWorking = false;
        Failed = !result.Success;
        IsDone = result.Success;
        StatusText = result.Message;
        if (result.Success)
        {
            _isSelected = false;
            OnPropertyChanged(nameof(IsSelected));
        }

        OnPropertyChanged(nameof(StatusColorKey));
    }
}

/// <summary>
/// The Drivers page (Windows, since 1.8.6): every device's driver grouped and labeled (Graphics, Audio, Network…), and
/// updates from Windows Update, NVIDIA (for an NVIDIA graphics card, with the NVIDIA app a click away) and Microsoft's
/// DirectX runtime for older games. The user picks which updates to install, or all of them; nothing is installed
/// otherwise.
/// </summary>
public sealed class DriversViewModel : PageViewModel
{
    private readonly IDriverService _service;
    private IReadOnlyList<InstalledDriver> _installed = [];
    private IReadOnlyList<DriverGroup> _groups = [];
    private bool _isLoaded;
    private bool _isLoading;
    private bool _isChecking;
    private bool _isInstalling;
    private bool _hasChecked;
    private bool _showWindowsDrivers;
    private string _search = string.Empty;
    private string _searchText = string.Empty;
    private string? _checkProgress;
    private string? _resultText;
    private DateTime? _lastChecked;
    private IReadOnlyList<string> _problems = [];

    public DriversViewModel(PageContext context)
        : base("drivers", "Drivers", "Icon.Driver", context)
    {
        _service = context.Tools.Drivers;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => IsSupported && !_isChecking && !_isInstalling && _isLoaded);
        InstallSelectedCommand = new AsyncRelayCommand(InstallSelectedAsync, () => !_isInstalling && !_isChecking && SelectedCount > 0);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => IsSupported && !_isLoading && !_isInstalling);
        OpenNvidiaAppCommand = new RelayCommand(OpenNvidiaApp);
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        Subtitle = "Every device's driver, and updates when there are any";
    }

    /// <summary>For tests: the clock "last checked" uses.</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    public bool IsSupported => _service.IsSupported;

    public string UnsupportedText => "Drivers are read from Windows' device tree and updated through Windows Update, which aren't available here.";

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                Commands();
            }
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                Commands();
            }
        }
    }

    /// <summary>Checking or installing: the banner's dial spins.</summary>
    public bool IsBusy => _isChecking || _isInstalling;

    public IAsyncRelayCommand CheckCommand { get; }

    public IAsyncRelayCommand InstallSelectedCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenNvidiaAppCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    // ---- Installed drivers ------------------------------------------------------------------------

    /// <summary>The installed drivers that match the search, under their headings.</summary>
    public ObservableCollection<DriverCategoryViewModel> Categories { get; } = [];

    public bool HasCategories => Categories.Count > 0;

    /// <summary>Also list the drivers Windows itself provides (hidden at first: they come with Windows' own updates).</summary>
    public bool ShowWindowsDrivers
    {
        get => _showWindowsDrivers;
        set
        {
            if (SetProperty(ref _showWindowsDrivers, value))
            {
                ApplyGroups();
            }
        }
    }

    public string Search
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _searchText, value))
            {
                _search = value.Trim();
                ApplyGroups();
                OnPropertyChanged(nameof(HasSearch));
                OnPropertyChanged(nameof(SearchSuggestions));
            }
        }
    }

    public bool HasSearch => _search.Length > 0;

    public IReadOnlyList<SearchSuggestion> SearchSuggestions =>
        Infrastructure.SearchSuggestions.Rank(
            _groups.Select(g => new DriverRowViewModel(g)),
            _search,
            r => r.Name,
            r => r.Matches(_search),
            r => new SearchSuggestion(r.Name, $"{DriverCatalog.Label(r.Group.Category)} · {r.ProviderText} · {r.VersionText}", DriverCatalog.IconKey(r.Group.Category)));

    /// <summary>Every driver line, Windows' own included (with alike devices counted).</summary>
    public int DriverCount => _groups.Sum(g => g.Count);

    /// <summary>Drivers from the devices' makers rather than Windows.</summary>
    public int MakerCount => _groups.Where(g => !g.IsMicrosoft).Sum(g => g.Count);

    public int ProblemCount => _groups.Count(g => g.HasProblem);

    /// <summary>The graphics card's driver ("NVIDIA 581.57"), or a dash.</summary>
    public string GraphicsDriverText
    {
        get
        {
            var gpu = GraphicsCard;
            if (gpu is null)
            {
                return "—";
            }

            bool nvidia = gpu.Provider?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false;
            string version = nvidia && DriverCatalog.NvidiaVersion(gpu.Version) is { } n ? n : gpu.Version ?? "—";
            return version;
        }
    }

    public string GraphicsDriverCaption => GraphicsCard is { } gpu ? Shorten(gpu.DeviceName) : "no graphics driver found";

    /// <summary>The page has an NVIDIA graphics card: the NVIDIA app button shows.</summary>
    public bool HasNvidiaCard => _installed.Any(IsNvidiaCard);

    public string EmptyText => _search.Length > 0 ? "No drivers match your search." : _isLoading ? "Reading your drivers…" : "No drivers to show.";

    // ---- Updates -----------------------------------------------------------------------------------

    public ObservableCollection<DriverUpdateViewModel> Updates { get; } = [];

    public bool HasUpdates => Updates.Count > 0;

    public int SelectedCount => Updates.Count(u => u.IsSelected && u.CanSelect);

    public int PendingCount => Updates.Count(u => !u.IsDone);

    /// <summary>The "select all" box: ticked when every open update is picked; ticking it picks them all, unticking none.</summary>
    public bool SelectAll
    {
        get
        {
            var open = Updates.Where(u => u.CanSelect).ToList();
            return open.Count > 0 && open.All(u => u.IsSelected);
        }
        set
        {
            foreach (var update in Updates.Where(u => u.CanSelect))
            {
                update.IsSelected = value;
            }
        }
    }

    public string InstallText => SelectedCount switch
    {
        0 => "Update selected",
        1 => "Update 1 driver",
        int n => $"Update {n.ToString(CultureInfo.CurrentCulture)} drivers",
    };

    /// <summary>What the check is doing now ("Asking Windows Update…").</summary>
    public string? CheckProgress
    {
        get => _checkProgress;
        private set => SetProperty(ref _checkProgress, value);
    }

    /// <summary>How the last install went ("Installed 2 updates. Restart Windows to finish.").</summary>
    public string? ResultText
    {
        get => _resultText;
        private set
        {
            if (SetProperty(ref _resultText, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(_resultText);

    /// <summary>Sources that couldn't be checked (no internet, Windows Update off…).</summary>
    public IReadOnlyList<string> Problems
    {
        get => _problems;
        private set
        {
            _problems = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProblems));
            OnPropertyChanged(nameof(ProblemsText));
        }
    }

    public bool HasProblems => _problems.Count > 0;

    public string ProblemsText => string.Join(Environment.NewLine, _problems);

    public string LastCheckedText => _lastChecked is { } when
        ? $"Last checked {(when.Date == Now().Date ? "today" : when.ToString("MMM d", CultureInfo.CurrentCulture))} at {when.ToString("t", CultureInfo.CurrentCulture)}"
        : "Not checked yet";

    /// <summary>The banner's headline.</summary>
    public string HeroTitle => !IsSupported ? "Drivers"
        : _isInstalling ? "Installing updates…"
        : _isChecking ? "Checking for updates…"
        : !_hasChecked ? "Check your drivers for updates"
        : PendingCount > 0 ? $"{DriverCatalog.Updates(PendingCount)} available"
        : "Your drivers are up to date";

    public string HeroDetail => _isChecking ? CheckProgress ?? "Asking Windows Update, NVIDIA and Microsoft…"
        : _isInstalling ? CheckProgress ?? "Your screen may go dark for a moment while a graphics driver installs."
        : !_hasChecked ? "Coreloom asks Windows Update for your devices' drivers, NVIDIA for a newer graphics driver, and checks the DirectX files older games need."
        : PendingCount > 0 ? "Pick the ones you want below, or update all of them. Nothing is installed until you choose."
        : HasProblems ? "Nothing new from the sources that answered." : "Windows Update, NVIDIA and DirectX have nothing newer for this PC.";

    /// <summary>The dial: 1 when everything is current, the share of installed updates while installing.</summary>
    public double Readiness => !_hasChecked ? 0 : Updates.Count == 0 ? 1 : Updates.Count(u => u.IsDone) / (double)Updates.Count;

    public string UpdatesCountText => _hasChecked ? PendingCount.ToString(CultureInfo.CurrentCulture) : "—";

    // ---- Loading -------------------------------------------------------------------------------------

    protected override void OnUpdate(TelemetryFrame frame)
    {
    }

    protected override void OnActivated()
    {
        if (!_isLoaded && !_isLoading && IsSupported)
        {
            _ = LoadAndCheckAsync();
        }
    }

    private async Task LoadAndCheckAsync()
    {
        await LoadAsync();
        if (!_hasChecked && _isLoaded)
        {
            await CheckAsync(); // the first visit looks for updates once; the button checks again
        }
    }

    /// <summary>Reads the installed drivers again.</summary>
    public async Task LoadAsync()
    {
        if (_isLoading || !IsSupported)
        {
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(EmptyText));
        try
        {
            _installed = await _service.InstalledAsync(CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log.Warn("Drivers", "Reading the installed drivers failed.", e);
            _installed = [];
        }
        finally
        {
            IsLoading = false;
        }

        _groups = DriverCatalog.Group(_installed);
        IsLoaded = true;
        ApplyGroups();
        OnPropertyChanged(nameof(DriverCount));
        OnPropertyChanged(nameof(MakerCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(GraphicsDriverText));
        OnPropertyChanged(nameof(GraphicsDriverCaption));
        OnPropertyChanged(nameof(HasNvidiaCard));
        Commands();
    }

    private async Task CheckAsync()
    {
        if (_isChecking || !IsSupported)
        {
            return;
        }

        IsChecking = true;
        ResultText = null;
        CheckProgress = "Asking Windows Update, NVIDIA and Microsoft…";
        Hero();
        DriverScan scan;
        try
        {
            var progress = new Progress<string>(text =>
            {
                CheckProgress = text;
                OnPropertyChanged(nameof(HeroDetail));
            });
            scan = await _service.CheckAsync(_installed, progress, CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log.Warn("Drivers", "Checking for driver updates failed.", e);
            scan = new DriverScan([], ["Couldn't check for updates. Check your internet connection and try again."]);
        }

        Updates.Clear();
        foreach (var update in scan.Updates
                     .OrderBy(u => u.Source == DriverSource.Nvidia ? 0 : u.Source == DriverSource.WindowsUpdate ? 1 : 2)
                     .ThenBy(u => u.Category)
                     .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Updates.Add(new DriverUpdateViewModel(this, update));
        }

        Problems = scan.Problems;
        _hasChecked = true;
        _lastChecked = Now();
        CheckProgress = null;
        IsChecking = false;
        Log.Info("Drivers", scan.Updates.Count == 0
            ? $"Checked for driver updates: none.{(scan.Problems.Count > 0 ? " Couldn't check: " + string.Join(" ", scan.Problems) : string.Empty)}"
            : $"Checked for driver updates: {string.Join(", ", scan.Updates.Select(u => $"{u.Name} {u.NewVersion} ({u.Source})"))}.");
        SelectionChanged();
        Hero();
    }

    private async Task InstallSelectedAsync()
    {
        var picked = Updates.Where(u => u.IsSelected && u.CanSelect).ToList();
        if (picked.Count == 0)
        {
            return;
        }

        IsInstalling = true;
        ResultText = null;
        Hero();
        int installed = 0;
        bool restart = false;
        foreach (var update in picked)
        {
            update.Working("Waiting…");
        }

        // Graphics last: its installer blanks the screen and can take a few minutes.
        foreach (var update in picked.OrderBy(u => u.Update.BlanksScreen ? 1 : 0))
        {
            update.Working("Downloading…");
            CheckProgress = $"{update.Name}: downloading…";
            OnPropertyChanged(nameof(HeroDetail));
            DriverInstallResult result;
            try
            {
                var progress = new Progress<string>(text =>
                {
                    update.Working(text);
                    CheckProgress = $"{update.Name}: {text.TrimEnd('…').ToLower(CultureInfo.CurrentCulture)}…";
                    OnPropertyChanged(nameof(HeroDetail));
                });
                result = await _service.InstallAsync(update.Update, progress, CancellationToken.None);
            }
            catch (Exception e) when (IsExpected(e))
            {
                result = new DriverInstallResult(false, "Couldn't be installed: " + e.Message);
            }

            update.Finished(result);
            Log.Info("Drivers", $"{update.Name} {update.Update.NewVersion}: {result.Message}");
            if (result.Success)
            {
                installed++;
                restart |= result.RestartNeeded;
            }

            OnPropertyChanged(nameof(Readiness));
        }

        CheckProgress = null;
        IsInstalling = false;
        ResultText = installed == picked.Count
            ? $"Installed {DriverCatalog.Updates(installed)}.{(restart ? " Restart Windows to finish." : string.Empty)}"
            : $"Installed {installed.ToString(CultureInfo.CurrentCulture)} of {DriverCatalog.Updates(picked.Count)}; the others say what went wrong.{(restart ? " Restart Windows to finish." : string.Empty)}";
        SelectionChanged();
        Hero();
        if (installed > 0)
        {
            await LoadAsync(); // the new versions
        }
    }

    private void OpenNvidiaApp()
    {
        if (!_service.OpenNvidiaApp())
        {
            _service.OpenUrl(NvidiaDrivers.DriversPage);
        }
    }

    internal void OpenUrl(string url) => _service.OpenUrl(url);

    internal string FormatBytes(long bytes) => Formatter.Bytes(bytes).ToString();

    internal void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectAll));
        OnPropertyChanged(nameof(InstallText));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(UpdatesCountText));
        InstallSelectedCommand.NotifyCanExecuteChanged();
    }

    private void ApplyGroups()
    {
        var shown = _groups
            .Where(g => _showWindowsDrivers || !g.IsMicrosoft || g.Category == DriverCategory.Graphics || g.HasProblem)
            .Select(g => new DriverRowViewModel(g))
            .Where(r => _search.Length == 0 || r.Matches(_search))
            .GroupBy(r => r.Group.Category)
            .OrderBy(g => g.Key)
            .Select(g => new DriverCategoryViewModel(g.Key, [.. g]))
            .ToList();
        Categories.Clear();
        foreach (var category in shown)
        {
            Categories.Add(category);
        }

        OnPropertyChanged(nameof(HasCategories));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HiddenText));
    }

    /// <summary>"38 of Windows' own drivers are hidden" under the list, while they are.</summary>
    public string? HiddenText
    {
        get
        {
            if (_showWindowsDrivers)
            {
                return null;
            }

            int hidden = _groups.Where(g => g.IsMicrosoft && g.Category != DriverCategory.Graphics && !g.HasProblem).Sum(g => g.Count);
            return hidden == 0 ? null : $"{hidden.ToString(CultureInfo.CurrentCulture)} of Windows' own drivers are hidden; they're updated with Windows.";
        }
    }

    private InstalledDriver? GraphicsCard => _installed
        .Where(d => d.Category == DriverCategory.Graphics && DriverCatalog.IsListed(d))
        .OrderByDescending(IsNvidiaCard)
        .ThenByDescending(d => !DriverCatalog.IsMicrosoft(d.Provider))
        .FirstOrDefault();

    private static bool IsNvidiaCard(InstalledDriver d) =>
        d.Category == DriverCategory.Graphics && ((d.Provider?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false) || d.DeviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

    private static string Shorten(string name) => name.Replace("NVIDIA ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    private void Hero()
    {
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroDetail));
        OnPropertyChanged(nameof(Readiness));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(UpdatesCountText));
        OnPropertyChanged(nameof(HasUpdates));
    }

    private void Commands()
    {
        CheckCommand.NotifyCanExecuteChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private static bool IsExpected(Exception e) =>
        e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException
            or HttpRequestException or TaskCanceledException or ArgumentException or System.ComponentModel.Win32Exception;
}
