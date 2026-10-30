using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// CPU cache optimization on the Gaming page (since 1.8.5, in DirectStorage's place): while the user has it on and a
/// library game is in front, it keeps known background apps off the cores whose L3 cache the game uses
/// (<see cref="CacheLocality"/>), and gives them their cores back 30 seconds after the game leaves, when it's turned
/// off, or when Coreloom closes. Fed once a second by the app (the same ticks as core scheduling). Every move and
/// restore is written to the log.
/// </summary>
public sealed class CacheOptimizationController : ObservableObject
{
    private const string LogCategory = "Gaming";

    /// <summary>A session ends when no game has been in front for this long (alt-tabbing out doesn't end it).</summary>
    private static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(30);

    /// <summary>How often running apps are looked through for ones to move (apps opened mid-game are picked up).</summary>
    private static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(5);

    private readonly GamingViewModel _page;
    private readonly ICoreSchedulingService _service;
    private readonly Dictionary<int, (string Name, ulong Original)> _moved = [];
    private CorePlacement? _placement;
    private string? _gameId;
    private string? _gameName;
    private string? _program;
    private int _gamePid;
    private DateTime _lastSeen;
    private DateTime _lastScan;
    private CacheTarget? _target;
    private bool _isBusy;

    public CacheOptimizationController(GamingViewModel page, ICoreSchedulingService service)
    {
        _page = page;
        _service = service;
    }

    private CoreLayout Layout => _page.CoreScheduling.Layout;

    /// <summary>More than one L3 cache, and Windows lets Coreloom choose cores.</summary>
    public bool IsSupported => _service.IsSupported && !_page.IsMac && CacheLocality.Applies(Layout);

    public bool IsOn => _page.Store.Current.Gaming.CacheOptimization;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The game being looked after now, or null.</summary>
    public string? GameName => _gameName;

    /// <summary>The apps moved now (program file names, for the details).</summary>
    public IReadOnlyList<string> MovedApps => [.. _moved.Values.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The cores the game has to itself now, or null.</summary>
    internal CoreGroup? TargetGroup => _target?.Group;

    public TweakStatus Status => IsSupported ? new TweakStatus(true, IsOn) : TweakStatus.Unsupported(CacheLocality.UnsupportedText(Layout));

    /// <summary>What it's doing, for the boost's recommendation line once it's on (null while off).</summary>
    public string? Note
    {
        get
        {
            if (!IsOn || !IsSupported)
            {
                return null;
            }

            if (_gameName is null)
            {
                return "On · waiting for a game from your library.";
            }

            return _target is { Group: { } group }
                ? $"Keeping {CacheLocality.Apps(_moved.Count)} off {CacheLocality.Name(group)} while {_gameName} plays."
                : _target?.Text ?? $"Finding where {_gameName} runs…";
        }
    }

    /// <summary>Turns it on, or off (giving every app it moved its cores back).</summary>
    public Task<TweakResult> SetEnabledAsync(bool on)
    {
        if (!IsSupported)
        {
            return Task.FromResult(new TweakResult(false, CacheLocality.UnsupportedText(Layout)));
        }

        if (on != IsOn)
        {
            IsBusy = true;
            if (!on)
            {
                RestoreAll();
            }

            _page.Store.Update("Gaming", s => s.Gaming.CacheOptimization = on);
            Log.Info(LogCategory, on ? "CPU cache optimization turned on." : "CPU cache optimization turned off.");
            IsBusy = false;
            Changed();
        }

        return Task.FromResult(new TweakResult(true, on ? "Background apps will be kept off the game's cache." : "Background apps have every core again."));
    }

    /// <summary>Called about once a second by the app while it's on (and once more when it goes off).</summary>
    public void Tick(CoreTick tick, DateTime now)
    {
        if (!IsOn || !IsSupported)
        {
            if (_moved.Count > 0 || _gameName is not null)
            {
                RestoreAll();
            }

            return;
        }

        var game = tick.ProcessId is int pid && tick.ProgramPath is { Length: > 0 } path
            ? GameLibrary.FindRunning(_page.LibrarySnapshot(), [new RunningProcess(pid, path)])?.Game
            : null;
        if (game is null)
        {
            if (_gameName is not null && now - _lastSeen > EndAfter)
            {
                RestoreAll(); // the game left
            }

            return;
        }

        if (_gameId is not null && !string.Equals(_gameId, game.Id, StringComparison.OrdinalIgnoreCase))
        {
            RestoreAll(); // another game: start over
        }

        if (_gameId is null)
        {
            _gameId = game.Id;
            _gameName = game.Name;
            _program = tick.ProgramPath;
            _placement = new CorePlacement(Layout);
            _target = null;
            _lastScan = DateTime.MinValue;
        }

        _gamePid = tick.ProcessId!.Value;
        _lastSeen = now;
        if (tick.ThreadLoads is { } loads)
        {
            _placement?.Add(loads);
        }

        // The game's cores, decided once per session (a pinned profile can still take over).
        var pinned = Pinned();
        if (_target?.Group is null || (pinned is not null && pinned != _target.Group))
        {
            var shares = _placement is { Samples: >= CorePlacement.MinimumSamples } placement ? placement.Shares : null;
            var target = CacheLocality.Target(Layout, pinned, shares, AmdParks(), _gameName!);
            if (target.Group != _target?.Group)
            {
                if (_target?.Group is not null)
                {
                    GiveBack(); // the game moved to other cores: the apps go back first
                }

                _lastScan = DateTime.MinValue;
            }

            _target = target;
        }

        if (_target?.Group is { } group && now - _lastScan >= ScanEvery)
        {
            _lastScan = now;
            Move(group);
        }

        Changed();
    }

    /// <summary>Gives every app it moved its cores back (also when Coreloom closes).</summary>
    public void RestoreAll()
    {
        GiveBack();
        _gameId = null;
        _gameName = null;
        _program = null;
        _target = null;
        _placement = null;
        Changed();
    }

    /// <summary>The card the Details pill opens: the processor's caches, what the game has now, the apps moved.</summary>
    public (string Summary, IReadOnlyList<DetailRow> Rows, string? Note, string? Empty) Details()
    {
        var rows = new List<DetailRow>();
        foreach (var group in Layout.Groups)
        {
            bool game = _target?.Group == group;
            string l3 = group.L3Bytes > 0 ? $"{(group.L3Bytes >> 20).ToString(System.Globalization.CultureInfo.CurrentCulture)} MB L3" : "L3 shared";
            rows.Add(new DetailRow(group.Name, $"{group.Cores.ToString(System.Globalization.CultureInfo.CurrentCulture)} cores · {l3}",
                game ? ColorKeys.ToneCool : ColorKeys.Neutral,
                game ? $"{_gameName} has these to itself now." : _target?.Group is not null ? "Background apps run here while the game plays." : null));
        }

        var moved = MovedApps;
        if (moved.Count > 0)
        {
            rows.Add(new DetailRow("Moved now", CacheLocality.Apps(_moved.Count), ColorKeys.ToneCool, string.Join(", ", moved)));
        }

        string summary = !IsSupported ? CacheLocality.UnsupportedText(Layout)
            : !IsOn ? $"Off. {Layout.Summary}: turn it on and Coreloom keeps background apps off the game's L3 cache while you play."
            : Note ?? string.Empty;
        string note = $"It moves {CacheLocality.AppKinds}, never the game, Windows' own processes or an app whose cores you chose yourself, and gives them every core back 30 seconds after the game leaves. " +
            "On a Ryzen X3D it leaves things to AMD's driver while that driver is parking the other cores.";
        return (summary, rows, note, rows.Count == 0 ? "The processor's caches couldn't be read." : null);
    }

    /// <summary>The cores the game's core profile keeps it on, when core scheduling applies one.</summary>
    private CoreGroup? Pinned()
    {
        var scheduling = _page.CoreScheduling;
        if (!scheduling.IsOn || _program is null || scheduling.Amd.For(_program) is not null)
        {
            return null;
        }

        return CoreProfile.For(_page.Store.Current.Gaming.CoreProfiles, _program) is { } profile && profile.Cores is not (CoreKind.Frequency or CoreKind.Efficiency)
            ? Layout.Group(profile.Cores)
            : null;
    }

    /// <summary>A Ryzen X3D whose 3D V-Cache driver runs with a power plan that lets it park the frequency cores.</summary>
    private bool AmdParks() =>
        Layout.IsVCache && _page.CoreScheduling.Amd.IsActive && !CoreScheduling.StopsParking(_page.CurrentStatus.ActivePowerPlan);

    private void Move(CoreGroup group)
    {
        ulong mask = CacheLocality.BackgroundMask(Layout, group);
        if (mask == 0)
        {
            return;
        }

        IReadOnlyList<RunningProcess> running;
        try
        {
            running = _service.RunningPrograms();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.WarnThrottled("cache-optimization", TimeSpan.FromMinutes(10), LogCategory, "CPU cache optimization couldn't list the running apps.", e);
            return;
        }

        var moved = new List<string>();
        foreach (var process in CacheLocality.ToMove(running, [_gamePid], _service.GetAffinity, _moved.Keys, mask))
        {
            var before = _service.GetAffinity(process.Id);
            if (before is { } cores && (cores.Process == mask || _service.SetAffinity(process.Id, mask)))
            {
                string name = GameExecutables.FileName(process.Path) ?? process.Path;
                _moved[process.Id] = (name, cores.System); // every core, which it had (or its parent had) before
                moved.Add(name);
            }
        }

        if (moved.Count > 0)
        {
            Log.Info(LogCategory, $"CPU cache optimization: moved {string.Join(", ", moved.Distinct(StringComparer.OrdinalIgnoreCase))} off {CacheLocality.Name(group)} while {_gameName} plays ({_target?.Text}).");
        }
    }

    private void GiveBack()
    {
        if (_moved.Count == 0)
        {
            return;
        }

        int restored = 0;
        foreach (var (pid, (_, original)) in _moved)
        {
            if (_service.SetAffinity(pid, original))
            {
                restored++;
            }
        }

        Log.Info(LogCategory, $"CPU cache optimization: gave {restored} of {_moved.Count} background app processes every core back (the others had closed).");
        _moved.Clear();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(Note));
    }
}
