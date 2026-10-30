using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>What the app hands VRAM management every couple of seconds: the program in front and its graphics memory.</summary>
/// <param name="ProcessId">The game in front (null when none).</param>
/// <param name="ProgramPath">Its program file.</param>
/// <param name="Sample">Its graphics memory, when it could be read.</param>
/// <param name="Problem">Why it couldn't be read (Windows' GPU counters missing), or null.</param>
public readonly record struct VramTick(int? ProcessId, string? ProgramPath, VramSample? Sample, string? Problem = null);

/// <summary>
/// Intelligent VRAM management on the Gaming page (since 1.8.4): while the user has it on and a library game is in
/// front, it collects the game's graphics memory (handed in by the app), and when the game leaves keeps a record per game
/// with a recommendation (<see cref="VramAdvice"/>): fine as it is, lower Texture quality one step, or close other apps
/// on the card. It reads only; nothing in the game or the driver is changed. Each game's result is written to the log.
/// </summary>
public sealed class VramController : ObservableObject
{
    private const string LogCategory = "Gaming";

    /// <summary>A session ends when no game has been in front for this long (alt-tabbing out doesn't end it).</summary>
    private static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(30);

    /// <summary>Fewer readings than this (about ten seconds) aren't kept: too little to judge.</summary>
    private const int MinimumSamples = 5;

    private readonly GamingViewModel _page;
    private VramSession? _session;
    private DateTime _lastSeen;
    private string? _problem;
    private bool _isBusy;

    public VramController(GamingViewModel page)
    {
        _page = page;
    }

    public bool IsOn => _page.Store.Current.Gaming.VramManagement;

    public bool IsSupported => _page.IsSupported && !_page.IsMac;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The game being watched now, or null.</summary>
    public string? GameName => _session?.Name;

    /// <summary>The session in progress (for tests).</summary>
    internal VramSession? Session => _session;

    /// <summary>
    /// The boost's state. Its note is only a problem reading graphics memory (shown in the warning color); what it's
    /// watching and the latest game's result are <see cref="BoostNote"/>, shown on the boost's recommendation line.
    /// </summary>
    public TweakStatus Status => IsSupported
        ? new TweakStatus(true, IsOn, Note: IsOn ? _problem : null)
        : TweakStatus.Unsupported("Needs Windows' graphics-memory counters.");

    /// <summary>"Watching Cyberpunk 2077: 9.8 of 16 GB in use", the latest game's result, or what it's waiting for.</summary>
    public string? BoostNote
    {
        get
        {
            if (!IsOn)
            {
                return null;
            }

            if (_session is { Latest: { } now } session)
            {
                int used = (int)(now.InUse / (1024 * 1024));
                int total = (int)(now.Total / (1024 * 1024));
                return $"Watching {session.Name}: {VramAdvice.Gb(used)} of {VramAdvice.Gb(total)} GB in use.";
            }

            var latest = _page.Store.Current.Gaming.VramGames.FirstOrDefault();
            return latest is null
                ? "Waiting for a game from your library."
                : $"{latest.Name}: {VramAdvice.FitText(VramAdvice.Fit(latest)).ToLowerInvariant()}, {VramAdvice.PeakText(latest)}.";
        }
    }

    /// <summary>Turns watching on, or off (what it learned is kept).</summary>
    public Task<TweakResult> SetEnabledAsync(bool on)
    {
        if (!IsSupported)
        {
            return Task.FromResult(new TweakResult(false, "Not available on this computer."));
        }

        if (on != IsOn)
        {
            IsBusy = true;
            if (!on)
            {
                End(DateTime.UtcNow);
            }

            _page.Store.Update("Gaming", s => s.Gaming.VramManagement = on);
            _problem = null;
            Log.Info(LogCategory, on ? "Intelligent VRAM management turned on." : "Intelligent VRAM management turned off.");
            IsBusy = false;
            Changed();
        }

        return Task.FromResult(new TweakResult(true, on ? "Watching graphics memory while you play." : "Stopped watching graphics memory."));
    }

    /// <summary>One reading from the app: starts, feeds or ends the game's session.</summary>
    public void Tick(VramTick tick, DateTime now)
    {
        if (!IsOn)
        {
            End(now);
            return;
        }

        string? problem = tick.Problem;
        if (tick.ProcessId is not int pid || tick.ProgramPath is not { Length: > 0 } path)
        {
            if (_session is not null && now - _lastSeen > EndAfter)
            {
                End(now);
            }

            SetProblem(null);
            return;
        }

        var game = GameLibrary.FindRunning(_page.LibrarySnapshot(), [new RunningProcess(pid, path)])?.Game;
        if (game is null)
        {
            if (_session is not null && now - _lastSeen > EndAfter)
            {
                End(now);
            }

            return; // a fullscreen program that isn't one of the games: not watched
        }

        if (_session is not null && !string.Equals(_session.GameId, game.Id, StringComparison.OrdinalIgnoreCase))
        {
            End(now);
        }

        _session ??= new VramSession(game.Id, game.Name, now);
        _lastSeen = now;
        if (tick.Sample is { } sample)
        {
            _session.Add(sample, now);
        }

        SetProblem(problem);
        Changed();
    }

    /// <summary>The card the Details pill opens: one line per game with its fit and recommendation.</summary>
    public (string Summary, IReadOnlyList<DetailRow> Rows, string? Note, string? Empty) Details()
    {
        var records = _page.Store.Current.Gaming.VramGames;
        var rows = records
            .Select(r =>
            {
                var fit = VramAdvice.Fit(r);
                string color = fit switch
                {
                    VramFit.Over => ColorKeys.ToneHot,
                    VramFit.Tight => ColorKeys.ToneWarm,
                    _ => ColorKeys.ToneCool,
                };
                return new DetailRow(r.Name, $"{VramAdvice.FitText(fit)} · {VramAdvice.PeakText(r)}", color, VramAdvice.Advice(r));
            })
            .ToList();
        string summary = !IsOn
            ? "Off. Turn it on and Coreloom watches the graphics memory of each game you play, then recommends texture settings for it."
            : BoostNote ?? string.Empty;
        const string note = "Coreloom reads Windows' graphics-memory counters every two seconds while one of your games is in front; nothing in the game or the driver is changed. When memory runs out, Windows moves textures to system memory, which shows as pop-in and stutter.";
        return (summary, rows, note, rows.Count == 0 ? "No games measured yet. Play one of your games for a few minutes." : null);
    }

    private void End(DateTime now)
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        if (session.Samples >= MinimumSamples)
        {
            var record = session.ToRecord(_page.Store.Current.Gaming.VramFor(session.GameId), now);
            _page.Store.Update("Gaming", s => s.Gaming.KeepVram(record));
            Log.Info(LogCategory, $"Graphics memory in {record.Name}: {VramAdvice.PeakText(record)} ({VramAdvice.FitText(VramAdvice.Fit(record))}; " +
                $"the game itself {VramAdvice.Gb(record.PeakGameMb)} GB, {VramAdvice.Gb(record.PeakSharedMb)} GB in system memory, " +
                $"{record.NearFullPercent} % of the time at 95 % or more).");
        }

        Changed();
    }

    private void SetProblem(string? problem)
    {
        if (problem != _problem)
        {
            if (problem is not null)
            {
                Log.WarnThrottled("vram-watch", TimeSpan.FromMinutes(10), LogCategory, $"Graphics memory couldn't be read: {problem}");
            }

            _problem = problem is null ? null : $"Graphics memory couldn't be read: {problem}";
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(BoostNote));
    }
}
