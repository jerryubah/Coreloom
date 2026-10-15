using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.Gaming;
using Coreloom.Core.Overlay;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>An installed game: a card in the list or a cover in the grid, with Play and Open folder.</summary>
public sealed class GameViewModel : ObservableObject
{
    /// <summary>Backgrounds for games without a picture (picked from the name, so a game keeps its color).</summary>
    private static readonly string[] Placeholders = ["#3B5BDB", "#7048E8", "#0C8599", "#2B8A3E", "#E8590C", "#C2255C", "#5F3DC4", "#1971C2"];

    private readonly GamingViewModel _page;
    private GameEntry _game;
    private bool _isRunning;

    public GameViewModel(GameEntry game, GamingViewModel page)
    {
        _game = game;
        _page = page;
        PlayCommand = new RelayCommand(() => _page.Play(this));
        OpenFolderCommand = new RelayCommand(() => _page.OpenFolder(this), () => CanOpenFolder);
        int hash = 0;
        foreach (char c in game.Name)
        {
            hash = unchecked((hash * 31) + char.ToUpperInvariant(c));
        }

        PlaceholderColor = Placeholders[(int)((uint)hash % (uint)Placeholders.Length)];
    }

    public GameEntry Game => _game;

    public string Id => _game.Id;

    public string Name => _game.Name;

    public string StoreName => GameLibrary.StoreName(_game.Store);

    public string Initials => GameLibrary.Initials(_game.Name);

    /// <summary>#RRGGBB behind the initials when there is no picture.</summary>
    public string PlaceholderColor { get; }

    public string? Cover => _game.Artwork.Cover;

    public string? Banner => _game.Artwork.Banner;

    public string? Icon => _game.Artwork.Icon;

    /// <summary>The grid's picture: the portrait cover, else the wide banner (cropped).</summary>
    public string? GridPicture => _game.Artwork.Cover ?? _game.Artwork.Banner;

    /// <summary>The list's picture: the wide banner, else the cover, else the icon.</summary>
    public string? ListPicture => _game.Artwork.Banner ?? _game.Artwork.Cover;

    public bool HasGridPicture => GridPicture is not null;

    public bool HasListPicture => ListPicture is not null;

    /// <summary>Only the program's icon is known (shown over the placeholder color).</summary>
    public bool HasIconOnly => GridPicture is null && Icon is not null;

    /// <summary>Has no cover or banner, but one can be downloaded (if the user allows it).</summary>
    public bool CanDownloadPicture => GridPicture is null && _game.CoverUrls.Count > 0;

    public string SizeText { get; private set; } = string.Empty;

    public string LastPlayedText { get; private set; } = string.Empty;

    /// <summary>"Steam · 98.4 GB".</summary>
    public string Detail => SizeText.Length > 0 ? $"{StoreName} · {SizeText}" : StoreName;

    public bool CanOpenFolder => !string.IsNullOrEmpty(_game.InstallPath);

    public bool CanPlay => !string.IsNullOrEmpty(_game.Launch);

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public IRelayCommand PlayCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public bool Matches(string search) => GameLibrary.Matches(_game, search);

    internal void Refresh(UnitFormatter formatter, DateTimeOffset now)
    {
        SizeText = _game.SizeBytes is long bytes && bytes > 0 ? formatter.Bytes(bytes).ToString() : string.Empty;
        LastPlayedText = GameLibrary.LastPlayedText(_game.LastPlayed, now);
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(Detail));
    }

    internal void Update(GameEntry game, UnitFormatter formatter, DateTimeOffset now)
    {
        _game = game;
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(GridPicture));
        OnPropertyChanged(nameof(ListPicture));
        OnPropertyChanged(nameof(HasGridPicture));
        OnPropertyChanged(nameof(HasListPicture));
        OnPropertyChanged(nameof(HasIconOnly));
        OnPropertyChanged(nameof(CanDownloadPicture));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(CanPlay));
        Refresh(formatter, now);
        OpenFolderCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A switch for one Windows or driver setting (HAGS, Ultimate Performance, Game Mode…).</summary>
public sealed class GamingTweakViewModel : ObservableObject
{
    private readonly Func<GamingTweakViewModel, bool, Task> _set;
    private TweakStatus _status = TweakStatus.Unsupported("Checking…");
    private bool _isBusy;
    private string? _message;
    private bool? _pending;

    public GamingTweakViewModel(GamingTweak tweak, string title, string description, string iconKey, string? tag, Func<GamingTweakViewModel, bool, Task> set)
    {
        Tweak = tweak;
        Title = title;
        Description = description;
        IconKey = iconKey;
        Tag = tag;
        _set = set;
    }

    public GamingTweak Tweak { get; }

    public string Title { get; }

    public string Description { get; }

    public string IconKey { get; }

    /// <summary>A small label next to the title ("NVIDIA", "Windows 11").</summary>
    public string? Tag { get; }

    public bool HasTag => Tag is not null;

    public bool IsSupported => _status.IsSupported;

    /// <summary>The switch. Turning it changes the setting (and the page re-reads it; it flips back if Windows refused).</summary>
    public bool IsOn
    {
        get => _pending ?? _status.IsOn;
        set
        {
            if (value != IsOn && CanToggle)
            {
                _pending = value;
                OnPropertyChanged(nameof(IsOn));
                _ = _set(this, value);
            }
        }
    }

    public bool NeedsRestart => _status.NeedsRestart;

    public bool CanToggle => _status.IsSupported && !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    /// <summary>Why it can't be used, what went wrong, or that a restart is needed.</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    public string? StatusText => !_status.IsSupported ? "Not available" : _status.NeedsRestart ? "Restart to finish" : _status.IsOn ? "On" : "Off";

    /// <summary>"Tone.Cool" when on, "Tone.Warm" when a restart is pending, "Neutral" otherwise.</summary>
    public string ColorKey => !_status.IsSupported ? ColorKeys.Neutral : _status.NeedsRestart ? ColorKeys.ToneWarm : _status.IsOn ? ColorKeys.ToneCool : ColorKeys.Neutral;

    internal TweakStatus Status => _status;

    internal void Apply(TweakStatus status)
    {
        _status = status;
        _pending = null;
        Message = status.NeedsRestart ? "Restart Windows to finish." : status.IsSupported ? null : status.Note;
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(NeedsRestart));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ColorKey));
    }
}

/// <summary>A shader cache folder and its size.</summary>
public sealed record ShaderCacheRow(string Name, string Path, string SizeText) : StaticRecord;

/// <summary>
/// The frame pacer: a frame-rate limit for every game, set in the graphics driver (no injection, so it is safe with
/// anti-cheat), automatically a little under the refresh rate so variable refresh stays engaged; plus how evenly frames
/// arrive in the game that is running.
/// </summary>
public sealed class FramePacerViewModel : ObservableObject
{
    private readonly GamingViewModel _page;
    private bool _isBusy;
    private string? _message;
    private bool _hasLive;
    private string _liveFps = "—";
    private string _liveLow = "—";
    private string _liveConsistency = "—";
    private string _pacingText = "—";
    private string _pacingColorKey = ColorKeys.Neutral;
    private IReadOnlyList<Option<int>> _options = [];
    private bool? _pending;

    public FramePacerViewModel(GamingViewModel page)
    {
        _page = page;
    }

    private GamingStatus Status => _page.CurrentStatus;

    public bool IsSupported => Status.HasNvidiaDriver;

    public string UnsupportedText => "The frame pacer sets the NVIDIA graphics driver's own frame limiter, and no NVIDIA driver was found. On AMD graphics, AMD Software's Radeon Chill or Frame Rate Target Control does the same.";

    /// <summary>The switch: on sets the driver's limit for all games, off removes it.</summary>
    public bool IsOn
    {
        get => _pending ?? Status.FrameCap > 0;
        set
        {
            if (value != IsOn && CanToggle)
            {
                _pending = value;
                OnPropertyChanged(nameof(IsOn));
                _ = _page.SetFramePacerAsync(value ? Target : 0);
            }
        }
    }

    public bool CanToggle => IsSupported && !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        internal set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    /// <summary>The chosen limit (0 = automatic).</summary>
    public int SelectedTarget
    {
        get => _page.Store.Current.Gaming.FramePacerTarget;
        set
        {
            if (value == SelectedTarget)
            {
                return;
            }

            _page.Store.Update("Gaming", s => s.Gaming.FramePacerTarget = value <= 0 ? 0 : FrameCap.Clamp(value));
            Refresh();
            if (IsOn && CanToggle)
            {
                _ = _page.SetFramePacerAsync(Target);
            }
        }
    }

    public IReadOnlyList<Option<int>> Options
    {
        get => _options;
        private set => SetProperty(ref _options, value);
    }

    /// <summary>The limit in frames per second (what "Automatic" works out to on this display).</summary>
    public int Target => FrameCap.Resolve(SelectedTarget, Status.RefreshRate > 0 ? Status.RefreshRate : 60);

    /// <summary>The big number: the driver's limit when on, the limit it would use when off.</summary>
    public string LimitText => (IsOn ? Status.FrameCap : Target).ToString(CultureInfo.CurrentCulture);

    public string FrameTimeText => FrameCap.FrameTimeText(IsOn ? Status.FrameCap : Target) + " per frame";

    public string DisplayText => Status.RefreshRate > 0 ? $"{Status.RefreshRate.ToString(CultureInfo.CurrentCulture)} Hz display" : "Display refresh rate unknown";

    /// <summary>Why this limit.</summary>
    public string Explanation => SelectedTarget <= 0
        ? $"Just under your {(Status.RefreshRate > 0 ? Status.RefreshRate : 60).ToString(CultureInfo.CurrentCulture)} Hz refresh rate, so G-SYNC or FreeSync stays engaged and frames never queue up behind V-Sync. Even frame times, lower latency."
        : SelectedTarget >= Status.RefreshRate && Status.RefreshRate > 0
            ? "At or above the refresh rate: good for displays without variable refresh, with V-Sync on."
            : "A fixed limit: steadier frame times and less heat and noise when the game would otherwise swing above it.";

    /// <summary>The setup that paces best with variable refresh (G-SYNC / FreeSync).</summary>
    public string TipText => "For the smoothest result with G-SYNC or FreeSync, turn V-Sync on in the NVIDIA Control Panel and off in games: the limit keeps latency low and V-Sync catches the rare frame that would tear.";

    public string MethodText => "Uses the graphics driver's own frame limiter for every game, so nothing is added to games and it's safe with anti-cheat. It applies to games you start next.";

    /// <summary>The driver had a limit Coreloom didn't set (e.g. from the NVIDIA Control Panel).</summary>
    public string? ForeignLimitText => IsOn && Status.FrameCap != Target ? $"The driver's limit is {Status.FrameCap.ToString(CultureInfo.CurrentCulture)} FPS. Pick a limit to replace it." : null;

    public string? Message
    {
        get => _message;
        internal set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>A game is running and its frames are being measured.</summary>
    public bool HasLive
    {
        get => _hasLive;
        private set => SetProperty(ref _hasLive, value);
    }

    public string LiveFps
    {
        get => _liveFps;
        private set => SetProperty(ref _liveFps, value);
    }

    public string LiveLow
    {
        get => _liveLow;
        private set => SetProperty(ref _liveLow, value);
    }

    /// <summary>The 1% low as a share of the average ("91 %").</summary>
    public string LiveConsistency
    {
        get => _liveConsistency;
        private set => SetProperty(ref _liveConsistency, value);
    }

    /// <summary>"Smooth", "Uneven", "Stuttering".</summary>
    public string PacingText
    {
        get => _pacingText;
        private set => SetProperty(ref _pacingText, value);
    }

    public string PacingColorKey
    {
        get => _pacingColorKey;
        private set => SetProperty(ref _pacingColorKey, value);
    }

    internal void Live(FrameRateStats? stats)
    {
        if (stats is not { Fps: > 0 } s)
        {
            HasLive = false;
            LiveFps = LiveLow = LiveConsistency = PacingText = "—";
            PacingColorKey = ColorKeys.Neutral;
            return;
        }

        var grade = FramePacing.Grade(s.Fps, s.LowFps);
        HasLive = true;
        LiveFps = Math.Round(s.Fps).ToString(CultureInfo.CurrentCulture);
        LiveLow = s.LowFps > 0 ? Math.Round(s.LowFps).ToString(CultureInfo.CurrentCulture) : "—";
        LiveConsistency = s.LowFps > 0 ? FramePacing.Consistency(s.Fps, s.LowFps).ToString(CultureInfo.CurrentCulture) + " %" : "—";
        PacingText = FramePacing.Text(grade);
        PacingColorKey = grade == PacingGrade.Unknown ? ColorKeys.Neutral : ColorKeys.ForLevel(FramePacing.Level(grade));
    }

    internal void Refresh()
    {
        if (!_isBusy)
        {
            _pending = null;
        }

        int refresh = Status.RefreshRate > 0 ? Status.RefreshRate : 60;
        var options = new List<Option<int>> { new(0, $"Automatic · {FrameCap.AutoTarget(refresh).ToString(CultureInfo.CurrentCulture)} FPS") };
        foreach (int fps in FrameCap.Presets(refresh))
        {
            options.Add(new(fps, fps == refresh ? $"{fps.ToString(CultureInfo.CurrentCulture)} FPS · refresh rate" : $"{fps.ToString(CultureInfo.CurrentCulture)} FPS"));
        }

        if (SelectedTarget > 0 && !options.Any(o => o.Value == SelectedTarget))
        {
            options.Add(new(SelectedTarget, $"{SelectedTarget.ToString(CultureInfo.CurrentCulture)} FPS"));
        }

        if (Options.Count != options.Count || !Options.Zip(options).All(p => p.First == p.Second))
        {
            Options = options;
        }

        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(FrameTimeText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(ForeignLimitText));
    }
}

/// <summary>
/// The Gaming page: boosts for Windows and the graphics card, the frame pacer, shader caches and the installed games
/// (as cards or a grid of covers). Every change happens only when the user flips a switch or presses a button.
/// </summary>
public sealed class GamingViewModel : PageViewModel
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunningInterval = TimeSpan.FromSeconds(3);

    private readonly IGamingService _service;
    private readonly List<GameViewModel> _all = [];
    private GamingStatus _status = GamingStatus.Unavailable;
    private bool _isLoaded;
    private bool _isScanning;
    private bool _isDownloadingArtwork;
    private bool _isApplyingAll;
    private bool _statusBusy;
    private bool _statusAgain;
    private bool _runningBusy;
    private string _search = string.Empty;
    private DateTime _lastStatus = DateTime.MinValue;
    private DateTime _lastRunning = DateTime.MinValue;
    private RunningGame? _running;
    private bool _watching;
    private string? _heroMessage;

    public GamingViewModel(PageContext context)
        : base("gaming", "Gaming", "Icon.Controller", context)
    {
        _service = context.Tools.Gaming;
        FramePacer = new FramePacerViewModel(this);
        Boosts =
        [
            new(GamingTweak.GpuScheduling, "Hardware-accelerated GPU scheduling", "The graphics card schedules its own work: lower latency and steadier frames. Takes effect after a restart.", "Icon.Gpu", "HAGS", SetTweakAsync),
            new(GamingTweak.UltimatePower, "Ultimate Performance power plan", "Keeps the processor at full speed with no power-saving delays. Turning it off goes back to your previous plan.", "Icon.Power", null, SetTweakAsync),
            new(GamingTweak.GameMode, "Game Mode", "Windows gives the game priority and holds back updates and notifications while you play.", "Icon.Controller", null, SetTweakAsync),
            new(GamingTweak.WindowedOptimizations, "Optimizations for windowed games", "Borderless and windowed games use the faster flip presentation: lower latency, variable refresh and Auto HDR.", "Icon.Window", "Windows 11", SetTweakAsync),
            new(GamingTweak.NoBackgroundRecording, "No background recording", "Stops Xbox Game Bar from recording the last minutes of play in the background, which costs frames.", "Icon.Camera", null, SetTweakAsync),
            new(GamingTweak.MaxGpuPerformance, "Maximum graphics card performance", "The driver's \"Prefer maximum performance\" mode: no downclocking between scenes, fewer frame-time spikes.", "Icon.Rocket", "NVIDIA", SetTweakAsync),
        ];
        UnlimitedShaderCache = new(GamingTweak.UnlimitedShaderCache, "Unlimited shader cache", "The driver keeps every pipeline it compiles instead of discarding older ones when the cache fills, so games don't compile the same shaders again mid-play.", "Icon.Storage", "NVIDIA", SetTweakAsync);

        RescanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && IsSupported);
        RefreshCommand = new AsyncRelayCommand(() => RefreshStatusAsync(force: true));
        ApplyAllCommand = new AsyncRelayCommand(ApplyAllAsync, () => IsSupported && !_isApplyingAll && AllTweaks.Any(t => t.IsSupported && !t.IsOn && !t.IsBusy));
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        DownloadArtworkCommand = new AsyncRelayCommand(DownloadArtworkAsync, () => !IsDownloadingArtwork && MissingArtworkCount > 0);
        DismissDriverNoticeCommand = new RelayCommand(DismissDriverNotice);
        ShowListCommand = new RelayCommand(() => IsGridView = false);
        ShowGridCommand = new RelayCommand(() => IsGridView = true);
        FramePacer.Refresh();
    }

    /// <summary>For tests: the clock "played … ago" texts use.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    internal SettingsService Store => Settings;

    internal GamingStatus CurrentStatus => _status;

    public bool IsSupported => _service.IsSupported;

    public string UnsupportedText => "Gaming tools read and change Windows and graphics-driver settings, which aren't available here.";

    /// <summary>Hardware-accelerated GPU scheduling, Ultimate Performance, Game Mode, windowed optimizations, background recording, GPU power mode.</summary>
    public IReadOnlyList<GamingTweakViewModel> Boosts { get; }

    public GamingTweakViewModel UnlimitedShaderCache { get; }

    public FramePacerViewModel FramePacer { get; }

    private IEnumerable<GamingTweakViewModel> AllTweaks => Boosts.Append(UnlimitedShaderCache);

    public IAsyncRelayCommand RescanCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>"Boost now": turns on every boost that is off.</summary>
    public IAsyncRelayCommand ApplyAllCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IAsyncRelayCommand DownloadArtworkCommand { get; }

    public IRelayCommand DismissDriverNoticeCommand { get; }

    public IRelayCommand ShowListCommand { get; }

    public IRelayCommand ShowGridCommand { get; }

    /// <summary>The first scan is done (until then the page shows "Looking for games…").</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    // ---- Hero ----------------------------------------------------------------------------------------------

    public int BoostsAvailable => AllTweaks.Count(t => t.IsSupported);

    public int BoostsOn => AllTweaks.Count(t => t.IsSupported && t.IsOn);

    /// <summary>0…1 for the readiness ring.</summary>
    public double Readiness => BoostsAvailable == 0 ? 0 : BoostsOn / (double)BoostsAvailable;

    public string ReadinessText => BoostsAvailable == 0 ? "—" : $"{BoostsOn.ToString(CultureInfo.CurrentCulture)}/{BoostsAvailable.ToString(CultureInfo.CurrentCulture)}";

    public string HeroTitle => !IsSupported ? "Gaming"
        : BoostsAvailable == 0 ? "Checking your PC…"
        : BoostsOn == BoostsAvailable ? "Your PC is tuned for games"
        : $"{(BoostsAvailable - BoostsOn).ToString(CultureInfo.CurrentCulture)} {(BoostsAvailable - BoostsOn == 1 ? "boost" : "boosts")} can make your games smoother";

    public string HeroDetail => RestartPending
        ? "Restart Windows to finish turning on hardware-accelerated GPU scheduling."
        : BoostsOn == BoostsAvailable && BoostsAvailable > 0
            ? "Windows and your graphics card are set up for the highest, steadiest frame rates."
            : "Turn them on one by one below, or all at once. Each switch shows what it changes, and you can turn it off again anytime.";

    public bool RestartPending => AllTweaks.Any(t => t.NeedsRestart);

    public bool AllBoostsOn => BoostsAvailable > 0 && BoostsOn == BoostsAvailable;

    public string ReadinessColorKey => AllBoostsOn ? ColorKeys.ToneCool : Readiness >= 0.5 ? ColorKeys.ToneNormal : ColorKeys.ToneWarm;

    /// <summary>What the last "Boost now" did.</summary>
    public string? HeroMessage
    {
        get => _heroMessage;
        private set
        {
            if (SetProperty(ref _heroMessage, value))
            {
                OnPropertyChanged(nameof(HasHeroMessage));
            }
        }
    }

    public bool HasHeroMessage => !string.IsNullOrEmpty(_heroMessage);

    public string DisplayText => _status.RefreshRate > 0 ? $"{_status.RefreshRate.ToString(CultureInfo.CurrentCulture)} Hz" : "—";

    public string DriverText => _status.DriverVersion ?? "—";

    public string FrameLimitText => _status.FrameCap > 0 ? $"{_status.FrameCap.ToString(CultureInfo.CurrentCulture)} FPS" : "Off";

    public string GameCountText => _all.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>The library game that is running.</summary>
    public bool IsPlaying => _running is not null;

    public string NowPlayingName => _running?.Game.Name ?? string.Empty;

    // ---- Shader cache --------------------------------------------------------------------------------------

    public ObservableCollection<ShaderCacheRow> ShaderCaches { get; } = [];

    public bool HasShaderCaches => ShaderCaches.Count > 0;

    /// <summary>"6.2 GB in 4 caches".</summary>
    public string ShaderCacheSummary
    {
        get
        {
            var present = _status.ShaderCaches.Where(c => c.Files > 0).ToList();
            if (present.Count == 0)
            {
                return "No shader caches yet: games create them the first time they run.";
            }

            long total = present.Sum(c => c.Bytes);
            return $"{Formatter.Bytes(total)} of compiled shaders in {present.Count.ToString(CultureInfo.CurrentCulture)} {(present.Count == 1 ? "cache" : "caches")}";
        }
    }

    /// <summary>Shown when the driver changed since last time (caches start over).</summary>
    public string? DriverNotice => ShaderCachesDriverNotice();

    public bool HasDriverNotice => DriverNotice is not null;

    public string ShaderCacheNote => "Smart Optimize never clears these unless you tick \"Shader caches\" yourself. Coreloom doesn't pre-compile shaders by injecting into games: that's what anti-cheat systems ban for.";

    // ---- Library -------------------------------------------------------------------------------------------

    /// <summary>The games that match the search, in the chosen order.</summary>
    public ObservableCollection<GameViewModel> Games { get; } = [];

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RescanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasGames => Games.Count > 0;

    public int GameCount => _all.Count;

    /// <summary>"38 games · 2.41 TB".</summary>
    public string LibrarySummary
    {
        get
        {
            if (_all.Count == 0)
            {
                return IsLoaded ? "No installed games found." : "Looking for games…";
            }

            long total = _all.Sum(g => g.Game.SizeBytes ?? 0);
            string count = _all.Count == 1 ? "1 game" : $"{_all.Count.ToString(CultureInfo.CurrentCulture)} games";
            return total > 0 ? $"{count} · {Formatter.Bytes(total)}" : count;
        }
    }

    public string EmptyText => _search.Length > 0 && _all.Count > 0
        ? "No games match your search."
        : "Games installed with Steam, Epic Games, GOG, Xbox, Ubisoft Connect, EA app, Battle.net or Riot show up here.";

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, (value ?? string.Empty).Trim()))
            {
                ApplyLibrary();
            }
        }
    }

    public bool IsGridView
    {
        get => Settings.Current.Gaming.LibraryView == GameLibraryView.Grid;
        set
        {
            if (value != IsGridView)
            {
                Settings.Update("Gaming", s => s.Gaming.LibraryView = value ? GameLibraryView.Grid : GameLibraryView.List);
                OnPropertyChanged(nameof(IsGridView));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(SelectedViewOption));
            }
        }
    }

    public bool IsListView => !IsGridView;

    /// <summary>The List / Grid switch.</summary>
    public IReadOnlyList<Option<GameLibraryView>> ViewOptions { get; } =
    [
        new(GameLibraryView.List, "List"),
        new(GameLibraryView.Grid, "Grid"),
    ];

    public Option<GameLibraryView> SelectedViewOption
    {
        get => ViewOptions[IsGridView ? 1 : 0];
        set
        {
            if (value is not null)
            {
                IsGridView = value.Value == GameLibraryView.Grid;
            }
        }
    }

    public IReadOnlyList<Option<GameSort>> SortOptions { get; } =
    [
        new(GameSort.RecentlyPlayed, "Recently played"),
        new(GameSort.Name, "Name"),
        new(GameSort.Size, "Size"),
    ];

    public GameSort SelectedSort
    {
        get => Settings.Current.Gaming.LibrarySort;
        set
        {
            if (value != SelectedSort)
            {
                Settings.Update("Gaming", s => s.Gaming.LibrarySort = value);
                OnPropertyChanged(nameof(SelectedSort));
                ApplyLibrary();
            }
        }
    }

    /// <summary>Games without a picture that can be downloaded from their store.</summary>
    public int MissingArtworkCount => _all.Count(g => g.CanDownloadPicture);

    /// <summary>The offer to download missing covers (until the user has allowed it once).</summary>
    public bool ShowArtworkOffer => MissingArtworkCount > 0 && !Settings.Current.Gaming.DownloadArtwork && !IsDownloadingArtwork;

    public string ArtworkOfferText => MissingArtworkCount == 1
        ? "1 game has no cover picture on this PC. Download it from its store?"
        : $"{MissingArtworkCount.ToString(CultureInfo.CurrentCulture)} games have no cover picture on this PC. Download them from their stores?";

    public bool IsDownloadingArtwork
    {
        get => _isDownloadingArtwork;
        private set
        {
            if (SetProperty(ref _isDownloadingArtwork, value))
            {
                OnPropertyChanged(nameof(ShowArtworkOffer));
                DownloadArtworkCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // ---- Lifecycle -----------------------------------------------------------------------------------------

    /// <summary>Scans the library and reads the settings (the first time the page opens, and from tests).</summary>
    public async Task LoadAsync()
    {
        if (!IsSupported)
        {
            IsLoaded = true;
            return;
        }

        await Task.WhenAll(ScanAsync(), RefreshStatusAsync(force: true));
        await CheckRunningAsync();
    }

    protected override void OnActivated()
    {
        if (!IsLoaded && !IsScanning)
        {
            _ = LoadAsync();
        }
        else if (IsSupported)
        {
            _ = RefreshStatusAsync(force: false);
            _ = CheckRunningAsync();
        }
    }

    protected override void OnDeactivated() => Watch(false);

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (!IsSupported || !IsLoaded)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastRunning >= RunningInterval)
        {
            _ = CheckRunningAsync();
        }

        if (now - _lastStatus >= StatusInterval)
        {
            _ = RefreshStatusAsync(force: false);
        }

        UpdateLive();
    }

    public override void OnSettingsChanged(string section)
    {
        if (section is "Monitoring" or "All")
        {
            var now = Now();
            foreach (var game in _all)
            {
                game.Refresh(Formatter, now);
            }

            OnPropertyChanged(nameof(LibrarySummary));
            OnPropertyChanged(nameof(ShaderCacheSummary));
            ApplyShaderCaches();
        }
    }

    // ---- Library -------------------------------------------------------------------------------------------

    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        try
        {
            var games = await _service.ScanLibraryAsync(CancellationToken.None);
            ApplyGames(games);
            if (Settings.Current.Gaming.DownloadArtwork && MissingArtworkCount > 0)
            {
                _ = DownloadArtworkAsync();
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            // shown as before; Rescan tries again
        }
        finally
        {
            IsScanning = false;
            IsLoaded = true;
            OnPropertyChanged(nameof(LibrarySummary));
            OnPropertyChanged(nameof(EmptyText));
        }
    }

    private void ApplyGames(IReadOnlyList<GameEntry> games)
    {
        var now = Now();
        var byId = _all.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        var next = new List<GameViewModel>(games.Count);
        foreach (var entry in GameLibrary.Merge(games))
        {
            if (byId.Remove(entry.Id, out var existing))
            {
                existing.Update(entry, Formatter, now);
                next.Add(existing);
            }
            else
            {
                var game = new GameViewModel(entry, this);
                game.Refresh(Formatter, now);
                next.Add(game);
            }
        }

        _all.Clear();
        _all.AddRange(next);
        ApplyLibrary();
    }

    private void ApplyLibrary()
    {
        var sorted = GameLibrary.Sort(_all.Select(g => g.Game), SelectedSort);
        var byId = _all.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        var visible = sorted.Select(e => byId[e.Id]).Where(g => g.Matches(_search)).ToList();
        foreach (var game in _all)
        {
            game.IsRunning = _running is not null && string.Equals(_running.Game.Id, game.Id, StringComparison.OrdinalIgnoreCase);
        }

        Sync(Games, visible);
        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(GameCount));
        OnPropertyChanged(nameof(GameCountText));
        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(MissingArtworkCount));
        OnPropertyChanged(nameof(ShowArtworkOffer));
        OnPropertyChanged(nameof(ArtworkOfferText));
        DownloadArtworkCommand.NotifyCanExecuteChanged();
    }

    internal void Play(GameViewModel game)
    {
        if (!_service.Launch(game.Game))
        {
            HeroMessage = $"Couldn't start {game.Name}. Try starting it from {game.StoreName}.";
        }
    }

    internal void OpenFolder(GameViewModel game)
    {
        if (game.Game.InstallPath is { Length: > 0 } path)
        {
            _service.OpenFolder(path);
        }
    }

    private async Task DownloadArtworkAsync()
    {
        if (IsDownloadingArtwork)
        {
            return;
        }

        if (!Settings.Current.Gaming.DownloadArtwork)
        {
            Settings.Update("Gaming", s => s.Gaming.DownloadArtwork = true);
        }

        IsDownloadingArtwork = true;
        try
        {
            var missing = _all.Where(g => g.CanDownloadPicture).Select(g => g.Game).ToList();
            var updated = await _service.DownloadArtworkAsync(missing, CancellationToken.None);
            var now = Now();
            var byId = _all.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in updated)
            {
                if (byId.TryGetValue(entry.Id, out var game))
                {
                    game.Update(entry, Formatter, now);
                }
            }
        }
        catch (Exception e) when (IsExpected(e) || e is HttpRequestException or TaskCanceledException)
        {
            // pictures stay as they were
        }
        finally
        {
            IsDownloadingArtwork = false;
            ApplyLibrary();
        }
    }

    // ---- Running game --------------------------------------------------------------------------------------

    private async Task CheckRunningAsync()
    {
        if (_runningBusy || _all.Count == 0)
        {
            return;
        }

        _runningBusy = true;
        _lastRunning = DateTime.UtcNow;
        try
        {
            var running = await _service.FindRunningAsync(_all.Select(g => g.Game).ToList(), CancellationToken.None);
            if (running?.Game.Id != _running?.Game.Id || (running is null) != (_running is null))
            {
                _running = running;
                ApplyLibrary();
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(NowPlayingName));
            }
            else
            {
                _running = running;
            }

            Watch(IsActive && _running is not null);
            UpdateLive();
        }
        catch (Exception e) when (IsExpected(e))
        {
        }
        finally
        {
            _runningBusy = false;
        }
    }

    private void Watch(bool on)
    {
        if (on != _watching)
        {
            _watching = on;
            _service.WatchFrameRates(on);
        }

        if (!on)
        {
            FramePacer.Live(null);
        }
    }

    private void UpdateLive()
    {
        if (_running is null || !_watching)
        {
            FramePacer.Live(null);
            return;
        }

        FrameRateStats? best = null;
        foreach (int id in _running.ProcessIds)
        {
            if (_service.FrameRate(id) is { } stats && (best is null || stats.Fps > best.Value.Fps))
            {
                best = stats;
            }
        }

        FramePacer.Live(best);
    }

    // ---- Settings ------------------------------------------------------------------------------------------

    private async Task RefreshStatusAsync(bool force)
    {
        if (_statusBusy)
        {
            _statusAgain |= force; // a change was made meanwhile: read again once the running read is done
            return;
        }

        if (!force && DateTime.UtcNow - _lastStatus < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _statusBusy = true;
        try
        {
            do
            {
                _statusAgain = false;
                _lastStatus = DateTime.UtcNow;
                try
                {
                    ApplyStatus(await _service.StatusAsync(Settings.Current.Gaming, CancellationToken.None));
                }
                catch (Exception e) when (IsExpected(e))
                {
                }
            }
            while (_statusAgain);
        }
        finally
        {
            _statusBusy = false;
        }
    }

    private void ApplyStatus(GamingStatus status)
    {
        _status = status;
        foreach (var tweak in AllTweaks)
        {
            if (!tweak.IsBusy)
            {
                tweak.Apply(status.Tweak(tweak.Tweak));
            }
        }

        if (Settings.Current.Gaming.LastDriverVersion is null && status.DriverVersion is not null)
        {
            Settings.Update("Gaming", s => s.Gaming.LastDriverVersion = status.DriverVersion); // first look: nothing to compare with
        }

        FramePacer.Refresh();
        ApplyShaderCaches();
        HeroChanged();
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DriverText));
        OnPropertyChanged(nameof(FrameLimitText));
        OnPropertyChanged(nameof(DriverNotice));
        OnPropertyChanged(nameof(HasDriverNotice));
    }

    private void ApplyShaderCaches()
    {
        var rows = _status.ShaderCaches
            .Where(c => c.Files > 0)
            .GroupBy(c => c.Name)
            .Select(g => new ShaderCacheRow(g.Key, g.First().Path, Formatter.Bytes(g.Sum(c => c.Bytes)).ToString()))
            .ToList();
        if (ShaderCaches.Count != rows.Count || !ShaderCaches.Zip(rows).All(p => p.First == p.Second))
        {
            ShaderCaches.Clear();
            foreach (var row in rows)
            {
                ShaderCaches.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasShaderCaches));
        OnPropertyChanged(nameof(ShaderCacheSummary));
    }

    private string? ShaderCachesDriverNotice() => Coreloom.Core.Gaming.ShaderCaches.DriverChangeText(Settings.Current.Gaming.LastDriverVersion, _status.DriverVersion);

    private void DismissDriverNotice()
    {
        if (_status.DriverVersion is { } version)
        {
            Settings.Update("Gaming", s => s.Gaming.LastDriverVersion = version);
        }

        OnPropertyChanged(nameof(DriverNotice));
        OnPropertyChanged(nameof(HasDriverNotice));
    }

    private void HeroChanged()
    {
        OnPropertyChanged(nameof(BoostsAvailable));
        OnPropertyChanged(nameof(BoostsOn));
        OnPropertyChanged(nameof(Readiness));
        OnPropertyChanged(nameof(ReadinessText));
        OnPropertyChanged(nameof(ReadinessColorKey));
        OnPropertyChanged(nameof(AllBoostsOn));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroDetail));
        OnPropertyChanged(nameof(RestartPending));
        ApplyAllCommand.NotifyCanExecuteChanged();
    }

    private async Task SetTweakAsync(GamingTweakViewModel tweak, bool on)
    {
        tweak.IsBusy = true;
        tweak.Message = null;
        TweakResult result;
        try
        {
            result = await _service.SetTweakAsync(tweak.Tweak, on, Settings.Current.Gaming, CancellationToken.None);
            Settings.Update("Gaming"); // the service may have noted the power plans to restore
        }
        catch (Exception e) when (IsExpected(e))
        {
            result = new TweakResult(false, "Windows didn't accept the change.");
        }
        finally
        {
            tweak.IsBusy = false;
        }

        await RefreshStatusAsync(force: true);
        if (!result.Success)
        {
            tweak.Message = result.Message;
        }
        else if (result.NeedsRestart || tweak.NeedsRestart)
        {
            tweak.Message = "Restart Windows to finish.";
        }

        HeroChanged();
    }

    internal async Task SetFramePacerAsync(int fps)
    {
        FramePacer.IsBusy = true;
        FramePacer.Message = null;
        TweakResult result;
        try
        {
            result = await _service.SetFrameCapAsync(fps, CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            result = new TweakResult(false, "The graphics driver didn't accept the change.");
        }
        finally
        {
            FramePacer.IsBusy = false;
        }

        await RefreshStatusAsync(force: true);
        FramePacer.Message = result.Success
            ? fps > 0 ? (_running is not null ? $"Restart {_running.Game.Name} to apply the new limit." : null) : null
            : result.Message;
    }

    private async Task ApplyAllAsync()
    {
        _isApplyingAll = true;
        ApplyAllCommand.NotifyCanExecuteChanged();
        try
        {
            int changed = 0;
            foreach (var tweak in AllTweaks.Where(t => t.IsSupported && !t.IsOn).ToList())
            {
                await SetTweakAsync(tweak, true);
                if (tweak.IsOn)
                {
                    changed++;
                }
            }

            HeroMessage = changed == 0 ? null
                : RestartPending ? $"Turned on {changed.ToString(CultureInfo.CurrentCulture)} {(changed == 1 ? "boost" : "boosts")}. Restart Windows to finish."
                : $"Turned on {changed.ToString(CultureInfo.CurrentCulture)} {(changed == 1 ? "boost" : "boosts")}.";
        }
        finally
        {
            _isApplyingAll = false;
            HeroChanged();
        }
    }

    private static bool IsExpected(Exception e) =>
        e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException or ArgumentException or System.ComponentModel.Win32Exception;

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        where T : class
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int at = target.IndexOf(item);
            if (at == i)
            {
                continue;
            }

            if (at >= 0)
            {
                target.Move(at, i);
            }
            else
            {
                target.Insert(i, item);
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
