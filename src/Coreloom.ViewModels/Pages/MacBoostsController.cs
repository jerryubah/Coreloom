using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>What the Mac app hands the boosts every two seconds: the library game running (if any) and whether a game is in front.</summary>
/// <param name="Game">The library game running now, or null.</param>
/// <param name="ProcessId">One of its processes.</param>
/// <param name="GameInFront">A game (a library game or an app filed under Games) is the app in front.</param>
public readonly record struct MacBoostTick(GameEntry? Game, int? ProcessId, bool GameInFront);

/// <summary>
/// The Mac's eight Gaming boosts added in 1.9.4, Coreloom's own switches (nothing in System Settings changes, except the
/// charger's power mode while High Power Mode automation is on): High Power Mode automation, the unified memory pressure
/// guard, the thermal headroom optimizer, the performance core workload monitor, the MetalFX graphics optimizer, the
/// ProMotion and display pipeline optimizer, the native Apple silicon check and the background application governor.
/// Each one reads only while it's on (or its Details are open); every finding is in its Details and the important ones on
/// its card. Nothing is added to games.
/// </summary>
public sealed class MacBoostsController : ObservableObject
{
    private const string LogCategory = "Gaming";

    public static readonly GamingTweak[] Tweaks =
    [
        GamingTweak.HighPowerAutomation, GamingTweak.MemoryGuard, GamingTweak.ThermalHeadroom, GamingTweak.CoreWorkload,
        GamingTweak.MetalFx, GamingTweak.DisplayPipeline, GamingTweak.NativeApps, GamingTweak.BackgroundGovernor,
    ];

    private readonly GamingViewModel _page;
    private readonly IMacPerformanceService _service;
    private readonly Func<TelemetryFrame> _frame;
    private readonly WorkloadDetector _workload = new();
    private readonly MemoryGuard _memoryGuard = new();
    private readonly ThermalTracker _thermal = new();
    private readonly CoreWorkloadMonitor _cores = new();
    private readonly Dictionary<string, MetalFxFeatures> _metalFx = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CpuArchitecture> _architectures = new(StringComparer.OrdinalIgnoreCase);
    private MacPowerState? _power;
    private DateTime _powerRead = DateTime.MinValue;
    private string? _powerNote;
    private string? _lastPowerChange;
    private MemorySnapshot? _memory;
    private MemoryVerdict? _memoryVerdict;
    private ThermalSample? _thermalSample;
    private IReadOnlyList<CoreUser> _coreUsers = [];
    private double? _pLoad;
    private double? _eLoad;
    private IReadOnlyList<DisplayVerdict> _displays = [];
    private IReadOnlyList<TranslatedProcess> _translated = [];
    private bool _scanningGames;
    private string _scannedLibrary = string.Empty;
    private GameEntry? _game;
    private DateTime _gameLeft = DateTime.MinValue;
    private DateTime _now = DateTime.MinValue;
    private int _tick;
    private bool _busy;

    public MacBoostsController(GamingViewModel page, IMacPerformanceService service, Func<TelemetryFrame> frame)
    {
        _page = page;
        _service = service;
        _frame = frame;
        Apps = new BackgroundAppsViewModel(this);
    }

    /// <summary>The background application governor's card (its Details).</summary>
    public BackgroundAppsViewModel Apps { get; }

    internal IMacPerformanceService Service => _service;

    internal GamingViewModel Page => _page;

    private Coreloom.Core.Settings.GamingSettings Gaming => _page.Store.Current.Gaming;

    /// <summary>The library game running now (as last handed in), or null.</summary>
    public GameEntry? Game => _game;

    public bool IsSupported => _page.IsMac && _service.IsSupported;

    public static bool Owns(GamingTweak tweak) => Array.IndexOf(Tweaks, tweak) >= 0;

    public bool IsOn(GamingTweak tweak) => tweak switch
    {
        GamingTweak.HighPowerAutomation => Gaming.HighPowerAutomation,
        GamingTweak.MemoryGuard => Gaming.MemoryGuard,
        GamingTweak.ThermalHeadroom => Gaming.ThermalHeadroom,
        GamingTweak.CoreWorkload => Gaming.CoreWorkload,
        GamingTweak.MetalFx => Gaming.MetalFx,
        GamingTweak.DisplayPipeline => Gaming.DisplayPipeline,
        GamingTweak.NativeApps => Gaming.NativeApps,
        GamingTweak.BackgroundGovernor => Gaming.BackgroundGovernor,
        _ => false,
    };

    private static void Set(Coreloom.Core.Settings.GamingSettings gaming, GamingTweak tweak, bool on)
    {
        switch (tweak)
        {
            case GamingTweak.HighPowerAutomation: gaming.HighPowerAutomation = on; break;
            case GamingTweak.MemoryGuard: gaming.MemoryGuard = on; break;
            case GamingTweak.ThermalHeadroom: gaming.ThermalHeadroom = on; break;
            case GamingTweak.CoreWorkload: gaming.CoreWorkload = on; break;
            case GamingTweak.MetalFx: gaming.MetalFx = on; break;
            case GamingTweak.DisplayPipeline: gaming.DisplayPipeline = on; break;
            case GamingTweak.NativeApps: gaming.NativeApps = on; break;
            case GamingTweak.BackgroundGovernor: gaming.BackgroundGovernor = on; break;
        }
    }

    /// <summary>The key a boost's Details are opened under.</summary>
    public static string DetailsKey(GamingTweak tweak) => "mac-" + tweak.ToString().ToLowerInvariant();

    /// <summary>Reads (or the card) want this boost's data now: it's on, or its Details are open.</summary>
    private bool Wanted(GamingTweak tweak) =>
        IsOn(tweak) || (tweak == GamingTweak.BackgroundGovernor ? Apps.IsOpen : _page.Details.IsOpen && _page.Details.OpenFor == DetailsKey(tweak));

    // ------------------------------------------------------------------------------------------------ status

    /// <summary>A boost's state for its card: supported here, on, and a word for its pill once it's on.</summary>
    public TweakStatus Status(GamingTweak tweak)
    {
        if (!IsSupported)
        {
            return TweakStatus.Unsupported("Only on a Mac.");
        }

        bool on = IsOn(tweak);
        switch (tweak)
        {
            case GamingTweak.HighPowerAutomation:
                if (_power is { HasHighPowerMode: false })
                {
                    return TweakStatus.Unsupported("This Mac has no High Power Mode: only some Macs with M-series Pro and Max chips do.");
                }

                return new TweakStatus(true, on, Note: on ? _powerNote : null, Label: on ? (Gaming.AutomationRaisedFrom is not null ? "High Power" : "Watching") : null);
            case GamingTweak.CoreWorkload when !_service.IsAppleSilicon:
                return TweakStatus.Unsupported("Intel Macs have one kind of core, so there's nothing to share out.");
            case GamingTweak.NativeApps when !_service.IsAppleSilicon:
                return TweakStatus.Unsupported("Intel Macs run Intel programs natively; Rosetta 2 is for Apple silicon.");
            default:
                return new TweakStatus(true, on, Label: on ? Label(tweak) : null);
        }
    }

    private string? Label(GamingTweak tweak) => tweak switch
    {
        GamingTweak.MemoryGuard => _memoryVerdict?.Level,
        GamingTweak.ThermalHeadroom => _thermalSample is null ? null : _thermal.Evaluate().Headroom + " headroom",
        GamingTweak.CoreWorkload => _game is null ? "Watching" : CompetingCount() is var n and > 0 ? $"{N(n)} competing" : "Clear",
        GamingTweak.MetalFx => _metalFx.Count == 0 ? null : MetalFxGames() is var n and > 0 ? $"{N(n)} {(n == 1 ? "game" : "games")}" : "None found",
        GamingTweak.DisplayPipeline => _displays.Count == 0 ? null : _displays.All(d => d.Good) ? "Set up well" : "Check",
        GamingTweak.NativeApps => _architectures.Count == 0 && _translated.Count == 0 ? null
            : TranslatedCount() is var n and > 0 ? $"{N(n)} on Rosetta" : "All native",
        GamingTweak.BackgroundGovernor => Apps.HeavyCount is var n and > 0 ? $"{N(n)} to review" : "Clear",
        _ => null,
    };

    /// <summary>The line under a boost's description once it's on: what it found, or what it's waiting for.</summary>
    public string? Note(GamingTweak tweak)
    {
        if (!IsSupported || !IsOn(tweak))
        {
            return null;
        }

        switch (tweak)
        {
            case GamingTweak.HighPowerAutomation:
                return _lastPowerChange
                    ?? (_power is { OnCharger: false } ? "On battery: High Power Mode waits for the charger." : "Waiting for a game or a long graphics workload.");
            case GamingTweak.MemoryGuard:
                return _memoryVerdict is null ? "Reading memory…" : _memoryVerdict.Advice ?? $"Memory pressure is normal. {_memoryVerdict.Summary}.";
            case GamingTweak.ThermalHeadroom:
                return _thermalSample is null ? "Reading temperatures…" : _thermal.Evaluate().Advice;
            case GamingTweak.CoreWorkload:
                var competing = _coreUsers.Where(u => u.Competes).Take(3).ToList();
                return _game is null ? "Start a game to see what competes with it for the performance cores."
                    : competing.Count == 0 ? $"Nothing competes with {_game.Name} for the performance cores."
                    : $"{MemoryGuardJoin(competing.Select(u => u.Name))} {(competing.Count == 1 ? "uses" : "use")} the performance cores at high priority while {_game.Name} plays.";
            case GamingTweak.MetalFx:
                if (_scanningGames)
                {
                    return "Checking your games for MetalFX…";
                }

                var with = Library().Where(g => _metalFx.TryGetValue(g.Id, out var f) && MetalFx.Supports(f)).Select(g => g.Name).ToList();
                return _metalFx.Count == 0 ? "Checks your games when the library is ready."
                    : with.Count == 0 ? "None of your games uses MetalFX."
                    : $"MetalFX in {MemoryGuardJoin(with.Take(3))}{(with.Count > 3 ? $" and {N(with.Count - 3)} more" : string.Empty)}: open Details for what to turn on.";
            case GamingTweak.DisplayPipeline:
                var check = _displays.FirstOrDefault(d => !d.Good);
                return _displays.Count == 0 ? "Reading the displays…" : check is null ? $"{_displays[0].Name}: {_displays[0].Value}." : $"{check.Name}: {check.Detail}";
            case GamingTweak.NativeApps:
                var games = Library().Where(g => _architectures.TryGetValue(g.Id, out var a) && a == CpuArchitecture.X86_64).Select(g => g.Name).ToList();
                return _architectures.Count == 0 && _translated.Count == 0 ? "Checking your games and apps…"
                    : games.Count > 0 ? $"{MemoryGuardJoin(games.Take(3))} {(games.Count == 1 ? "runs" : "run")} through Rosetta 2: look for {(games.Count == 1 ? "its" : "their")} Apple silicon version."
                    : _translated.Count > 0 ? $"{MemoryGuardJoin(_translated.Select(t => t.Name).Distinct().Take(3))} {(_translated.Count == 1 ? "runs" : "run")} through Rosetta 2 now."
                    : "Your games and the apps running now are native to Apple silicon.";
            case GamingTweak.BackgroundGovernor:
                return Apps.Note;
            default:
                return null;
        }
    }

    private bool SameGame(string name) =>
        _game is { } game && (name.Contains(game.Name, StringComparison.OrdinalIgnoreCase) || game.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string MemoryGuardJoin(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count switch { 0 => string.Empty, 1 => list[0], _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1] };
    }

    private static string N(int n) => n.ToString(CultureInfo.CurrentCulture);

    private int CompetingCount() => _coreUsers.Count(u => u.Competes);

    private int MetalFxGames() => Library().Count(g => _metalFx.TryGetValue(g.Id, out var f) && MetalFx.Supports(f));

    private int TranslatedCount() =>
        Library().Count(g => _architectures.TryGetValue(g.Id, out var a) && a == CpuArchitecture.X86_64) + _translated.Count;

    private IReadOnlyList<GameEntry> Library() => _page.LibrarySnapshot();

    // ------------------------------------------------------------------------------------------------ switching

    /// <summary>Turns a boost on or off. High Power Mode automation first asks macOS for permission (the password, once).</summary>
    public async Task<TweakResult> SetEnabledAsync(GamingTweak tweak, bool on)
    {
        if (!IsSupported || !Status(tweak).IsSupported)
        {
            return new TweakResult(false, "Not available on this Mac.");
        }

        if (tweak == GamingTweak.HighPowerAutomation)
        {
            var result = await SetPowerAutomationAsync(on);
            if (!result.Success)
            {
                return result;
            }
        }

        _page.Store.Update("Gaming", s => Set(s.Gaming, tweak, on));
        Log.Info(LogCategory, $"{Title(tweak)} turned {(on ? "on" : "off")}.");
        if (on)
        {
            await RefreshAsync(tweak, force: true);
        }

        if (tweak == GamingTweak.BackgroundGovernor && !on && Gaming.AppsToReopen.Count > 0)
        {
            Apps.ReopenAll(); // nothing is left closed when the governor is turned off
        }

        return new TweakResult(true, on ? $"{Title(tweak)} is on." : $"{Title(tweak)} is off.");
    }

    private async Task<TweakResult> SetPowerAutomationAsync(bool on)
    {
        if (on)
        {
            var power = await _service.PowerAsync(CancellationToken.None);
            _power = power;
            if (!power.HasHighPowerMode)
            {
                return new TweakResult(false, "This Mac has no High Power Mode.");
            }

            if (!power.CanSwitchSilently)
            {
                var allowed = await _service.AllowPowerSwitchingAsync(true, CancellationToken.None);
                if (!allowed.Success)
                {
                    return allowed;
                }
            }

            return new TweakResult(true, "High Power Mode follows your games now.");
        }

        if (Gaming.AutomationRaisedFrom is int previous)
        {
            await _service.SetChargerPowerModeAsync(previous, CancellationToken.None);
            _page.Store.Update("Gaming", s => s.Gaming.AutomationRaisedFrom = null);
            Log.Info(LogCategory, $"High Power Mode automation off: charger power mode back to {MacPowerModes.Describe(previous)}.");
        }

        _lastPowerChange = null;
        var removed = await _service.AllowPowerSwitchingAsync(false, CancellationToken.None);
        return removed.Success || removed.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            ? new TweakResult(true, removed.Message)
            : removed;
    }

    internal static string Title(GamingTweak tweak) => tweak switch
    {
        GamingTweak.HighPowerAutomation => "High Power Mode automation",
        GamingTweak.MemoryGuard => "Unified memory pressure guard",
        GamingTweak.ThermalHeadroom => "Thermal headroom optimizer",
        GamingTweak.CoreWorkload => "Performance core workload monitor",
        GamingTweak.MetalFx => "MetalFX graphics optimizer",
        GamingTweak.DisplayPipeline => "ProMotion & display pipeline optimizer",
        GamingTweak.NativeApps => "Native Apple silicon detection",
        GamingTweak.BackgroundGovernor => "Background application governor",
        _ => tweak.ToString(),
    };

    // ------------------------------------------------------------------------------------------------ reading

    /// <summary>The page's status read (every 30 s, and after a change): the power state, and a boost's one-off checks.</summary>
    public async Task RefreshAsync(GamingTweak? only = null, bool force = false)
    {
        if (!IsSupported)
        {
            return;
        }

        try
        {
            if (only is null or GamingTweak.HighPowerAutomation && (force || DateTime.UtcNow - _powerRead > TimeSpan.FromSeconds(25)))
            {
                _power = await _service.PowerAsync(CancellationToken.None);
                _powerRead = DateTime.UtcNow;
            }

            if ((only is null or GamingTweak.MetalFx or GamingTweak.NativeApps) && (Wanted(GamingTweak.MetalFx) || Wanted(GamingTweak.NativeApps)))
            {
                await ScanGamesAsync(force);
            }

            if (only is null or GamingTweak.DisplayPipeline && Wanted(GamingTweak.DisplayPipeline))
            {
                _displays = DisplayPipeline.Evaluate(await Task.Run(_service.Displays));
            }

            if (only is null or GamingTweak.NativeApps && Wanted(GamingTweak.NativeApps))
            {
                _translated = await Task.Run(_service.TranslatedProcesses);
            }

            if (only is GamingTweak.MemoryGuard or GamingTweak.ThermalHeadroom or GamingTweak.CoreWorkload or GamingTweak.BackgroundGovernor)
            {
                await SampleAsync(DateTime.UtcNow, all: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.WarnThrottled("mac-boosts-refresh", TimeSpan.FromMinutes(10), LogCategory, "Reading the Mac's boosts failed.", e);
        }

        Changed();
    }

    /// <summary>MetalFX and the processor each game was built for (once per game; again when the library changes).</summary>
    private async Task ScanGamesAsync(bool force)
    {
        var games = Library();
        string key = string.Join("|", games.Select(g => g.Id));
        if (_scanningGames || (!force && key == _scannedLibrary) || games.Count == 0)
        {
            return;
        }

        _scanningGames = true;
        Changed();
        try
        {
            foreach (var game in games)
            {
                if (Wanted(GamingTweak.MetalFx) && !_metalFx.ContainsKey(game.Id))
                {
                    _metalFx[game.Id] = await _service.MetalFxAsync(game, CancellationToken.None);
                }

                if (Wanted(GamingTweak.NativeApps) && _service.IsAppleSilicon && !_architectures.ContainsKey(game.Id))
                {
                    _architectures[game.Id] = await _service.ArchitectureAsync(game, CancellationToken.None);
                }
            }

            _scannedLibrary = key;
            int metal = games.Count(g => _metalFx.TryGetValue(g.Id, out var f) && MetalFx.Supports(f));
            int intel = games.Count(g => _architectures.TryGetValue(g.Id, out var a) && a == CpuArchitecture.X86_64);
            Log.Info(LogCategory, $"Checked {N(games.Count)} games: {N(metal)} with MetalFX, {N(intel)} built for Intel only.");
        }
        finally
        {
            _scanningGames = false;
        }
    }

    /// <summary>Every two seconds from the Mac app: the game, then what each boost that's on reads.</summary>
    public async Task TickAsync(MacBoostTick tick, DateTime now)
    {
        if (!IsSupported || _busy)
        {
            return;
        }

        _busy = true;
        _now = now;
        _tick++;
        try
        {
            await GameChangedAsync(tick, now);
            if (IsOn(GamingTweak.HighPowerAutomation))
            {
                await AutomatePowerAsync(tick, now);
            }

            await SampleAsync(now, all: false);
            if (_tick % 15 == 0 && (Wanted(GamingTweak.DisplayPipeline) || Wanted(GamingTweak.NativeApps)))
            {
                if (Wanted(GamingTweak.DisplayPipeline))
                {
                    _displays = DisplayPipeline.Evaluate(await Task.Run(_service.Displays));
                }

                if (Wanted(GamingTweak.NativeApps))
                {
                    _translated = await Task.Run(_service.TranslatedProcesses);
                }
            }

            if (_tick % 30 == 0 && (Wanted(GamingTweak.MetalFx) || Wanted(GamingTweak.NativeApps)))
            {
                await ScanGamesAsync(force: false);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.WarnThrottled("mac-boosts-tick", TimeSpan.FromMinutes(10), LogCategory, "A Mac boost's reading failed.", e);
        }
        finally
        {
            _busy = false;
        }

        Changed();
    }

    /// <summary>A game started or quit: the governor's apps are quit for it, or reopened after it.</summary>
    private async Task GameChangedAsync(MacBoostTick tick, DateTime now)
    {
        var game = tick.Game;
        if (game is not null)
        {
            _gameLeft = DateTime.MinValue;
            if (_game?.Id != game.Id)
            {
                _game = game;
                Log.Info(LogCategory, $"{game.Name} is running.");
                if (IsOn(GamingTweak.BackgroundGovernor))
                {
                    await Apps.QuitForGameAsync(game);
                }
            }

            return;
        }

        if (_game is not null)
        {
            // Quitting a game and opening it again (or its launcher restarting it) shouldn't reopen everything.
            if (_gameLeft == DateTime.MinValue)
            {
                _gameLeft = now;
            }
            else if (now - _gameLeft >= TimeSpan.FromSeconds(10))
            {
                Log.Info(LogCategory, $"{_game.Name} quit.");
                _game = null;
                _gameLeft = DateTime.MinValue;
                if (Gaming.AppsToReopen.Count > 0)
                {
                    Apps.ReopenAll();
                }
            }
        }
    }

    private async Task AutomatePowerAsync(MacBoostTick tick, DateTime now)
    {
        double? gpu = _frame().GetValue(MetricIds.GpuLoad(0));
        bool wasActive = _workload.IsActive;
        bool active = _workload.Update(now, tick.Game?.Name ?? (tick.GameInFront ? "the game in front" : null), gpu);
        if (active != wasActive || _power is null || now - _powerRead > TimeSpan.FromSeconds(30))
        {
            _power = await _service.PowerAsync(CancellationToken.None);
            _powerRead = now;
        }

        var power = _power;
        var decision = HighPowerAutomation.Decide(power.HasHighPowerMode, power.OnCharger, active, power.ChargerMode, Gaming.AutomationRaisedFrom);
        string time = now.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        switch (decision.Step)
        {
            case PowerStep.RaiseToHigh:
                if (await _service.SetChargerPowerModeAsync(MacPowerModes.HighPower, CancellationToken.None))
                {
                    int from = power.ChargerMode;
                    _page.Store.Update("Gaming", s => s.Gaming.AutomationRaisedFrom = from);
                    _power = power with { ChargerMode = MacPowerModes.HighPower };
                    _powerNote = null;
                    _lastPowerChange = $"High Power Mode on at {time} for {_workload.Reason}; {MacPowerModes.Describe(from)} comes back when it ends.";
                    Log.Info(LogCategory, $"High Power Mode on for {_workload.Reason} (was {MacPowerModes.Describe(from)}).");
                }
                else
                {
                    _powerNote = "Coreloom no longer has permission to switch the power mode: turn this off and on again.";
                    Log.WarnThrottled("mac-hpm-denied", TimeSpan.FromMinutes(30), LogCategory, "Setting High Power Mode without a password failed.");
                }

                break;
            case PowerStep.Restore when decision.RestoreTo is int back:
                if (await _service.SetChargerPowerModeAsync(back, CancellationToken.None))
                {
                    _page.Store.Update("Gaming", s => s.Gaming.AutomationRaisedFrom = null);
                    _power = power with { ChargerMode = back };
                    _lastPowerChange = $"Back to {MacPowerModes.Describe(back)} at {time}, after the workload ended.";
                    Log.Info(LogCategory, $"Workload ended: charger power mode back to {MacPowerModes.Describe(back)}.");
                }

                break;
            case PowerStep.Forget:
                _page.Store.Update("Gaming", s => s.Gaming.AutomationRaisedFrom = null);
                _lastPowerChange = "The power mode was changed by hand, so Coreloom left it as it is.";
                break;
        }
    }

    /// <summary>Memory, heat, the cores and the apps: read while their boost is on or their Details are open.</summary>
    private async Task SampleAsync(DateTime now, bool all)
    {
        var frame = _frame();
        if (Wanted(GamingTweak.ThermalHeadroom))
        {
            var pressure = await Task.Run(_service.ThermalState) ?? ThermalPressure.Nominal;
            double? fan = frame.Samples.Where(p => p.Key.StartsWith("board/fan/", StringComparison.Ordinal)).Select(p => (double?)p.Value.Value).Max();
            _thermalSample = new ThermalSample(now, pressure, frame.GetValue(MetricIds.CpuTemperature), frame.GetValue(MetricIds.GpuTemperature(0)),
                frame.GetValue(MetricIds.CpuLoad), frame.GetValue(MetricIds.GpuLoad(0)), frame.GetValue(MetricIds.CpuClockPerformanceCores) ?? frame.GetValue(MetricIds.CpuClockAverage),
                frame.GetValue(MetricIds.GpuCoreClock(0)), fan);
            _thermal.Add(_thermalSample);
        }

        bool every4 = all || _tick % 2 == 0;
        if (every4 && Wanted(GamingTweak.MemoryGuard) && await Task.Run(_service.Memory) is { } memory)
        {
            _memory = memory;
            _memoryVerdict = _memoryGuard.Evaluate(memory, now, _game?.Name);
        }

        if (every4 && Wanted(GamingTweak.CoreWorkload) && _service.IsAppleSilicon)
        {
            var times = await Task.Run(_service.CoreTimes);
            _coreUsers = _cores.Update(times, now, _service.TicksToSeconds, _game?.Name);
            _pLoad = frame.GetValue(MetricIds.CpuLoadPerformanceCores);
            _eLoad = frame.GetValue(MetricIds.CpuLoadEfficiencyCores);
        }

        if (every4 && Wanted(GamingTweak.BackgroundGovernor))
        {
            await Apps.RefreshAsync();
        }
    }

    // ------------------------------------------------------------------------------------------------ details

    /// <summary>A boost's Details: a summary, its lines, a note at the bottom and what to show when there are no lines yet.</summary>
    public (string Summary, IReadOnlyList<DetailRow> Rows, string? Note, string? Empty) Details(GamingTweak tweak)
    {
        var ci = CultureInfo.CurrentCulture;
        var f = _page.Store.Formatter;
        string Pct(double? v) => v is { } x ? $"{x.ToString("0", ci)} %" : "—";
        string Temp(double? c) => c is { } x ? f.Temperature(x).ToString() : "—";
        string Clock(double? mhz) => mhz is { } x ? f.Clock(x).ToString() : "—";
        switch (tweak)
        {
            case GamingTweak.HighPowerAutomation:
            {
                var p = _power;
                var rows = new List<DetailRow>
                {
                    new("High Power Mode", p is null ? "—" : p.HasHighPowerMode ? "This Mac has it" : "Not on this Mac", p?.HasHighPowerMode == true ? ColorKeys.ToneCool : ColorKeys.Neutral, null),
                    new("Power", p is null ? "—" : p.OnCharger ? "Charger" : "Battery", p?.OnCharger == true ? ColorKeys.ToneCool : ColorKeys.Neutral,
                        "Only the charger's setting changes; on battery your Mac keeps its own mode."),
                    new("Charger's power mode", p is null ? "—" : MacPowerModes.Describe(p.ChargerMode), p?.ChargerMode == MacPowerModes.HighPower ? ColorKeys.ToneCool : ColorKeys.Neutral, null),
                    new("Workload", _workload.Reason ?? "None now", _workload.IsActive ? ColorKeys.ToneCool : ColorKeys.Neutral,
                        "A game from your library, an app filed under Games in front, or the GPU at 60 % or more for 20 seconds."),
                    new("Switching", p?.CanSwitchSilently == true ? "Allowed" : "Not allowed yet", p?.CanSwitchSilently == true ? ColorKeys.ToneCool : ColorKeys.Neutral,
                        "Turning this on lets Coreloom set only the charger's power mode without asking each time; turning it off removes that."),
                };
                if (_lastPowerChange is { } change)
                {
                    rows.Add(new DetailRow("Last change", string.Empty, ColorKeys.Neutral, change));
                }

                return ("High Power Mode lets the fans run faster so the chip keeps its highest speed through long sessions. Coreloom turns it on while you play on the charger and puts your setting back when you stop.",
                    rows, "Coreloom changes only pmset's charger power mode, and only with your permission.", null);
            }

            case GamingTweak.MemoryGuard:
            {
                if (_memory is not { } m || _memoryVerdict is not { } v)
                {
                    return ("Memory pressure, compressed memory, swap and the apps using the most.", [], null, "Reading memory…");
                }

                var rows = new List<DetailRow>
                {
                    new("Memory pressure", MemoryGuard.Describe(m.Pressure), m.Pressure == MemoryPressure.Normal ? ColorKeys.ToneCool : m.Pressure == MemoryPressure.Warning ? ColorKeys.ToneNormal : ColorKeys.ToneHot,
                        "macOS's own measure of how hard it's working to find memory (Activity Monitor's graph)."),
                    new("In use", $"{f.MemoryBytes(m.Used)} of {f.MemoryBytes(m.Total)}", ColorKeys.Neutral, null),
                    new("Compressed", f.MemoryBytes(m.Compressed).ToString(), ColorKeys.Neutral, "Memory macOS squeezed to make room; a lot of it costs processor time."),
                    new("Swap", f.MemoryBytes(m.SwapUsed).ToString() + (v.SwapGrowthPerMinute >= 1024 * 1024 ? $", growing {f.MemoryBytes(v.SwapGrowthPerMinute)} a minute" : string.Empty),
                        v.SwapGrowthPerMinute >= MemoryGuard.SwapGrowthWarning ? ColorKeys.ToneNormal : ColorKeys.Neutral, "Memory written to the disk: when it grows during a game, stutters follow."),
                };
                rows.AddRange(m.Apps.Take(6).Select(a => new DetailRow(a.Name, f.MemoryBytes(a.Bytes, a.Bytes >= 1L << 30 ? 1 : 0).ToString(),
                    v.Suggested.Contains(a) ? ColorKeys.ToneNormal : _game is not null && SameGame(a.Name) ? ColorKeys.ToneCool : ColorKeys.Neutral,
                    v.Suggested.Contains(a) ? "Quitting it would give your game room." : _game is not null && SameGame(a.Name) ? "Your game." : null)));
                return ($"Memory is {v.Level.ToLowerInvariant()}. {v.Summary}.", rows, v.Advice ?? "Nothing to do: there's room for your game.", null);
            }

            case GamingTweak.ThermalHeadroom:
            {
                if (_thermalSample is not { } s)
                {
                    return ("Thermal state, temperatures and clocks over the session.", [], null, "Reading temperatures…");
                }

                var v = _thermal.Evaluate();
                var rows = new List<DetailRow>
                {
                    new("Thermal state", ThermalTracker.Describe(s.Pressure), s.Pressure == ThermalPressure.Nominal ? ColorKeys.ToneCool : s.Pressure == ThermalPressure.Fair ? ColorKeys.ToneNormal : ColorKeys.ToneHot,
                        "macOS's own judgement: from Serious on it slows the chip to cool it."),
                    new("Processor", $"{Temp(s.CpuTemperature)} · {Pct(s.CpuLoad)} busy · {Clock(s.CpuClock)}", ColorKeys.Neutral, null),
                    new("Graphics", $"{Temp(s.GpuTemperature)} · {Pct(s.GpuLoad)} busy · {Clock(s.GpuClock)}", ColorKeys.Neutral, null),
                    new("Fans", s.FanRpm is { } rpm ? f.Fan(rpm).ToString() : "No fans or not reported", ColorKeys.Neutral, null),
                    new("Slowed by heat", v.Events == 0 ? "Not this session" : $"{N(v.Events)} {(v.Events == 1 ? "time" : "times")} this session",
                        v.Events == 0 ? ColorKeys.ToneCool : ColorKeys.ToneNormal, v.ClockDrop is { } drop && drop > 0.02 ? $"The busier chip's clock is {(drop * 100).ToString("0", ci)} % under its peak this session." : null),
                };
                return ($"{v.Headroom} headroom.", rows, v.Advice, null);
            }

            case GamingTweak.CoreWorkload:
            {
                var rows = new List<DetailRow>
                {
                    new("Performance cores", Pct(_pLoad), ColorKeys.Neutral, "The fast cores games run on."),
                    new("Efficiency cores", Pct(_eLoad), ColorKeys.Neutral, "The frugal cores macOS gives background work."),
                };
                rows.AddRange(_coreUsers.Take(8).Select(u => new DetailRow(u.Name,
                    $"{(u.CoresUsed * 100).ToString("0", ci)} % of a core · {(u.PerformanceShare * 100).ToString("0", ci)} % on performance cores",
                    u.IsGame ? ColorKeys.ToneCool : u.Competes ? ColorKeys.ToneNormal : ColorKeys.Neutral,
                    (u.IsGame ? "Your game. " : u.Competes ? "Competes with your game. " : string.Empty) + $"Mostly {CoreWorkloadMonitor.DescribePriority(u.Priority)} priority (QoS).")));
                string summary = _game is null
                    ? "Which programs use the performance and efficiency cores, and at what priority. Start a game to see what competes with it."
                    : $"While {_game.Name} plays: who else uses the performance cores.";
                string? note = CompetingCount() > 0
                    ? "Quit the apps marked as competing (the background application governor can do it for each game); macOS runs utility and background work on the efficiency cores by itself."
                    : "macOS keeps background work on the efficiency cores by itself; nothing needs changing.";
                return (summary, _coreUsers.Count == 0 ? [] : rows, note, _coreUsers.Count == 0 ? "Measuring for a few seconds…" : null);
            }

            case GamingTweak.MetalFx:
            {
                var rows = Library()
                    .Select(g => (Game: g, Found: _metalFx.TryGetValue(g.Id, out var x) ? x : (MetalFxFeatures?)null))
                    .Where(x => x.Found is not null)
                    .OrderByDescending(x => MetalFx.Supports(x.Found!.Value))
                    .ThenBy(x => x.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => new DetailRow(x.Game.Name, MetalFx.Describe(x.Found!.Value), MetalFx.Supports(x.Found.Value) ? ColorKeys.ToneCool : ColorKeys.Neutral,
                        MetalFx.Supports(x.Found.Value) ? MetalFx.Advice(x.Found.Value) : null))
                    .ToList();
                return ("MetalFX is Apple's upscaling, frame interpolation and denoising. Games build it in themselves; this finds which of yours did and what to turn on.",
                    rows, "Coreloom reads the games' program files; it doesn't change them or add anything to them.",
                    _scanningGames ? "Checking your games…" : Library().Count == 0 ? "No games found yet." : "Checking your games…");
            }

            case GamingTweak.DisplayPipeline:
            {
                var rows = _displays.Select(d => new DetailRow(d.Name, d.Value, d.Good ? ColorKeys.ToneCool : ColorKeys.ToneNormal, d.Detail)).ToList();
                return ("Refresh rate, ProMotion, HDR, resolution and colour depth of each display, and what suits games.", rows,
                    "Change these in System Settings › Displays; Coreloom only reads them.", "Reading the displays…");
            }

            case GamingTweak.NativeApps:
            {
                var rows = Library()
                    .Where(g => _architectures.ContainsKey(g.Id))
                    .OrderBy(g => _architectures[g.Id] == CpuArchitecture.X86_64 ? 0 : 1)
                    .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new DetailRow(g.Name, NativeApps.Describe(_architectures[g.Id]),
                        _architectures[g.Id] == CpuArchitecture.X86_64 ? ColorKeys.ToneNormal : _architectures[g.Id] == CpuArchitecture.Unknown ? ColorKeys.Neutral : ColorKeys.ToneCool,
                        _architectures[g.Id] is CpuArchitecture.X86_64 or CpuArchitecture.Unknown ? NativeApps.Advice(g.Name, _architectures[g.Id]) : null))
                    .ToList();
                rows.AddRange(_translated.GroupBy(t => t.Name).Select(g => new DetailRow(g.Key, "Running through Rosetta 2", ColorKeys.ToneNormal,
                    "An Intel program running now. An Apple silicon version would use less power and run faster.")));
                return ("Which of your games and running apps are built for Apple silicon, and which run through Rosetta 2's translation.", rows,
                    "Rosetta 2 runs Intel programs well, but native ones are faster and use less battery.", "Checking your games and apps…");
            }

            default:
                return (string.Empty, [], null, null);
        }
    }

    internal void Changed()
    {
        _page.MacBoostsChanged();
    }
}

/// <summary>An app in the background application governor's card, with its tick box.</summary>
public sealed class BackgroundAppRow : ObservableObject
{
    private bool _isSelected;
    private RunningAppInfo? _app;
    private string _usage;
    private bool _heavy;

    public BackgroundAppRow(string bundleId, string name, RunningAppInfo? app, string usage, bool heavy, bool selected)
    {
        BundleId = bundleId;
        Name = name;
        _app = app;
        _usage = usage;
        _heavy = heavy;
        _isSelected = selected;
    }

    public string BundleId { get; }

    public string Name { get; }

    /// <summary>Running now (null when it's only in the game's list).</summary>
    public RunningAppInfo? App
    {
        get => _app;
        private set
        {
            if (SetProperty(ref _app, value))
            {
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public bool IsRunning => _app is not null;

    public string Usage
    {
        get => _usage;
        private set => SetProperty(ref _usage, value);
    }

    /// <summary>Uses enough to be worth quitting while you play.</summary>
    public bool Heavy
    {
        get => _heavy;
        private set => SetProperty(ref _heavy, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    internal void Update(RunningAppInfo? app, string usage, bool heavy)
    {
        App = app;
        Usage = usage;
        Heavy = heavy;
    }
}

/// <summary>
/// The background application governor's card: the apps running while you play (heaviest first), ticked to quit now or
/// each time a game starts; the apps quit for a game open again, in the background, when it ends. Apps are asked to quit
/// as their Quit command would (they can ask to save); macOS's own apps and game launchers are never listed.
/// </summary>
public sealed class BackgroundAppsViewModel : ObservableObject
{
    private readonly MacBoostsController _owner;
    private bool _isOpen;
    private bool _isBusy;
    private string? _message;
    private string? _note;
    private GameEntry? _profileGame;
    private readonly Dismissal _messageDismissal = new();

    public BackgroundAppsViewModel(MacBoostsController owner)
    {
        _owner = owner;
        CloseCommand = new RelayCommand(() => IsOpen = false);
        QuitSelectedCommand = new AsyncRelayCommand(QuitSelectedAsync, () => !IsBusy && Rows.Any(r => r.IsSelected && r.IsRunning));
        SaveForGameCommand = new RelayCommand(SaveForGame, () => ProfileGame is not null);
        ReopenCommand = new RelayCommand(ReopenAll, () => CanReopen);
    }

    public ObservableCollection<BackgroundAppRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public int HeavyCount => Rows.Count(r => r.IsRunning && r.Heavy);

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetProperty(ref _isOpen, value) && value)
            {
                OnPropertyChanged(nameof(Games)); // the library may have loaded since the card was first drawn
                ProfileGame = _owner.Game is { } playing ? Games.FirstOrDefault(g => g.Id == playing.Id) ?? playing
                    : _profileGame is { } chosen ? Games.FirstOrDefault(g => g.Id == chosen.Id) : Games.FirstOrDefault();
                _ = RefreshAsync();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                QuitSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The games a list of apps can be kept for (the library, most recently played first).</summary>
    public IReadOnlyList<GameEntry> Games => [.. _owner.Page.LibrarySnapshot().OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue)];

    /// <summary>The game whose list the ticks show and "Save for this game" keeps.</summary>
    public GameEntry? ProfileGame
    {
        get => _profileGame;
        set
        {
            if (SetProperty(ref _profileGame, value))
            {
                SaveForGameCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ProfileText));
                ApplyProfileTicks();
            }
        }
    }

    public string ProfileText => _profileGame is { } game && _owner.Page.Store.Current.Gaming.QuitProfileFor(game.Id) is { } profile
        ? $"{profile.BundleIds.Count.ToString(CultureInfo.CurrentCulture)} {(profile.BundleIds.Count == 1 ? "app quits" : "apps quit")} each time {game.Name} starts, and {(profile.BundleIds.Count == 1 ? "reopens" : "reopen")} when it ends."
        : "Tick the apps to quit, then keep them for a game: they quit each time it starts and reopen when it ends.";

    public bool CanReopen => _owner.Page.Store.Current.Gaming.AppsToReopen.Count > 0;

    public string ReopenText => CanReopen
        ? $"Reopen {(_owner.Page.Store.Current.Gaming.AppsToReopen.Count == 1 ? "1 app" : $"{_owner.Page.Store.Current.Gaming.AppsToReopen.Count.ToString(CultureInfo.CurrentCulture)} apps")} now"
        : "Reopen apps";

    /// <summary>What happened (leaves after five seconds).</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }

            _messageDismissal.Update(!string.IsNullOrEmpty(value), () => Message = null);
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>The boost's line: what it did last, or what it found.</summary>
    public string Note => _note ?? (HeavyCount > 0
        ? $"{MemoryGuardNames(Rows.Where(r => r.IsRunning && r.Heavy).Select(r => r.Name).Take(3))} {(HeavyCount == 1 ? "uses" : "use")} a lot while you play: open Details to quit {(HeavyCount == 1 ? "it" : "them")} for a game."
        : "Nothing in the background is using much now.");

    public IRelayCommand CloseCommand { get; }

    public IAsyncRelayCommand QuitSelectedCommand { get; }

    public IRelayCommand SaveForGameCommand { get; }

    public IRelayCommand ReopenCommand { get; }

    private static string MemoryGuardNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count switch { 0 => string.Empty, 1 => list[0], _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1] };
    }

    /// <summary>Reads the running apps again (heaviest first) and keeps the ticks.</summary>
    public async Task RefreshAsync()
    {
        var apps = await Task.Run(_owner.Service.RunningApps);
        var candidates = BackgroundGovernor.Rank(apps, _owner.Game?.Name, null);
        var profile = _profileGame is { } game ? _owner.Page.Store.Current.Gaming.QuitProfileFor(game.Id) : null;
        var wanted = new List<(string Id, string Name, RunningAppInfo? App, string Usage, bool Heavy)>();
        foreach (var c in candidates)
        {
            wanted.Add((c.App.BundleId ?? c.App.BundlePath ?? c.App.Name, c.App.Name, c.App, c.Usage, c.Heavy));
        }

        foreach (string id in profile?.BundleIds ?? [])
        {
            if (!wanted.Any(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                wanted.Add((id, NameFromId(id), null, "Not running", false));
            }
        }

        var byId = Rows.ToDictionary(r => r.BundleId, StringComparer.OrdinalIgnoreCase);
        var next = new List<BackgroundAppRow>();
        foreach (var w in wanted)
        {
            if (byId.TryGetValue(w.Id, out var row))
            {
                row.Update(w.App, w.Usage, w.Heavy);
            }
            else
            {
                row = new BackgroundAppRow(w.Id, w.Name, w.App, w.Usage, w.Heavy, profile?.BundleIds.Contains(w.Id, StringComparer.OrdinalIgnoreCase) ?? false);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BackgroundAppRow.IsSelected))
                    {
                        QuitSelectedCommand.NotifyCanExecuteChanged();
                    }
                };
            }

            next.Add(row);
        }

        if (!next.SequenceEqual(Rows))
        {
            Rows.Clear();
            foreach (var row in next)
            {
                Rows.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HeavyCount));
        OnPropertyChanged(nameof(Note));
        QuitSelectedCommand.NotifyCanExecuteChanged();
    }

    private static string NameFromId(string id) =>
        id.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(id) : id[(id.LastIndexOf('.') + 1)..];

    private void ApplyProfileTicks()
    {
        var profile = _profileGame is { } game ? _owner.Page.Store.Current.Gaming.QuitProfileFor(game.Id) : null;
        foreach (var row in Rows)
        {
            row.IsSelected = profile?.BundleIds.Contains(row.BundleId, StringComparer.OrdinalIgnoreCase) ?? false;
        }
    }

    /// <summary>Quits the ticked apps now; while a game runs they reopen when it ends.</summary>
    private async Task QuitSelectedAsync()
    {
        var chosen = Rows.Where(r => r.IsSelected && r.App is not null).Select(r => r.App!).ToList();
        var quit = await QuitAsync(chosen, reopenLater: _owner.Game is not null);
        Message = quit.Count == 0 ? "Nothing was quit: the apps may have asked to save first."
            : $"Quit {MemoryGuardNames(quit)}{(_owner.Game is not null ? $"; {(quit.Count == 1 ? "it reopens" : "they reopen")} when {_owner.Game.Name} ends" : string.Empty)}.";
        await RefreshAsync();
    }

    /// <summary>Keeps the ticked apps as the game's list (an empty list removes it).</summary>
    private void SaveForGame()
    {
        if (_profileGame is not { } game)
        {
            return;
        }

        var ids = Rows.Where(r => r.IsSelected).Select(r => r.BundleId).ToList();
        _owner.Page.Store.Update("Gaming", s =>
        {
            s.Gaming.QuitProfiles.RemoveAll(p => string.Equals(p.GameId, game.Id, StringComparison.OrdinalIgnoreCase));
            if (ids.Count > 0)
            {
                s.Gaming.QuitProfiles.Insert(0, new QuitProfile { GameId = game.Id, Name = game.Name, BundleIds = ids });
            }

            s.Gaming.QuitProfiles = QuitProfile.Normalize(s.Gaming.QuitProfiles);
        });
        Log.Info("Gaming", $"Apps to quit for {game.Name}: {(ids.Count == 0 ? "none" : string.Join(", ", ids))}.");
        Message = ids.Count == 0 ? $"{game.Name} no longer quits any apps." : $"Kept for {game.Name}.";
        OnPropertyChanged(nameof(ProfileText));
        _owner.Changed();
    }

    /// <summary>A game started: its list's apps that are running are quit, to reopen when it ends.</summary>
    internal async Task QuitForGameAsync(GameEntry game)
    {
        if (_owner.Page.Store.Current.Gaming.QuitProfileFor(game.Id) is not { } profile)
        {
            return;
        }

        var running = (await Task.Run(_owner.Service.RunningApps))
            .Where(a => a.BundleId is { } id && profile.BundleIds.Contains(id, StringComparer.OrdinalIgnoreCase) && !BackgroundGovernor.IsEssential(a))
            .ToList();
        var quit = await QuitAsync(running, reopenLater: true);
        if (quit.Count > 0)
        {
            _note = $"Quit {MemoryGuardNames(quit)} for {game.Name}; {(quit.Count == 1 ? "it reopens" : "they reopen")} when you stop playing.";
            Log.Info("Gaming", _note);
        }
    }

    private async Task<List<string>> QuitAsync(IReadOnlyList<RunningAppInfo> apps, bool reopenLater)
    {
        IsBusy = true;
        var quit = new List<string>();
        try
        {
            foreach (var app in apps)
            {
                if (BackgroundGovernor.IsEssential(app))
                {
                    continue;
                }

                if (await _owner.Service.QuitAsync(app, CancellationToken.None))
                {
                    quit.Add(app.Name);
                    if (reopenLater && app.BundlePath is { } path)
                    {
                        _owner.Page.Store.Update("Gaming", s =>
                        {
                            if (!s.Gaming.AppsToReopen.Contains(path, StringComparer.Ordinal))
                            {
                                s.Gaming.AppsToReopen.Add(path);
                            }
                        });
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
            ReopenChanged();
        }

        if (quit.Count > 0)
        {
            Log.Info("Gaming", $"Quit for gaming: {string.Join(", ", quit)}.");
        }

        return quit;
    }

    /// <summary>Opens the apps quit for a game again, in the background.</summary>
    public void ReopenAll()
    {
        var paths = _owner.Page.Store.Current.Gaming.AppsToReopen.ToList();
        var opened = paths.Where(_owner.Service.Reopen).Select(p => Path.GetFileNameWithoutExtension(p)).ToList();
        _owner.Page.Store.Update("Gaming", s => s.Gaming.AppsToReopen.Clear());
        if (opened.Count > 0)
        {
            _note = $"Reopened {MemoryGuardNames(opened)}.";
            Log.Info("Gaming", _note);
        }

        ReopenChanged();
        _owner.Changed();
    }

    private void ReopenChanged()
    {
        OnPropertyChanged(nameof(CanReopen));
        OnPropertyChanged(nameof(ReopenText));
        OnPropertyChanged(nameof(Note));
        ReopenCommand.NotifyCanExecuteChanged();
    }
}
