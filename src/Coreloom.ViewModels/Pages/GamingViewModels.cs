using Coreloom.Core;
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
        EditCommand = new RelayCommand(() => _page.EditGame(this), () => IsCustom);
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

    /// <summary>Has no cover or banner on this PC, so one is looked up and downloaded automatically.</summary>
    public bool CanDownloadPicture => GridPicture is null;

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

    /// <summary>Changes or removes a game added by hand.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Added by hand (can be edited and removed).</summary>
    public bool IsCustom => _game.Store == GameStore.Custom;

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
    private readonly bool _countsAsBoost;
    private string? _advice;
    private IRelayCommand? _detailsCommand;
    private bool _isBusy;
    private string? _message;
    private string? _note;
    private bool _showingOutcome;
    private readonly Dismissal _outcomeDismissal = new();
    private bool? _pending;

    private string? _recommendation;

    public GamingTweakViewModel(
        GamingTweak tweak,
        string title,
        string description,
        string iconKey,
        string? tag,
        Func<GamingTweakViewModel, bool, Task> set,
        string? category = null,
        string? recommendation = null,
        string? caveat = null,
        bool countsAsBoost = true)
    {
        Tweak = tweak;
        Title = title;
        Description = description;
        IconKey = iconKey;
        Tag = tag;
        _set = set;
        Category = category;
        _recommendation = recommendation;
        Caveat = caveat;
        _countsAsBoost = countsAsBoost;
    }

    /// <summary>What it helps with ("Smoothness", "Frame stability", "Visual fidelity"…), shown as a pill.</summary>
    public string? Category { get; }

    public bool HasCategory => Category is not null;

    /// <summary>"Smoothness" gets the green pill; the others a neutral one.</summary>
    public bool IsSmoothness => Category == "Smoothness";

    /// <summary>"Recommended: …" — how to use it on this PC.</summary>
    public string? Recommendation
    {
        get => _recommendation;
        set
        {
            if (SetProperty(ref _recommendation, value))
            {
                OnPropertyChanged(nameof(HasRecommendation));
            }
        }
    }

    public bool HasRecommendation => !string.IsNullOrEmpty(_recommendation);

    /// <summary>What it doesn't do ("Improves how frames are shown, not the frame rate").</summary>
    public string? Caveat { get; }

    public bool HasCaveat => Caveat is not null;

    /// <summary>
    /// Counted in the banner's "boosts on" and turned on by "Boost now". Every boost on the page is (since 1.4.9 also
    /// Ultimate Performance on a Ryzen X3D), so the ring's count matches the switches: 9 of 9 when all are on.
    /// </summary>
    public bool CountsAsBoost => _countsAsBoost;

    /// <summary>
    /// A trade-off of this boost on this PC (Ultimate Performance on a multi-CCD Ryzen X3D stops the core parking AMD's
    /// driver uses), or null. Shown when the pointer rests on the card; it doesn't change the count or Boost now.
    /// </summary>
    public string? Advice
    {
        get => _advice;
        internal set => SetProperty(ref _advice, value);
    }

    /// <summary>Opens more about this boost (core scheduling's details), or null when there's nothing more.</summary>
    public IRelayCommand? DetailsCommand
    {
        get => _detailsCommand;
        internal set
        {
            if (SetProperty(ref _detailsCommand, value))
            {
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasDetails => _detailsCommand is not null;

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

    /// <summary>
    /// Why it can't be used (or a hint), which stays; or, for five seconds after the switch is used, what went wrong or
    /// that a restart is needed (the status keeps saying "Restart to finish").
    /// </summary>
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

    /// <summary>Shows how using the switch went for five seconds, then the usual note again; null shows the note now.</summary>
    internal void ShowOutcome(string? message)
    {
        _showingOutcome = message is not null;
        Message = message ?? _note;
        _outcomeDismissal.Update(_showingOutcome, () =>
        {
            _showingOutcome = false;
            Message = _note;
        });
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
        _note = status.NeedsRestart ? null : status.Note; // why it can't be used, or a hint
        if (!_showingOutcome)
        {
            Message = _note;
        }

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

/// <summary>A game with a frame limit of its own, shown as a chip under the frame limiter.</summary>
public sealed record GameCapChip(string GameId, string Name, string LimitText, IRelayCommand SelectCommand, IAsyncRelayCommand RemoveCommand) : StaticRecord;

/// <summary>
/// The frame limiter: a frame-rate limit set in the graphics driver (no injection, so it is safe with anti-cheat), for
/// every game or for one game only (the driver's profile for that game's programs), automatically a little under the
/// refresh rate so variable refresh stays engaged; plus how evenly frames arrive in the game that is running.
/// </summary>
public sealed class FramePacerViewModel : ObservableObject
{
    /// <summary>The scope that applies to every game.</summary>
    public const string AllGames = "";

    private readonly GamingViewModel _page;
    private bool _isBusy;
    private string? _message;
    private readonly Dismissal _messageDismissal = new();
    private bool _hasLive;
    private string _liveFps = "—";
    private string _liveLow = "—";
    private string _liveConsistency = "—";
    private string _pacingText = "—";
    private string _pacingColorKey = ColorKeys.Neutral;
    private IReadOnlyList<Option<int>> _options = [];
    private IReadOnlyList<Option<string>> _scopes = [new(AllGames, "All games")];
    private IReadOnlyList<GameCapChip> _gameCaps = [];
    private string _scope = AllGames;
    private int _gameChoice;
    private bool? _pending;

    public FramePacerViewModel(GamingViewModel page)
    {
        _page = page;
        FrameGenerationOptions =
        [
            new(1, "Off"),
            new(2, "2× (DLSS or FSR frame generation)"),
            new(3, "3× (multi frame generation)"),
            new(4, "4× (multi frame generation)"),
        ];
        page.Adaptive.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AdaptivePacingController.IsBusy) or nameof(AdaptivePacingController.IsOn))
            {
                OnPropertyChanged(nameof(AdaptiveOn));
                OnPropertyChanged(nameof(CanToggleAdaptive));
                OnPropertyChanged(nameof(ShowFrameGeneration));
            }
            else if (e.PropertyName is nameof(AdaptivePacingController.GameName))
            {
                OnPropertyChanged(nameof(ShowFrameGeneration));
                OnPropertyChanged(nameof(FrameGenerationLabel));
                OnPropertyChanged(nameof(SelectedFrameGeneration));
            }
        };
    }

    // ---- Adaptive frame pacing ------------------------------------------------------------------------------

    /// <summary>The engine behind the adaptive pacing switch (its status and last change are bound directly).</summary>
    public AdaptivePacingController Adaptive => _page.Adaptive;

    public string AdaptiveTags => "Frame stability · 1% lows · Smoothness";

    public string AdaptiveDescription => "An intelligent engine that monitors actual frame times and automatically finds a stable FPS target for each game.";

    /// <summary>The switch: on lets adaptive pacing set each game's limit as it plays; off removes the limits it set.</summary>
    public bool AdaptiveOn
    {
        get => Adaptive.IsOn;
        set
        {
            if (value != AdaptiveOn && CanToggleAdaptive)
            {
                _ = Adaptive.SetEnabledAsync(value);
            }
        }
    }

    public bool CanToggleAdaptive => _page.IsSupported && !Adaptive.IsBusy;

    /// <summary>How it works, and what it can't do here.</summary>
    public string AdaptiveMethodText => IsSupported
        ? "While a library game plays, Coreloom measures each frame (Windows' graphics events, nothing added to the game), finds the frame rate it holds with steady 1% lows over the last two minutes, and sets that game's own limit in the NVIDIA driver: a step at a time, at most 15 % lower or 10 % higher, and never again until the change shows. It stays under your refresh rate with G-SYNC or FreeSync, and works in whole rendered frames with frame generation. A limit you set for a game is left alone; turning this off removes the limits it set."
        : "While a library game plays, Coreloom measures each frame and works out the frame rate it holds with steady 1% lows. Setting the limit needs an NVIDIA driver, so here it suggests one to set in AMD Software (Frame Rate Target Control) or in the game.";

    /// <summary>The game whose frame generation is shown: the one picked under "Apply to", else the one being paced.</summary>
    private string? FrameGenerationGameId => IsGameScope ? _scope : Adaptive.GameId;

    public bool ShowFrameGeneration => Adaptive.IsOn && FrameGenerationGameId is not null;

    public string FrameGenerationLabel
    {
        get
        {
            string? id = FrameGenerationGameId;
            string name = id is null ? "this game" : _page.FindGame(id)?.Name ?? Adaptive.GameName ?? "this game";
            return $"Frame generation in {name}";
        }
    }

    public IReadOnlyList<Option<int>> FrameGenerationOptions { get; }

    /// <summary>
    /// Frames shown per frame rendered in the game (set in the game's options; Coreloom can't see it): adaptive pacing
    /// keeps whole rendered frames and at least 40 of them a second.
    /// </summary>
    public int SelectedFrameGeneration
    {
        get => FrameGenerationGameId is { } id ? _page.Store.Current.Gaming.PacingFor(id)?.FrameGeneration ?? 1 : 1;
        set
        {
            value = Math.Clamp(value, 1, 4);
            if (FrameGenerationGameId is not { } id || value == SelectedFrameGeneration)
            {
                return;
            }

            string name = _page.FindGame(id)?.Name ?? Adaptive.GameName ?? id;
            _page.Store.Update("Gaming", s => AdaptivePacingController.Learned(s.Gaming, id, name).FrameGeneration = value);
            Adaptive.FrameGenerationChanged(id);
            OnPropertyChanged();
        }
    }

    private GamingStatus Status => _page.CurrentStatus;

    private int RefreshHz => Status.RefreshRate > 0 ? Status.RefreshRate : 60;

    public bool IsSupported => Status.HasNvidiaDriver;

    public string UnsupportedText => "The frame limiter sets the NVIDIA graphics driver's own high-precision limiter, and no NVIDIA driver was found. On AMD graphics, AMD Software's Radeon Chill or Frame Rate Target Control does the same.";

    /// <summary>"All games" and every game in the library.</summary>
    public IReadOnlyList<Option<string>> Scopes
    {
        get => _scopes;
        private set => SetProperty(ref _scopes, value);
    }

    /// <summary>What the limit applies to: <see cref="AllGames"/> or a game's library id.</summary>
    public string SelectedScope
    {
        get => _scope;
        set
        {
            value ??= AllGames;
            if (value != _scope)
            {
                _scope = value;
                _pending = null;
                _gameChoice = _page.Store.Current.Gaming.FramePacerTarget; // a new game starts from the all-games choice
                Message = null;
                OnPropertyChanged(nameof(SelectedScope));
                Update();
            }
        }
    }

    public bool IsGameScope => _scope.Length > 0;

    private GameFrameCap? ScopeCap => IsGameScope ? _page.Store.Current.Gaming.CapFor(_scope) : null;

    private string ScopeName => IsGameScope ? (_page.FindGame(_scope)?.Name ?? ScopeCap?.Name ?? "this game") : "every game";

    /// <summary>The switch: on sets the limit (for every game, or for the chosen game), off removes it.</summary>
    public bool IsOn
    {
        get => _pending ?? (IsGameScope ? ScopeCap is not null : Status.FrameCap > 0);
        set
        {
            if (value != IsOn && CanToggle)
            {
                _pending = value;
                OnPropertyChanged(nameof(IsOn));
                if (IsGameScope)
                {
                    _ = _page.SetGameFramePacerAsync(_scope, value ? Target : 0, SelectedTarget);
                }
                else
                {
                    _ = _page.SetFramePacerAsync(value ? Target : 0);
                }
            }
        }
    }

    public bool CanToggle => IsSupported && !_isBusy && (!IsGameScope || _page.FindGame(_scope) is not null || ScopeCap is not null);

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

    /// <summary>The chosen limit (0 = automatic), for the scope shown.</summary>
    public int SelectedTarget
    {
        get => IsGameScope ? ScopeCap?.Choice ?? _gameChoice : _page.Store.Current.Gaming.FramePacerTarget;
        set
        {
            value = value <= 0 ? 0 : FrameCap.Clamp(value);
            if (value == SelectedTarget)
            {
                return;
            }

            if (IsGameScope)
            {
                _gameChoice = value;
                Update();
                if (ScopeCap is not null && CanToggle)
                {
                    _ = _page.SetGameFramePacerAsync(_scope, FrameCap.Resolve(value, RefreshHz), value);
                }

                return;
            }

            _page.Store.Update("Gaming", s => s.Gaming.FramePacerTarget = value);
            Update();
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
    public int Target => FrameCap.Resolve(SelectedTarget, RefreshHz);

    /// <summary>The limit set now for the scope shown (0 when off).</summary>
    private int ActiveLimit => IsGameScope ? ScopeCap?.Fps ?? 0 : Status.FrameCap;

    /// <summary>The big number: the limit in the driver when on, the limit it would use when off.</summary>
    public string LimitText => (IsOn && ActiveLimit > 0 ? ActiveLimit : Target).ToString(CultureInfo.CurrentCulture);

    public string FrameTimeText => FrameCap.FrameTimeText(IsOn && ActiveLimit > 0 ? ActiveLimit : Target) + " per frame";

    public string DisplayText => Status.RefreshRate > 0 ? $"{Status.RefreshRate.ToString(CultureInfo.CurrentCulture)} Hz display" : "Display refresh rate unknown";

    /// <summary>Why this limit.</summary>
    public string Explanation => SelectedTarget <= 0
        ? $"Just under your {RefreshHz.ToString(CultureInfo.CurrentCulture)} Hz refresh rate, so G-SYNC or FreeSync stays engaged and frames never queue up behind V-Sync. Even frame times, lower latency."
        : SelectedTarget >= Status.RefreshRate && Status.RefreshRate > 0
            ? "At or above the refresh rate: good for displays without variable refresh, with V-Sync on."
            : "A fixed limit: steadier frame times and less heat and noise when the game would otherwise swing above it.";

    /// <summary>What the switch covers: every game, or one game and what the others do.</summary>
    public string ScopeText
    {
        get
        {
            if (!IsGameScope)
            {
                int own = _page.Store.Current.Gaming.GameFrameCaps.Count;
                return own == 0
                    ? "Applies to every game. Pick a game above to give it a limit of its own."
                    : $"Applies to every game except the {own.ToString(CultureInfo.CurrentCulture)} with a limit of {(own == 1 ? "its" : "their")} own.";
            }

            string others = Status.FrameCap > 0 ? $"the all-games limit ({Status.FrameCap.ToString(CultureInfo.CurrentCulture)} FPS)" : "no limit";
            var cap = ScopeCap;
            return cap is not null
                ? $"Only {ScopeName} ({string.Join(", ", cap.Executables)}) is limited this way; other games keep {others}."
                : $"Sets a limit for {ScopeName} only, in the driver's profile for its program; other games keep {others}.";
        }
    }

    /// <summary>The setup that paces best with variable refresh (G-SYNC / FreeSync).</summary>
    public string TipText => "For the smoothest result with G-SYNC or FreeSync, turn V-Sync on in the NVIDIA Control Panel and off in games: the limit keeps latency low and V-Sync catches the rare frame that would tear.";

    /// <summary>The G-SYNC setup tip and how the limiter works (shown on hover, not as text on the card).</summary>
    public string HelpText => $"{TipText}\n\n{MethodText}";

    public string MethodText => IsGameScope
        ? "Uses the graphics driver's own frame limiter in the game's driver profile (NVIDIA's own profile when it has one), so nothing is added to the game and it's safe with anti-cheat. It applies from the game's next start."
        : "Uses the graphics driver's own frame limiter for every game, so nothing is added to games and it's safe with anti-cheat. It applies to games you start next.";

    /// <summary>The driver had a limit Coreloom didn't set (e.g. from the NVIDIA Control Panel).</summary>
    public string? ForeignLimitText => !IsGameScope && IsOn && Status.FrameCap != Target ? $"The driver's limit is {Status.FrameCap.ToString(CultureInfo.CurrentCulture)} FPS. Pick a limit to replace it." : null;

    /// <summary>Games with a limit of their own.</summary>
    public IReadOnlyList<GameCapChip> GameCaps
    {
        get => _gameCaps;
        private set
        {
            if (SetProperty(ref _gameCaps, value))
            {
                OnPropertyChanged(nameof(HasGameCaps));
            }
        }
    }

    public bool HasGameCaps => _gameCaps.Count > 0;

    /// <summary>What went wrong, or that the game needs a restart; leaves after five seconds.</summary>
    public string? Message
    {
        get => _message;
        internal set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }

            _messageDismissal.Update(!string.IsNullOrEmpty(value), () => Message = null);
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

    /// <summary>Re-reads everything after a change (status, settings, library); the switch's pending state ends.</summary>
    internal void Refresh()
    {
        if (!_isBusy)
        {
            _pending = null;
        }

        Update();
    }

    private void Update()
    {
        var options = new List<Option<int>> { new(0, $"Automatic · {FrameCap.AutoTarget(RefreshHz).ToString(CultureInfo.CurrentCulture)} FPS") };
        foreach (int fps in FrameCap.Presets(RefreshHz))
        {
            options.Add(new(fps, fps == Status.RefreshRate ? $"{fps.ToString(CultureInfo.CurrentCulture)} FPS · refresh rate" : $"{fps.ToString(CultureInfo.CurrentCulture)} FPS"));
        }

        if (SelectedTarget > 0 && !options.Any(o => o.Value == SelectedTarget))
        {
            options.Add(new(SelectedTarget, $"{SelectedTarget.ToString(CultureInfo.CurrentCulture)} FPS"));
        }

        if (Options.Count != options.Count || !Options.Zip(options).All(p => p.First == p.Second))
        {
            Options = options;
        }

        // Every library game (and any game with a limit that has since left the library, so it can still be removed).
        var caps = _page.Store.Current.Gaming.GameFrameCaps;
        var games = _page.LibraryGames
            .Select(g => (g.Id, g.Name))
            .Concat(caps.Select(c => (Id: c.GameId, c.Name)))
            .DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => GameLibrary.SortName(g.Name), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new Option<string>(g.Id, caps.Any(c => string.Equals(c.GameId, g.Id, StringComparison.OrdinalIgnoreCase)) ? $"{g.Name} · own limit" : g.Name));
        var scopes = new List<Option<string>> { new(AllGames, "All games") };
        scopes.AddRange(games);
        if (Scopes.Count != scopes.Count || !Scopes.Zip(scopes).All(p => p.First == p.Second))
        {
            Scopes = scopes;
        }

        if (IsGameScope && !scopes.Any(s => string.Equals(s.Value, _scope, StringComparison.OrdinalIgnoreCase)))
        {
            _scope = AllGames; // the game was uninstalled
        }

        var chips = caps
            .OrderBy(c => GameLibrary.SortName(c.Name), StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new GameCapChip(
                c.GameId,
                c.Name,
                c.Adaptive ? $"{c.Fps.ToString(CultureInfo.CurrentCulture)} FPS · adaptive" : $"{c.Fps.ToString(CultureInfo.CurrentCulture)} FPS",
                new RelayCommand(() => SelectedScope = c.GameId),
                new AsyncRelayCommand(() => _page.SetGameFramePacerAsync(c.GameId, 0, c.Choice))))
            .ToList();
        if (GameCaps.Count != chips.Count || !GameCaps.Zip(chips).All(p => p.First.GameId == p.Second.GameId && p.First.LimitText == p.Second.LimitText && p.First.Name == p.Second.Name))
        {
            GameCaps = chips;
        }

        OnPropertyChanged(nameof(SelectedScope));
        OnPropertyChanged(nameof(IsGameScope));
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(FrameTimeText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(MethodText));
        OnPropertyChanged(nameof(HelpText));
        OnPropertyChanged(nameof(ForeignLimitText));
        OnPropertyChanged(nameof(AdaptiveOn));
        OnPropertyChanged(nameof(CanToggleAdaptive));
        OnPropertyChanged(nameof(AdaptiveMethodText));
        OnPropertyChanged(nameof(ShowFrameGeneration));
        OnPropertyChanged(nameof(FrameGenerationLabel));
        OnPropertyChanged(nameof(SelectedFrameGeneration));
    }
}

/// <summary>
/// The Gaming page: boosts for Windows and the graphics card, the frame limiter, shader caches and the installed games
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
    private bool _lookingUpArtwork;
    private bool _scanAgain;
    private bool _isApplyingAll;
    private bool _statusBusy;
    private bool _statusAgain;
    private bool _runningBusy;
    private string _search = string.Empty;      // trimmed: what the list is filtered by
    private string _searchText = string.Empty;  // as typed (a trailing space stays while typing the next word)
    private DateTime _lastStatus = DateTime.MinValue;
    private DateTime _lastRunning = DateTime.MinValue;
    private RunningGame? _running;
    private bool _watching;
    private string? _heroMessage;
    private readonly Dismissal _heroDismissal = new();
    private readonly Dismissal _driverNoticeDismissal = new();
    private bool _driverNoticeShowing;

    public GamingViewModel(PageContext context)
        : base("gaming", "Gaming", "Icon.Controller", context)
    {
        _service = context.Tools.Gaming;
        Adaptive = new AdaptivePacingController(this);
        CoreScheduling = new CoreSchedulingController(this, context.Tools.CoreScheduling);
        FramePacer = new FramePacerViewModel(this);
        GamingTweakViewModel shaderCache = new(GamingTweak.ShaderCache, "NVIDIA shader cache", "Keeps the shaders games compile, so they aren't compiled again mid-play: fewer repeat stutters and steadier frame times the next times you play.", "Icon.Database", "NVIDIA", SetTweakAsync, "Frame stability",
            "Recommended: 10 GB when the current limit is smaller, or a larger size for a big game library.", "Doesn't remove every shader stutter or raise the average frame rate.");
        GamingTweakViewModel coreScheduling = new(GamingTweak.CoreScheduling, "Intelligent CPU core scheduling", "Keeps games on the cores that suit them, such as a Ryzen X3D's 3D V-Cache cores, after checking AMD's own scheduling.", "Icon.Cpu", "Ryzen X3D", SetTweakAsync, "Frame stability",
            "Recommended: on. A per-game profile is only offered after a test in that game shows a clear gain.",
            "Chooses cores, never clocks or voltages; nothing is added to games.");
        Boosts = IsMac
            ?
            [
                // macOS manages most of a game's performance itself; these are the switches it leaves to the user.
                new(GamingTweak.GameMode, "Game Mode", "macOS gives a full-screen game the first claim on the processor and GPU, and halves the Bluetooth latency of controllers and AirPods.", "Icon.Controller", "macOS 14+", SetTweakAsync, "Performance",
                    null, "macOS turns it on by itself when a game goes full screen; it can't be forced from an app."),
                new(GamingTweak.PowerMode, "Full power on the charger", "Turns Low Power Mode off on the charger and, on Macs with High Power Mode, turns that on: the chip keeps its highest clocks through long sessions.", "Icon.Power", null, SetTweakAsync, "Performance",
                    "Recommended: on for gaming sessions. macOS asks for your password to change it.", "The fans may run faster; it doesn't change the battery's own mode."),
                new(GamingTweak.KeepAwake, "Keep the Mac awake while playing", "The display doesn't dim or sleep while a library game is running, even when you play with a controller macOS doesn't count as input.", "Icon.Display", null, SetTweakAsync, "Comfort",
                    null, "Coreloom asks macOS to hold off sleep only while the game runs; nothing is changed in System Settings."),
                new(GamingTweak.MetalHud, "Metal Performance HUD", "Apple's own overlay in Metal games: frame rate, frame times, GPU time and memory. Games started after you turn it on show it.", "Icon.Pulse", "Apple", SetTweakAsync, "Insight",
                    null, "Coreloom doesn't measure games' frame rates on a Mac (that would mean adding code to them); Apple's HUD does."),
            ]
            :
            [
                new(GamingTweak.GpuScheduling, "Hardware-accelerated GPU scheduling", "The graphics card schedules its own work: lower latency and steadier frames. Takes effect after a restart.", "Icon.Gpu", "HAGS", SetTweakAsync, "Latency"),
                new(GamingTweak.UltimatePower, "Ultimate Performance power plan", "Keeps the processor at full speed with no power-saving delays. Turning it off goes back to your previous plan.", "Icon.Power", null, SetTweakAsync, "Performance"),
                new(GamingTweak.GameMode, "Game Mode", "Windows gives the game priority and holds back updates and notifications while you play.", "Icon.Controller", null, SetTweakAsync, "Performance"),
                new(GamingTweak.WindowedOptimizations, "Optimizations for windowed games", "Borderless and windowed games use the faster flip presentation: lower latency, variable refresh and Auto HDR.", "Icon.Window", "Windows 11", SetTweakAsync, "Latency"),
                new(GamingTweak.NoBackgroundRecording, "No background recording", "Stops Xbox Game Bar from recording the last minutes of play in the background, which costs frames.", "Icon.Camera", null, SetTweakAsync, "Performance"),
                new(GamingTweak.MaxGpuPerformance, "Maximum graphics card performance", "The driver's \"Prefer maximum performance\" mode: no downclocking between scenes, fewer frame-time spikes.", "Icon.Rocket", "NVIDIA", SetTweakAsync, "Frame stability"),
                new(GamingTweak.GSync, "G-SYNC & adaptive refresh", "Matches your display's refresh to the frames the graphics card sends, so there's no tearing and less stutter, in fullscreen and windowed games.", "Icon.Pulse", "NVIDIA", SetTweakAsync, "Smoothness",
                    GSyncSetup.Recommendation(0, 0), "Improves how frames are shown, not the frame rate itself."),
                shaderCache,
                coreScheduling,
            ];
        ShaderCache = shaderCache;
        CoreSchedulingBoost = coreScheduling;
        CoreSchedulingBoost.DetailsCommand = CoreScheduling.OpenDetailsCommand;
        CoreScheduling.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CoreSchedulingController.BoostNote) && !CoreSchedulingBoost.IsBusy && _status != GamingStatus.Unavailable)
            {
                CoreSchedulingBoost.Apply(CoreScheduling.Status); // a test's progress, or a result to keep, under the boost
            }
        };
        ShaderCacheSizes = [.. Coreloom.Core.Gaming.ShaderCacheSize.Choices.Select(size => new Option<uint>(size, Coreloom.Core.Gaming.ShaderCacheSize.Describe(size)))];

        RescanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && IsSupported);
        Editor = new GameEditorViewModel(this, _service);
        AddGameCommand = new RelayCommand(() => Editor.Add(), () => IsSupported);
        RefreshCommand = new AsyncRelayCommand(() => RefreshStatusAsync(force: true));
        ApplyAllCommand = new AsyncRelayCommand(ApplyAllAsync, () => IsSupported && !_isApplyingAll && CountedBoosts.Any(t => t.IsSupported && !t.IsOn && !t.IsBusy));
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        DismissDriverNoticeCommand = new RelayCommand(DismissDriverNotice);
        ShowListCommand = new RelayCommand(() => IsGridView = false);
        ShowGridCommand = new RelayCommand(() => IsGridView = true);
        FramePacer.Refresh();
    }

    /// <summary>For tests: the clock "played … ago" texts use.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>The card for adding, changing and removing games added by hand.</summary>
    public GameEditorViewModel Editor { get; }

    /// <summary>Picks a game's program and opens the card to name it.</summary>
    public IRelayCommand AddGameCommand { get; }

    internal SettingsService Store => Settings;

    /// <summary>Every game in the library (not just those the search shows).</summary>
    internal IEnumerable<GameViewModel> LibraryGames => _all;

    /// <summary>The library as last scanned (empty until the Gaming page has scanned once); safe to read on the UI thread.</summary>
    public IReadOnlyList<GameEntry> LibrarySnapshot() => [.. _all.Select(g => g.Game)];

    internal GameViewModel? FindGame(string id) => _all.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    internal GamingStatus CurrentStatus => _status;

    public bool IsSupported => _service.IsSupported;

    public string UnsupportedText => "Gaming tools read and change Windows and graphics-driver settings, which aren't available here.";

    /// <summary>
    /// Hardware-accelerated GPU scheduling, Ultimate Performance, Game Mode, windowed optimizations, background recording,
    /// GPU power mode, G-SYNC, the shader cache and intelligent CPU core scheduling.
    /// </summary>
    public IReadOnlyList<GamingTweakViewModel> Boosts { get; }

    /// <summary>The shader cache boost (also shown in the Shader cache card, with its size).</summary>
    public GamingTweakViewModel ShaderCache { get; }

    /// <summary>The sizes the shader cache boost can set: 10 GB, 100 GB, unlimited.</summary>
    public IReadOnlyList<Option<uint>> ShaderCacheSizes { get; }

    /// <summary>The size the shader cache boost sets; changing it while the boost is on applies it at once.</summary>
    public uint ShaderCacheSize
    {
        get => Coreloom.Core.Gaming.ShaderCacheSize.Normalize(Settings.Current.Gaming.ShaderCacheSize);
        set
        {
            uint size = Coreloom.Core.Gaming.ShaderCacheSize.Normalize(value);
            if (size == ShaderCacheSize)
            {
                return;
            }

            Settings.Update("Gaming", s => s.Gaming.ShaderCacheSize = size);
            OnPropertyChanged();
            if (ShaderCache.IsOn && !ShaderCache.IsBusy)
            {
                _ = SetTweakAsync(ShaderCache, true);
            }
        }
    }

    /// <summary>"Limit: 10 GB" (what the driver has now), or the driver's default.</summary>
    public string ShaderCacheLimitText => _status.ShaderCacheLimit is { } limit
        ? $"Limit now: {Coreloom.Core.Gaming.ShaderCacheSize.Describe(limit)}"
        : "Limit now: the driver's default";

    public FramePacerViewModel FramePacer { get; }

    /// <summary>Adaptive frame pacing: learns each game's steady frame rate while it plays and sets the game's limit.</summary>
    public AdaptivePacingController Adaptive { get; }

    /// <summary>Intelligent CPU core scheduling: the core map, AMD's checks, where the game runs, tests and per-game profiles.</summary>
    public CoreSchedulingController CoreScheduling { get; }

    /// <summary>The core scheduling boost (its Details open <see cref="CoreScheduling"/>'s card).</summary>
    public GamingTweakViewModel CoreSchedulingBoost { get; }

    /// <summary>Every switch on the page.</summary>
    private IEnumerable<GamingTweakViewModel> AllTweaks => Boosts;

    /// <summary>The switches counted as boosts (and turned on by "Boost now").</summary>
    private IEnumerable<GamingTweakViewModel> CountedBoosts => Boosts.Where(b => b.CountsAsBoost);

    public IAsyncRelayCommand RescanCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>"Boost now": turns on every boost that is off.</summary>
    public IAsyncRelayCommand ApplyAllCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }


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

    public int BoostsAvailable => CountedBoosts.Count(t => t.IsSupported);

    public int BoostsOn => CountedBoosts.Count(t => t.IsSupported && t.IsOn);

    /// <summary>0…1 for the readiness ring.</summary>
    public double Readiness => BoostsAvailable == 0 ? 0 : BoostsOn / (double)BoostsAvailable;

    public string ReadinessText => BoostsAvailable == 0 ? "—" : $"{BoostsOn.ToString(CultureInfo.CurrentCulture)}/{BoostsAvailable.ToString(CultureInfo.CurrentCulture)}";

    public string HeroTitle => !IsSupported ? "Gaming"
        : BoostsAvailable == 0 ? $"Checking your {Computer}…"
        : BoostsOn == BoostsAvailable ? $"Your {Computer} is tuned for games"
        : $"{(BoostsAvailable - BoostsOn).ToString(CultureInfo.CurrentCulture)} {(BoostsAvailable - BoostsOn == 1 ? "boost" : "boosts")} can make your games smoother";

    public string HeroDetail => IsMac
        ? BoostsOn == BoostsAvailable && BoostsAvailable > 0
            ? "macOS keeps the chip at full speed and the Mac awake while you play, and Apple's HUD shows the frame rate in Metal games."
            : "Turn them on one by one below, or all at once. Each switch shows what it changes, and you can turn it off again anytime."
        : RestartPending
        ? "Restart Windows to finish turning on hardware-accelerated GPU scheduling."
        : BoostsOn == BoostsAvailable && BoostsAvailable > 0
            ? "Windows and your graphics card are set up for the highest, steadiest frame rates."
            : "Turn them on one by one below, or all at once. Each switch shows what it changes, and you can turn it off again anytime.";

    public bool RestartPending => AllTweaks.Any(t => t.NeedsRestart);

    public bool AllBoostsOn => BoostsAvailable > 0 && BoostsOn == BoostsAvailable;

    public string ReadinessColorKey => AllBoostsOn ? ColorKeys.ToneCool : Readiness >= 0.5 ? ColorKeys.ToneNormal : ColorKeys.ToneWarm;

    /// <summary>What the last "Boost now" (or adding, removing or starting a game) did; leaves after five seconds.</summary>
    public string? HeroMessage
    {
        get => _heroMessage;
        private set
        {
            if (SetProperty(ref _heroMessage, value))
            {
                OnPropertyChanged(nameof(HasHeroMessage));
            }

            _heroDismissal.Update(!string.IsNullOrEmpty(value), () => HeroMessage = null);
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

    /// <summary>All the shader caches together ("1.24 GB"), or a dash when there are none yet.</summary>
    public string ShaderCacheTotalText
    {
        get
        {
            long total = _status.ShaderCaches.Where(c => c.Files > 0).Sum(c => c.Bytes);
            return total > 0 ? Formatter.Bytes(total).ToString() : "—";
        }
    }

    /// <summary>Shown when the driver changed since last time (caches start over).</summary>
    public string? DriverNotice => ShaderCachesDriverNotice();

    public bool HasDriverNotice => DriverNotice is not null;

    public string ShaderCacheNote => IsMac
        ? "Metal keeps the shaders each game compiles, so they aren't compiled again mid-play; macOS clears them itself when it needs the space. Coreloom doesn't pre-compile shaders by adding code to games: that's what anti-cheat systems ban for."
        : "Smart Optimize never clears these unless you tick \"Shader caches\" yourself. Coreloom doesn't pre-compile shaders by injecting into games: that's what anti-cheat systems ban for.";

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
        : IsMac
            ? "Games installed with Steam or Epic Games, and the games in your Applications folder, show up here."
            : "Games installed with Steam, Epic Games, GOG, Xbox, Ubisoft Connect, EA app, Battle.net or Riot show up here.";

    public string Search
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty) && _searchText.Trim() != _search)
            {
                _search = _searchText.Trim();
                ApplyLibrary();
            }
        }
    }

    /// <summary>The best few matching games, listed under the search box while typing.</summary>
    public IReadOnlyList<SearchSuggestion> SearchSuggestions =>
        Infrastructure.SearchSuggestions.Rank(
            _all, _search, g => g.Name, g => g.Matches(_search),
            g => new SearchSuggestion(g.Name, g.LastPlayedText.Length > 0 ? $"{g.Detail} · {g.LastPlayedText}" : g.Detail, "Icon.Controller"));

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

    /// <summary>Games without a cover or banner on this PC (looked up and downloaded automatically after each scan).</summary>
    public int MissingArtworkCount => _all.Count(g => g.CanDownloadPicture);

    /// <summary>Pictures are being downloaded (shown only when it takes a moment, not for a quick look-up).</summary>
    public bool IsDownloadingArtwork
    {
        get => _isDownloadingArtwork;
        private set => SetProperty(ref _isDownloadingArtwork, value);
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

    /// <summary>Loads the library and settings once (adaptive pacing needs them before the page has been opened).</summary>
    internal Task EnsureLoadedAsync() => IsLoaded || IsScanning ? Task.CompletedTask : LoadAsync();

    protected override void OnActivated()
    {
        DismissDriverNoticeLater();
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

    protected override void OnDeactivated()
    {
        Watch(false);
        DismissDriverNoticeLater(); // not on screen: it waits for the next visit
    }

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
            OnPropertyChanged(nameof(ShaderCacheTotalText));
            ApplyShaderCaches();
        }
    }

    // ---- Library -------------------------------------------------------------------------------------------

    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            _scanAgain = true; // e.g. a game was added during a scan: look again when it ends
            return;
        }

        IsScanning = true;
        try
        {
            var games = await _service.ScanLibraryAsync(CancellationToken.None);
            ApplyGames(GameLibrary.Merge(CustomEntries().Concat(games))); // a game added by hand wins over the same game found in a launcher
            if (MissingArtworkCount > 0)
            {
                _ = DownloadArtworkAsync(); // automatic since 1.4.4: games without a picture get one from their store or Steam
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

        if (_scanAgain)
        {
            _scanAgain = false;
            await ScanAsync();
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
        FramePacer.Refresh(); // the games it can be set for
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
        OnPropertyChanged(nameof(SearchSuggestions));
        OnPropertyChanged(nameof(MissingArtworkCount));
    }

    internal void Play(GameViewModel game)
    {
        if (!_service.Launch(game.Game))
        {
            HeroMessage = $"Couldn't start {game.Name}. Try starting it from {game.StoreName}.";
        }
    }

    /// <summary>The games added by hand, as library entries with their pictures.</summary>
    private IReadOnlyList<GameEntry> CustomEntries()
    {
        var entries = Settings.Current.Gaming.CustomGames.Select(CustomGames.ToEntry).ToList();
        return entries.Count == 0 ? entries : _service.AddLocalPictures(entries);
    }

    internal void EditGame(GameViewModel game)
    {
        if (Settings.Current.Gaming.CustomGames.FirstOrDefault(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase)) is { } custom)
        {
            Editor.Edit(custom);
        }
    }

    /// <summary>Saves a game added by hand (new or changed) and shows it in the library.</summary>
    internal async Task SaveCustomGameAsync(CustomGame game)
    {
        Settings.Update("Gaming", s =>
        {
            int index = s.Gaming.CustomGames.FindIndex(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                s.Gaming.CustomGames[index] = game;
            }
            else
            {
                s.Gaming.CustomGames.Add(game);
            }
        });
        HeroMessage = $"{game.Name} is in your games.";
        await ScanAsync();
    }

    /// <summary>Removes a game added by hand, and its own frame limit if it had one.</summary>
    internal async Task RemoveCustomGameAsync(CustomGame game)
    {
        if (Settings.Current.Gaming.CapFor(game.Id) is { } cap)
        {
            await SetGameFramePacerAsync(game.Id, 0, cap.Choice);
        }

        Settings.Update("Gaming", s => s.Gaming.CustomGames.RemoveAll(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase)));
        HeroMessage = $"Removed {game.Name} from your games. Its files weren't touched.";
        await ScanAsync();
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
        if (_lookingUpArtwork)
        {
            return;
        }

        _lookingUpArtwork = true;
        try
        {
            var missing = _all.Where(g => g.CanDownloadPicture).Select(g => g.Game).ToList();
            var download = _service.DownloadArtworkAsync(missing, CancellationToken.None);
            if (!download.IsCompleted && await Task.WhenAny(download, Task.Delay(400)) != download)
            {
                IsDownloadingArtwork = true; // games already looked up recently come back at once: no flicker
            }

            var updated = await download;
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
            _lookingUpArtwork = false;
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
            if (running is { Game.Store: GameStore.Custom } && running.Game.Id != _running?.Game.Id)
            {
                RememberPlayed(running.Game.Id); // launchers keep this for their games; Coreloom keeps it for games added by hand
            }

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

    private void RememberPlayed(string gameId)
    {
        var now = Now();
        Settings.Update("Gaming", s =>
        {
            if (s.Gaming.CustomGames.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase)) is { } game)
            {
                game.LastPlayed = now;
            }
        });
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
                tweak.Apply(tweak.Tweak == GamingTweak.CoreScheduling ? CoreScheduling.Status : status.Tweak(tweak.Tweak));
            }
        }

        CoreScheduling.Refresh();
        if (Boosts.FirstOrDefault(b => b.Tweak == GamingTweak.UltimatePower) is { } ultimate)
        {
            ultimate.Advice = CoreScheduling.ParkingMatters
                ? "On this Ryzen X3D, the Balanced plan lets AMD's driver park the frequency cores so games stay on the cache cores; Ultimate Performance stops core parking."
                : null;
        }

        if (Settings.Current.Gaming.LastDriverVersion is null && status.DriverVersion is not null)
        {
            Settings.Update("Gaming", s => s.Gaming.LastDriverVersion = status.DriverVersion); // first look: nothing to compare with
        }

        if (status.HasNvidiaDriver && Settings.Current.Gaming.GlobalFrameCap != status.FrameCap)
        {
            Settings.Update("Gaming", s => s.Gaming.GlobalFrameCap = status.FrameCap); // the overlay shows when games are paced
        }

        FramePacer.Refresh();
        if (Boosts.FirstOrDefault(b => b.Tweak == GamingTweak.GSync) is { } gsync)
        {
            gsync.Recommendation = GSyncSetup.Recommendation(status.FrameCap, status.RefreshRate);
        }
        ApplyShaderCaches();
        OnPropertyChanged(nameof(ShaderCacheLimitText));
        HeroChanged();
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DriverText));
        OnPropertyChanged(nameof(FrameLimitText));
        OnPropertyChanged(nameof(DriverNotice));
        OnPropertyChanged(nameof(HasDriverNotice));
        DismissDriverNoticeLater();
    }

    /// <summary>The driver notice leaves by itself five seconds after it's been on screen, as if dismissed.</summary>
    private void DismissDriverNoticeLater()
    {
        bool showing = HasDriverNotice && IsActive;
        if (showing != _driverNoticeShowing)
        {
            _driverNoticeShowing = showing;
            _driverNoticeDismissal.Update(showing, () =>
            {
                _driverNoticeShowing = false;
                DismissDriverNotice();
            });
        }
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
        OnPropertyChanged(nameof(ShaderCacheTotalText));
    }

    private string? ShaderCachesDriverNotice() => IsMac
        ? Coreloom.Core.Gaming.ShaderCaches.SystemChangeText(Settings.Current.Gaming.LastDriverVersion, _status.DriverVersion)
        : Coreloom.Core.Gaming.ShaderCaches.DriverChangeText(Settings.Current.Gaming.LastDriverVersion, _status.DriverVersion);

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
        if (tweak.Tweak == GamingTweak.CoreScheduling)
        {
            // Coreloom's own switch, not a Windows setting.
            tweak.IsBusy = true;
            var outcome = await CoreScheduling.SetEnabledAsync(on);
            tweak.IsBusy = false;
            tweak.Apply(CoreScheduling.Status);
            if (!outcome.Success)
            {
                tweak.ShowOutcome(outcome.Message);
            }

            HeroChanged();
            return;
        }

        tweak.IsBusy = true;
        tweak.ShowOutcome(null);
        TweakResult result;
        try
        {
            result = await _service.SetTweakAsync(tweak.Tweak, on, Settings.Current.Gaming, CancellationToken.None);
            Settings.Update("Gaming"); // the service may have noted the power plans to restore
        }
        catch (Exception e) when (IsExpected(e))
        {
            result = new TweakResult(false, $"{Platform.Name()} didn't accept the change.");
        }
        finally
        {
            tweak.IsBusy = false;
        }

        await RefreshStatusAsync(force: true);
        if (!result.Success)
        {
            tweak.ShowOutcome(result.Message);
        }
        else if (result.NeedsRestart || tweak.NeedsRestart)
        {
            tweak.ShowOutcome("Restart Windows to finish.");
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

    /// <summary>Sets (fps &gt; 0) or removes a game's own limit from the frame limiter card, with its busy state and message.</summary>
    internal async Task SetGameFramePacerAsync(string gameId, int fps, int choice)
    {
        string name = FindGame(gameId)?.Name ?? Settings.Current.Gaming.CapFor(gameId)?.Name ?? "This game";
        FramePacer.IsBusy = true;
        FramePacer.Message = null;
        TweakResult result;
        try
        {
            result = await ApplyGameCapAsync(gameId, fps, choice, adaptive: false);
        }
        finally
        {
            FramePacer.IsBusy = false;
        }

        FramePacer.Refresh();
        bool runningNow = _running is not null && string.Equals(_running.Game.Id, gameId, StringComparison.OrdinalIgnoreCase);
        FramePacer.Message = !result.Success ? result.Message
            : fps > 0 && runningNow ? $"Restart {name} to apply the new limit."
            : null;
    }

    /// <summary>
    /// Sets (fps &gt; 0) or removes a game's own limit: finds the game's programs, sets the limit in the driver's profile
    /// for them, and remembers it (so it shows, can be removed, and the overlay knows the game is paced).
    /// <paramref name="adaptive"/> marks a limit set by adaptive pacing (which changes only its own limits).
    /// </summary>
    internal async Task<TweakResult> ApplyGameCapAsync(string gameId, int fps, int choice, bool adaptive)
    {
        var game = FindGame(gameId);
        var existing = Settings.Current.Gaming.CapFor(gameId);
        string name = game?.Name ?? existing?.Name ?? "This game";
        TweakResult result;
        IReadOnlyList<string> programs = existing?.Executables ?? [];
        try
        {
            if (fps > 0 && game is not null)
            {
                var found = await _service.FindExecutablesAsync(game.Game, CancellationToken.None);
                programs = found.Concat(programs).Distinct(StringComparer.OrdinalIgnoreCase).Take(GameExecutables.Max).ToList();
            }

            result = await _service.SetGameFrameCapAsync(name, programs, fps, CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            result = new TweakResult(false, "The graphics driver didn't accept the change.");
        }

        if (result.Success)
        {
            int limit = FrameCap.Clamp(fps);
            var saved = programs.ToList();
            Settings.Update("Gaming", s =>
            {
                s.Gaming.GameFrameCaps.RemoveAll(c => string.Equals(c.GameId, gameId, StringComparison.OrdinalIgnoreCase));
                if (limit > 0)
                {
                    s.Gaming.GameFrameCaps.Add(new GameFrameCap { GameId = gameId, Name = name, Choice = choice, Fps = limit, Executables = saved, Adaptive = adaptive });
                }
            });
        }

        FramePacer.Refresh();
        return result;
    }

    private async Task ApplyAllAsync()
    {
        _isApplyingAll = true;
        ApplyAllCommand.NotifyCanExecuteChanged();
        try
        {
            int changed = 0;
            foreach (var tweak in CountedBoosts.Where(t => t.IsSupported && !t.IsOn).ToList())
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
