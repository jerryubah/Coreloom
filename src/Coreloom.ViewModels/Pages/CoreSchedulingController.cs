using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>What the app hands core scheduling about once a second: the game in front, the load on each logical processor and, during a test, the game's newest second of frames.</summary>
/// <param name="ProcessId">The game in front (null when none).</param>
/// <param name="ProgramPath">Its program file.</param>
/// <param name="ThreadLoads">Load per logical processor (0–100), or null.</param>
/// <param name="Second">The game's newest second of frames (only measured during a test).</param>
/// <param name="FrameProblem">Why frames can't be measured, or null.</param>
public readonly record struct CoreTick(int? ProcessId, string? ProgramPath, IReadOnlyList<double>? ThreadLoads, PacingSecond? Second = null, string? FrameProblem = null);

/// <summary>A core group on the card: its name, size and share of the game's load.</summary>
public sealed class CoreGroupViewModel : ObservableObject
{
    private double _share;
    private string _shareText = "—";

    public CoreGroupViewModel(CoreGroup group, bool isPreferred)
    {
        Group = group;
        IsPreferred = isPreferred;
    }

    public CoreGroup Group { get; }

    public string Name => Group.Name;

    public string Description => Group.Description;

    /// <summary>The cores games are best kept on (shown in the accent).</summary>
    public bool IsPreferred { get; }

    public string ColorKey => IsPreferred ? ColorKeys.Accent : ColorKeys.Neutral;

    /// <summary>Share of the load while the game is in front (0–1).</summary>
    public double Share
    {
        get => _share;
        private set => SetProperty(ref _share, value);
    }

    public string ShareText
    {
        get => _shareText;
        private set => SetProperty(ref _shareText, value);
    }

    internal void Show(double? share)
    {
        Share = share ?? 0;
        ShareText = share is { } s ? CoreScheduling.Percent(s) : "—";
    }
}

/// <summary>A check on the card: what core scheduling depends on, and how it is.</summary>
public sealed record CoreCheckRow(string Name, string Status, string ColorKey, string Detail) : StaticRecord;

/// <summary>A game kept on chosen cores, as a chip with × to remove it.</summary>
public sealed record CoreProfileChip(string Program, string Name, string Text, IRelayCommand RemoveCommand) : StaticRecord;

/// <summary>
/// Intelligent CPU core scheduling on the Gaming page. Once the user turns it on it works out which cores suit games
/// (the 3D V-Cache CCD on a multi-CCD Ryzen X3D, the performance cores on a hybrid processor), watches where the game in
/// front runs, and checks what AMD's own scheduling depends on. A per-game core profile is only offered after an A/B
/// test the user starts shows a clear gain, and only kept when the user keeps it. It never conflicts with AMD's driver:
/// no profile for a game AMD has its own preference for, none for a game whose cores were chosen elsewhere, none when
/// AMD's driver already keeps the game on the cache cores, and never the frequency cores. Turning it off gives every
/// game it changed Windows' choice back. Each change is written to the log.
/// </summary>
public sealed class CoreSchedulingController : ObservableObject
{
    private const string LogCategory = "Gaming";

    /// <summary>Readings to wait for before deciding about a profile while AMD's driver is active (it needs a moment to move the game).</summary>
    internal const int PlacementWait = 10;

    private readonly GamingViewModel _page;
    private readonly ICoreSchedulingService _service;
    private readonly Dictionary<int, ulong> _changed = [];
    private CoreLayout? _layout;
    private CorePlacement? _placement;
    private AmdVCacheStatus _amd = AmdVCacheStatus.None;
    private int? _pid;
    private string? _program;
    private string? _gameName;
    private bool _inFront;
    private bool _decided;
    private bool _armed;
    private AffinityTrial? _trial;
    private int _trialPid;
    private string _trialProgram = string.Empty;
    private string _trialName = string.Empty;
    private ulong _trialSystem;
    private (int Pid, string Program, string Name, CoreKind Cores, AffinityTrialResult Result)? _pending;
    private string _statusText = "Off";
    private string _statusColorKey = ColorKeys.Neutral;
    private string? _hint;
    private string? _lastActionText;
    private string _testText = string.Empty;
    private bool _isBusy;

    public CoreSchedulingController(GamingViewModel page, ICoreSchedulingService service)
    {
        _page = page;
        _service = service;
        TestCommand = new RelayCommand(Arm, () => CanTest);
        CancelTestCommand = new RelayCommand(CancelTest, () => IsTesting);
        KeepCommand = new RelayCommand(Keep, () => _pending is not null);
        DiscardCommand = new RelayCommand(Discard, () => _pending is not null);
        _testText = IdleTestText;
    }

    /// <summary>The cores, grouped (read once).</summary>
    public CoreLayout Layout
    {
        get
        {
            if (_layout is null)
            {
                try
                {
                    _layout = _service.IsSupported ? _service.ReadLayout() : CoreLayout.Unknown;
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    Log.Warn(LogCategory, "Reading the processor's core layout failed.", e);
                    _layout = CoreLayout.Unknown;
                }

                _placement = new CorePlacement(_layout);
                Groups.Clear();
                var preferred = _layout.Preferred;
                foreach (var group in _layout.Groups)
                {
                    Groups.Add(new CoreGroupViewModel(group, group == preferred));
                }
            }

            return _layout;
        }
    }

    public AmdVCacheStatus Amd => _amd;

    /// <summary>There are cores to choose between, and Windows lets Coreloom read them.</summary>
    public bool IsSupported => _service.IsSupported && Layout.HasChoice;

    public bool IsOn => _page.Store.Current.Gaming.CoreScheduling;

    /// <summary>The boost's state for the Performance boosts list.</summary>
    public TweakStatus Status => IsSupported ? new TweakStatus(true, IsOn) : TweakStatus.Unsupported(UnsupportedNote);

    /// <summary>Why there's nothing to schedule here.</summary>
    public string UnsupportedNote => !_service.IsSupported || !Layout.IsKnown
        ? "The processor's cores couldn't be read here."
        : Layout.HasVCacheEverywhere
            ? "Every core on this processor has the 3D V-Cache, so there's nothing to choose between."
            : "All cores on this processor are alike, so there's nothing to choose between.";

    /// <summary>"2 CCDs · 3D V-Cache on one".</summary>
    public string LayoutText => Layout.Summary;

    /// <summary>A badge for the card ("Ryzen X3D", "Hybrid", "Multi-CCD").</summary>
    public string BadgeText => Layout.IsVCache ? "Ryzen X3D" : Layout.IsHybrid ? "Hybrid" : Layout.HasChoice ? "Multi-CCD" : "One CCD";

    /// <summary>On a multi-CCD Ryzen X3D with AMD's driver, Ultimate Performance stops the core parking that driver relies on.</summary>
    public bool ParkingMatters => Layout.IsVCache && _amd.Driver != ServiceState.Missing;

    public ObservableCollection<CoreGroupViewModel> Groups { get; } = [];

    public ObservableCollection<CoreCheckRow> Checks { get; } = [];

    public bool HasChecks => Checks.Count > 0;

    public ObservableCollection<CoreProfileChip> Profiles { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Turning on or off.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>What it sees now ("Cyberpunk 2077: 84 % of the load on the cache cores").</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Green when the game is on the right cores, orange when split, red when mostly elsewhere.</summary>
    public string StatusColorKey
    {
        get => _statusColorKey;
        private set => SetProperty(ref _statusColorKey, value);
    }

    /// <summary>What to do when a game isn't on the right cores.</summary>
    public string? Hint
    {
        get => _hint;
        private set
        {
            if (SetProperty(ref _hint, value))
            {
                OnPropertyChanged(nameof(HasHint));
            }
        }
    }

    public bool HasHint => !string.IsNullOrEmpty(_hint);

    /// <summary>The last profile applied, skipped or tested, with why.</summary>
    public string? LastActionText
    {
        get => _lastActionText;
        private set
        {
            if (SetProperty(ref _lastActionText, value))
            {
                OnPropertyChanged(nameof(HasLastAction));
            }
        }
    }

    public bool HasLastAction => !string.IsNullOrEmpty(_lastActionText);

    /// <summary>Where the test is ("Testing Cyberpunk 2077 · Round 2 of 4 · cache cores · 18 s left").</summary>
    public string TestText
    {
        get => _testText;
        private set => SetProperty(ref _testText, value);
    }

    /// <summary>A test is waiting for a game, or running.</summary>
    public bool IsTesting => _armed || _trial is not null;

    /// <summary>A test can start: on, cores to choose between that Coreloom can set, and no test or result waiting.</summary>
    public bool CanTest => IsOn && IsSupported && Layout.CanSetAffinity && !IsTesting && _pending is null;

    /// <summary>Frames are needed (a test is waiting or running).</summary>
    public bool WantsFrames => IsOn && IsTesting;

    /// <summary>"Cyberpunk 2077 on the cache cores: +6 % FPS, +4 % 1% lows. Keep this as its profile?"</summary>
    public string? PendingText => _pending is { } p
        ? $"{p.Name} on the {CoresName(p.Cores)}: {p.Result.GainText}. Keep this as its profile?"
        : null;

    public bool HasPending => _pending is not null;

    /// <summary>Starts a test in the next game in front.</summary>
    public IRelayCommand TestCommand { get; }

    public IRelayCommand CancelTestCommand { get; }

    /// <summary>Keeps the tested cores as the game's profile.</summary>
    public IRelayCommand KeepCommand { get; }

    public IRelayCommand DiscardCommand { get; }

    /// <summary>The game being watched now (for tests).</summary>
    internal string? GameName => _gameName;

    internal AffinityTrial? Trial => _trial;

    private string IdleTestText => Layout.Preferred is { } preferred
        ? $"Plays your next game on Windows' choice and on the {preferred.Name.ToLowerInvariant()}, 30 s at a time for 2 minutes, and offers a profile only when the {preferred.Name.ToLowerInvariant()} are clearly better."
        : string.Empty;

    private string CoresName(CoreKind kind) => (Layout.Group(kind)?.Name ?? kind.ToString()).ToLowerInvariant();

    // ---- Switching -----------------------------------------------------------------------------------------

    /// <summary>Turns core scheduling on, or off (every game it changed gets Windows' choice back).</summary>
    public Task<TweakResult> SetEnabledAsync(bool on)
    {
        if (!IsSupported)
        {
            return Task.FromResult(new TweakResult(false, UnsupportedNote));
        }

        if (on == IsOn)
        {
            return Task.FromResult(new TweakResult(true, string.Empty));
        }

        IsBusy = true;
        try
        {
            _page.Store.Update("Gaming", s => s.Gaming.CoreScheduling = on);
            Log.Info(LogCategory, on ? $"Intelligent CPU core scheduling turned on ({Layout.Summary})." : "Intelligent CPU core scheduling turned off.");
            if (!on)
            {
                int restored = RestoreAll();
                LastActionText = restored > 0 ? $"Gave {(restored == 1 ? "1 game" : $"{restored.ToString(CultureInfo.CurrentCulture)} games")} Windows' choice of cores back." : null;
            }

            Refresh();
            return Task.FromResult(new TweakResult(true, on ? "Watching which cores your games use." : "Off."));
        }
        finally
        {
            IsBusy = false;
            Changed();
        }
    }

    /// <summary>
    /// Gives every game Coreloom changed Windows' choice of cores back and stops a test (switching off, or Coreloom
    /// closing). Returns how many games were changed back.
    /// </summary>
    public int RestoreAll()
    {
        if (_trial is not null)
        {
            Log.Info(LogCategory, $"Core scheduling: stopped testing {_trialName}.");
        }

        _trial = null;
        _armed = false;
        _pending = null;
        int restored = 0;
        foreach (var (pid, system) in _changed.ToList())
        {
            if (_service.SetAffinity(pid, system))
            {
                restored++;
            }
        }

        if (restored > 0)
        {
            Log.Info(LogCategory, $"Core scheduling: gave {restored} game(s) Windows' choice of cores back.");
        }

        _changed.Clear();
        End();
        TestText = IdleTestText;
        return restored;
    }

    /// <summary>Reads AMD's driver again and redoes the checks (on each status refresh of the page, and when a game starts).</summary>
    public void Refresh()
    {
        if (!_service.IsSupported || !Layout.HasChoice)
        {
            Changed();
            return;
        }

        try
        {
            _amd = Layout.IsVCache ? _service.ReadAmdStatus() : AmdVCacheStatus.None;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.WarnThrottled("core-scheduling-amd", TimeSpan.FromMinutes(30), LogCategory, "Reading AMD's 3D V-Cache driver failed.", e);
        }

        var status = _page.CurrentStatus;
        bool gameMode = _page.Boosts.FirstOrDefault(b => b.Tweak == GamingTweak.GameMode)?.IsOn ?? false;
        var rows = CoreScheduling.Checks(Layout, _amd, gameMode, status.ActivePowerPlan)
            .Select(c => new CoreCheckRow(c.Name, c.Status, c.State switch
            {
                CheckState.Good => ColorKeys.ToneCool,
                CheckState.Warn => ColorKeys.ToneWarm,
                CheckState.Bad => ColorKeys.ToneHot,
                _ => ColorKeys.Neutral,
            }, c.Detail))
            .ToList();
        if (Checks.Count != rows.Count || !Checks.Zip(rows).All(p => p.First == p.Second))
        {
            Checks.Clear();
            foreach (var row in rows)
            {
                Checks.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasChecks));
        OnPropertyChanged(nameof(ParkingMatters));
        Changed();
    }

    // ---- Once a second -------------------------------------------------------------------------------------

    /// <summary>Called about once a second by the app while core scheduling is on (and once more when it goes off).</summary>
    public void Tick(CoreTick tick)
    {
        if (!IsOn || !IsSupported)
        {
            if (_pid is not null || IsTesting || _changed.Count > 0)
            {
                RestoreAll();
            }

            Changed();
            return;
        }

        if (tick.ProcessId is int pid && pid != _pid)
        {
            Begin(pid, tick.ProgramPath);
        }
        else if (tick.ProcessId is null && _pid is int was && _service.GetAffinity(was) is null)
        {
            if (_trial is not null && _trialPid == was)
            {
                StopTrial($"{_trialName} closed before the test finished; nothing was kept.");
            }

            _changed.Remove(was);
            End();
        }

        _inFront = tick.ProcessId is not null && tick.ProcessId == _pid;

        if (_inFront && tick.ThreadLoads is { } loads)
        {
            _placement?.Add(loads);
        }

        if (_inFront && !_decided)
        {
            DecideProfile();
        }

        if (_inFront && _armed && _trial is null && _pid is int game)
        {
            StartTrial(game);
        }

        if (_trial is { } trial && _inFront && tick.ProcessId == _trialPid)
        {
            if (tick.FrameProblem is { Length: > 0 } problem)
            {
                TestText = $"Testing {_trialName} · waiting: frame times can't be measured ({problem})";
            }
            else if (tick.Second is { } second)
            {
                if (trial.Add(second) && !SetCores(trial.OnCandidate))
                {
                    StopTrial($"Windows refused to change {_trialName}'s cores (its anti-cheat may block it); the test stopped and Windows' choice stays.");
                }
                else if (trial.IsDone)
                {
                    FinishTrial();
                }
                else
                {
                    TestText = $"Testing {_trialName} · {trial.ProgressText}";
                }
            }
        }
        else if (_trial is not null)
        {
            TestText = $"Testing {_trialName} · paused while it isn't in front";
        }

        ShowPlacement();
        Changed();
    }

    private void Begin(int pid, string? path)
    {
        End();
        foreach (int ended in _changed.Keys.Where(p => p != pid && _service.GetAffinity(p) is null).ToList())
        {
            _changed.Remove(ended); // games that closed: nothing to give back
        }

        _pid = pid;
        _program = GameExecutables.FileName(path) ?? $"process {pid.ToString(CultureInfo.InvariantCulture)}";
        _gameName = (path is null ? null : GameLibrary.FindRunning(_page.LibrarySnapshot(), [new RunningProcess(pid, path)])?.Game.Name)
            ?? Path.GetFileNameWithoutExtension(_program);
        _decided = false;
        _placement?.Reset();
        Refresh(); // AMD's per-program preferences may have changed
    }

    private void End()
    {
        _pid = null;
        _program = null;
        _gameName = null;
        _inFront = false;
        _decided = false;
        _placement?.Reset();
        Hint = null;
    }

    /// <summary>Applies the game's profile, unless something else already handles its cores.</summary>
    private void DecideProfile()
    {
        if (_pid is not int pid || _program is null || _trial is not null)
        {
            return; // (not during a test: it sets the cores itself)
        }

        string name = _gameName ?? _program;
        var profile = CoreProfile.For(_page.Store.Current.Gaming.CoreProfiles, _program);
        if (profile is null)
        {
            _decided = true;
            return;
        }

        var group = Layout.Group(profile.Cores);
        if (group is null || !Layout.CanSetAffinity || profile.Cores is CoreKind.Frequency or CoreKind.Efficiency)
        {
            Skip($"{name}'s profile doesn't fit this processor's cores, so Windows chooses.");
            return;
        }

        if (_amd.For(_program) is { } preference)
        {
            Skip($"AMD's driver has its own preference for {name} ({CoresName(preference.Prefers)}); Coreloom leaves its cores to AMD.");
            return;
        }

        if (Layout.IsVCache && _amd.IsActive)
        {
            if ((_placement?.Samples ?? 0) < PlacementWait)
            {
                return; // give AMD's driver a moment to move the game, then look
            }

            if (_placement!.AverageLoad >= CoreScheduling.IdleLoad && _placement.Share(group) >= CoreScheduling.AlreadyPlacedShare)
            {
                Skip($"AMD's driver already keeps {name} on the {group.Name.ToLowerInvariant()}; its profile isn't needed now.");
                return;
            }
        }

        var affinity = _service.GetAffinity(pid);
        if (affinity is not { } current)
        {
            Skip($"Windows refused to change {name}'s cores (its anti-cheat may block it); Windows' choice stays.");
            return;
        }

        if (current.IsCustom && !_changed.ContainsKey(pid))
        {
            Skip($"{name} already runs on cores chosen elsewhere (Task Manager, Process Lasso or the game itself); Coreloom leaves them.");
            return;
        }

        if (_service.SetAffinity(pid, group.Mask & current.System))
        {
            _changed[pid] = current.System;
            LastActionText = $"Kept {name} on the {group.Name.ToLowerInvariant()} (its profile: {CoreScheduling.Gain(profile.FpsGain)} FPS, {CoreScheduling.Gain(profile.LowGain)} 1% lows).";
            Log.Info(LogCategory, $"Core scheduling: {name} ({_program}) set to the {group.Name.ToLowerInvariant()} by its profile.");
        }
        else
        {
            LastActionText = $"Windows refused to change {name}'s cores (its anti-cheat may block it); Windows' choice stays.";
            Log.Warn(LogCategory, $"Core scheduling: couldn't set {name}'s cores for its profile.");
        }

        _decided = true;
    }

    private void Skip(string why)
    {
        _decided = true;
        LastActionText = why;
        Log.Info(LogCategory, $"Core scheduling: {why}");
    }

    private void ShowPlacement()
    {
        var shares = _placement?.Shares;
        bool measured = _placement is { Samples: >= CorePlacement.MinimumSamples };
        for (int i = 0; i < Groups.Count; i++)
        {
            Groups[i].Show(measured && shares is not null && i < shares.Count ? shares[i] : null);
        }

        if (!IsOn)
        {
            StatusText = "Off";
            StatusColorKey = ColorKeys.Neutral;
            Hint = null;
            return;
        }

        if (_pid is null || _placement is null)
        {
            StatusText = "On · waiting for a game";
            StatusColorKey = ColorKeys.Neutral;
            Hint = null;
            return;
        }

        string name = _gameName ?? _program ?? "The game";
        if (!_inFront)
        {
            StatusText = $"{name} is in the background";
            StatusColorKey = ColorKeys.Neutral;
            return;
        }

        var verdict = CoreScheduling.Judge(Layout, _placement, name);
        StatusText = verdict.Text;
        StatusColorKey = verdict.State switch
        {
            PlacementState.Good => ColorKeys.ToneCool,
            PlacementState.Mixed => ColorKeys.ToneWarm,
            PlacementState.Off => ColorKeys.ToneHot,
            _ => ColorKeys.Neutral,
        };
        Hint = verdict.State is PlacementState.Off or PlacementState.Mixed && Layout.IsVCache && !_changed.ContainsKey(_pid.Value)
            ? _amd.IsActive
                ? "AMD's driver isn't keeping this game on the cache cores. Check Game Mode is on and that Game Bar (Win+G) remembers it as a game, or test a profile."
                : "Without AMD's 3D V-Cache driver nothing moves games to the cache cores. Install AMD's chipset driver, or test a profile."
            : null;
    }

    // ---- The A/B test --------------------------------------------------------------------------------------

    private void Arm()
    {
        if (!CanTest)
        {
            return;
        }

        _armed = true;
        TestText = "Ready: switch to your game and play normally for about 2 minutes. Coreloom changes its cores every 30 s.";
        Log.Info(LogCategory, "Core scheduling: a test will start in the next game in front.");
        Changed();
    }

    private void StartTrial(int pid)
    {
        _armed = false;
        string name = _gameName ?? _program ?? "the game";
        if (Layout.Preferred is not { } candidate || !Layout.CanSetAffinity)
        {
            StopTrial("This processor's cores can't be tested.");
            return;
        }

        if (_amd.For(_program) is { } preference)
        {
            StopTrial($"AMD's driver has its own preference for {name} ({CoresName(preference.Prefers)}); Coreloom doesn't test over it.");
            return;
        }

        if (_service.GetAffinity(pid) is not { } affinity)
        {
            StopTrial($"Windows refused to change {name}'s cores (its anti-cheat may block it), so it can't be tested.");
            return;
        }

        if (affinity.IsCustom && !_changed.ContainsKey(pid))
        {
            StopTrial($"{name} already runs on cores chosen elsewhere (Task Manager, Process Lasso or the game itself); Coreloom doesn't test over them.");
            return;
        }

        _trial = new AffinityTrial(candidate);
        _trialPid = pid;
        _trialProgram = _program!;
        _trialName = name;
        _trialSystem = affinity.System;
        _changed[pid] = affinity.System;
        if (affinity.Process != affinity.System && !_service.SetAffinity(pid, affinity.System))
        {
            StopTrial($"Windows refused to change {name}'s cores (its anti-cheat may block it), so it can't be tested.");
            return;
        }

        TestText = $"Testing {name} · {_trial.ProgressText}";
        Log.Info(LogCategory, $"Core scheduling: testing {name} ({_trialProgram}) on Windows' choice against the {candidate.Name.ToLowerInvariant()}.");
    }

    private bool SetCores(bool candidate)
    {
        ulong mask = candidate ? _trial!.Candidate.Mask & _trialSystem : _trialSystem;
        return _service.SetAffinity(_trialPid, mask);
    }

    private void FinishTrial()
    {
        var trial = _trial!;
        var result = trial.Result();
        _service.SetAffinity(_trialPid, _trialSystem); // back to Windows' choice until the user keeps it
        _changed.Remove(_trialPid);
        _trial = null;
        string cores = trial.Candidate.Name.ToLowerInvariant();
        Log.Info(LogCategory, $"Core scheduling: {_trialName} on the {cores}: {result.GainText} ({result.FpsWindows:0.#} → {result.FpsCandidate:0.#} FPS, 1% lows {result.LowWindows:0.#} → {result.LowCandidate:0.#}); {(result.Proven ? "a clear gain" : "no clear gain")}.");
        if (result.Proven)
        {
            _pending = (_trialPid, _trialProgram, _trialName, trial.Candidate.Kind, result);
            TestText = $"Tested {_trialName}: a clear gain on the {cores}.";
        }
        else
        {
            bool hadProfile = CoreProfile.For(_page.Store.Current.Gaming.CoreProfiles, _trialProgram) is not null;
            if (hadProfile)
            {
                RemoveProfile(_trialProgram, restore: false);
            }

            TestText = IdleTestText;
            LastActionText = $"{_trialName}: no clear gain on the {cores} ({result.GainText}), so Windows' choice stays{(hadProfile ? " and its old profile was removed" : string.Empty)}.";
            if (_pid == _trialPid)
            {
                _decided = true;
            }
        }

        ProfilesChanged();
    }

    private void StopTrial(string why)
    {
        if (_trial is not null)
        {
            _service.SetAffinity(_trialPid, _trialSystem);
            _changed.Remove(_trialPid);
        }

        _trial = null;
        _armed = false;
        TestText = IdleTestText;
        LastActionText = why;
        Log.Info(LogCategory, $"Core scheduling: {why}");
        Changed();
    }

    private void CancelTest()
    {
        if (_trial is not null)
        {
            StopTrial($"Stopped testing {_trialName}; Windows' choice stays.");
        }
        else
        {
            _armed = false;
            TestText = IdleTestText;
            Changed();
        }
    }

    private void Keep()
    {
        if (_pending is not { } p)
        {
            return;
        }

        _pending = null;
        _page.Store.Update("Gaming", s =>
        {
            s.Gaming.CoreProfiles.RemoveAll(x => string.Equals(x.Program, p.Program, StringComparison.OrdinalIgnoreCase));
            s.Gaming.CoreProfiles.Add(new CoreProfile
            {
                Program = p.Program,
                Name = p.Name,
                Cores = p.Cores,
                FpsGain = p.Result.FpsGain,
                LowGain = p.Result.LowGain,
                Tested = DateTime.UtcNow,
            });
        });
        Log.Info(LogCategory, $"Core scheduling: kept a profile for {p.Name} ({p.Program}): the {CoresName(p.Cores)}, {p.Result.GainText}.");
        LastActionText = $"Kept: {p.Name} runs on the {CoresName(p.Cores)} from now on, while core scheduling is on.";
        TestText = IdleTestText;

        // The game may still be running: apply it now (the usual checks run when it's next in front).
        if (_pid == p.Pid)
        {
            _decided = false;
        }

        ProfilesChanged();
    }

    private void Discard()
    {
        if (_pending is not { } p)
        {
            return;
        }

        _pending = null;
        LastActionText = $"Not kept; Windows chooses {p.Name}'s cores.";
        TestText = IdleTestText;
        Changed();
    }

    private void RemoveProfile(string program, bool restore)
    {
        _page.Store.Update("Gaming", s => s.Gaming.CoreProfiles.RemoveAll(x => string.Equals(x.Program, program, StringComparison.OrdinalIgnoreCase)));
        if (restore && _pid is int pid && string.Equals(_program, program, StringComparison.OrdinalIgnoreCase) && _changed.Remove(pid, out ulong system))
        {
            _service.SetAffinity(pid, system);
            LastActionText = $"Removed {_gameName}'s profile; Windows chooses its cores again.";
        }

        Log.Info(LogCategory, $"Core scheduling: removed the profile for {program}.");
        ProfilesChanged();
    }

    private void ProfilesChanged()
    {
        var chips = _page.Store.Current.Gaming.CoreProfiles
            .Select(p => new CoreProfileChip(p.Program, p.Name, $"{CoresName(p.Cores)} · {CoreScheduling.Gain(p.FpsGain)} FPS",
                new RelayCommand(() => RemoveProfile(p.Program, restore: true))))
            .ToList();
        if (Profiles.Count != chips.Count || !Profiles.Zip(chips).All(x => x.First.Program == x.Second.Program && x.First.Text == x.Second.Text))
        {
            Profiles.Clear();
            foreach (var chip in chips)
            {
                Profiles.Add(chip);
            }
        }

        OnPropertyChanged(nameof(HasProfiles));
        Changed();
    }

    private void Changed()
    {
        if (Profiles.Count == 0 && _page.Store.Current.Gaming.CoreProfiles.Count > 0)
        {
            ProfilesChanged();
            return;
        }

        if (!IsOn)
        {
            StatusText = "Off";
            StatusColorKey = ColorKeys.Neutral;
            Hint = null;
        }
        else if (_pid is null && StatusText == "Off")
        {
            StatusText = "On · waiting for a game";
        }

        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsTesting));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(PendingText));
        OnPropertyChanged(nameof(HasPending));
        TestCommand.NotifyCanExecuteChanged();
        CancelTestCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }
}
