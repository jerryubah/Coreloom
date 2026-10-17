using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;

namespace Coreloom.ViewModels.Pages;

/// <summary>What the app hands adaptive pacing about once a second: the program in front and its newest second of frames.</summary>
/// <param name="ProcessId">The game in front (null when none).</param>
/// <param name="ProgramPath">Its program file.</param>
/// <param name="Second">Its newest second of frames, when measured.</param>
/// <param name="Problem">Why frames can't be measured (e.g. Windows' graphics events aren't available), or null.</param>
public readonly record struct PacingTick(int? ProcessId, string? ProgramPath, PacingSecond? Second, string? Problem = null);

/// <summary>
/// Adaptive frame pacing on the Gaming page: while a library game is in front it measures the game's frames (handed in
/// by the app once a second), lets <see cref="AdaptivePacingSession"/> work out a steady limit, and sets it as the
/// game's own limit in the NVIDIA driver — only after the user turned adaptive pacing on, never for a game with a limit
/// the user set, and a step at a time. Without an NVIDIA driver it suggests the limit instead. Turning it off removes
/// the limits it set. What it learns per game is kept in settings, and each change is written to the log.
/// </summary>
public sealed class AdaptivePacingController : ObservableObject
{
    private const string LogCategory = "Gaming";

    private readonly GamingViewModel _page;
    private AdaptivePacingSession? _session;
    private int? _processId;
    private string? _gameId;
    private string? _gameName;
    private string? _skipReason;
    private bool _applying;
    private bool _suggested;
    private bool _loading;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string? _lastChangeText;

    public AdaptivePacingController(GamingViewModel page)
    {
        _page = page;
        _statusText = IdleText;
    }

    public bool IsOn => _page.Store.Current.Gaming.AdaptivePacing;

    /// <summary>Limits are set in the NVIDIA driver; elsewhere they're suggested.</summary>
    public bool CanApply => _page.CurrentStatus.HasNvidiaDriver;

    /// <summary>Turning on or off (removing the limits it set).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The library id of the game being paced now, or null.</summary>
    public string? GameId => _gameId;

    /// <summary>The game being paced now, or null.</summary>
    public string? GameName
    {
        get => _gameName;
        private set => SetProperty(ref _gameName, value);
    }

    /// <summary>What adaptive pacing is doing now ("Measuring Cyberpunk 2077: 20 of 45 seconds of play.").</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The last limit it set or suggested, with why.</summary>
    public string? LastChangeText
    {
        get => _lastChangeText;
        private set
        {
            if (SetProperty(ref _lastChangeText, value))
            {
                OnPropertyChanged(nameof(HasLastChange));
            }
        }
    }

    public bool HasLastChange => !string.IsNullOrEmpty(_lastChangeText);

    /// <summary>The session in progress (for tests).</summary>
    internal AdaptivePacingSession? Session => _session;

    private string IdleText => !IsOn
        ? "Off"
        : CanApply
            ? "On · waiting for a game from your library"
            : "On · waiting for a game (it suggests limits; setting them needs NVIDIA)";

    /// <summary>Turns adaptive pacing on, or off (removing the limits it set; what it learned is kept).</summary>
    public async Task SetEnabledAsync(bool on)
    {
        if (on == IsOn || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _page.Store.Update("Gaming", s => s.Gaming.AdaptivePacing = on);
            Log.Info(LogCategory, on ? "Adaptive frame pacing turned on." : "Adaptive frame pacing turned off.");
            if (on)
            {
                await _page.EnsureLoadedAsync();
            }
            else
            {
                End();
                var adaptive = _page.Store.Current.Gaming.GameFrameCaps.Where(c => c.Adaptive).ToList();
                int removed = 0;
                foreach (var cap in adaptive)
                {
                    if ((await _page.ApplyGameCapAsync(cap.GameId, 0, 0, adaptive: true)).Success)
                    {
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    LastChangeText = $"Removed the {(removed == 1 ? "limit" : $"{removed.ToString(CultureInfo.CurrentCulture)} limits")} adaptive pacing set; what it learned is kept for next time.";
                    Log.Info(LogCategory, $"Adaptive pacing: removed {removed} game limits it had set.");
                }
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsOn));
            StatusText = IdleText;
            _page.FramePacer.Refresh();
        }
    }

    /// <summary>Called about once a second by the app while adaptive pacing is on (and once more when it goes off).</summary>
    public async Task TickAsync(PacingTick tick)
    {
        if (!IsOn)
        {
            End();
            StatusText = IdleText;
            return;
        }

        if (!_page.IsLoaded)
        {
            StatusText = "Reading your game library…";
            if (!_loading)
            {
                _loading = true;
                try
                {
                    await _page.EnsureLoadedAsync();
                }
                finally
                {
                    _loading = false;
                }
            }

            return;
        }

        if (tick.ProcessId is not int pid)
        {
            End();
            StatusText = IdleText;
            return;
        }

        if (pid != _processId)
        {
            End();
            Begin(pid, tick.ProgramPath);
        }

        if (_session is null)
        {
            StatusText = _skipReason ?? IdleText;
            return;
        }

        if (tick.Problem is { Length: > 0 } problem)
        {
            StatusText = $"Frame times can't be measured: {problem}";
            return;
        }

        if (tick.Second is { } second)
        {
            _session.Add(second);
        }

        var advice = _session.Advise();
        if (advice.Action != PacingAction.None && !_applying && !_suggested)
        {
            await ApplyAsync(advice);
            return;
        }

        StatusText = _suggested && !CanApply
            ? $"{_gameName}: suggested {advice.Target.ToString(CultureInfo.CurrentCulture)} FPS. Set it in AMD Software (Frame Rate Target Control) or in the game."
            : $"{_gameName}: {advice.Reason}";
    }

    /// <summary>The game's frame-generation setting changed: the session's limits follow.</summary>
    internal void FrameGenerationChanged(string gameId)
    {
        if (_session is not null && string.Equals(gameId, _gameId, StringComparison.OrdinalIgnoreCase))
        {
            _session.Limits = Limits(_page.Store.Current.Gaming.PacingFor(gameId));
        }
    }

    private void Begin(int pid, string? path)
    {
        _processId = pid;
        _suggested = false;
        var running = path is null ? null : GameLibrary.FindRunning(_page.LibrarySnapshot(), [new RunningProcess(pid, path)]);
        if (running is null)
        {
            string program = GameExecutables.FileName(path) ?? "The program in front";
            _skipReason = $"{program} isn't in your library. Add it with Add game and adaptive pacing will learn it.";
            return;
        }

        var game = running.Game;
        _gameId = game.Id;
        GameName = game.Name;
        var settings = _page.Store.Current.Gaming;
        var own = settings.CapFor(game.Id);
        if (own is { Adaptive: false })
        {
            _skipReason = $"{game.Name} has a limit you set ({own.Fps.ToString(CultureInfo.CurrentCulture)} FPS); adaptive pacing leaves it alone. Remove it to let adaptive pacing take over.";
            return;
        }

        var learned = settings.PacingFor(game.Id);
        int cap = own?.Fps ?? (CanApply ? _page.CurrentStatus.FrameCap : 0);
        _session = new AdaptivePacingSession(cap, Limits(learned), learned?.FailedAt ?? 0);
        Log.Info(LogCategory, $"Adaptive pacing: measuring {game.Name} ({(cap > 0 ? $"limit {cap} FPS" : "no limit")}, {_session.Limits.RefreshHz} Hz, {(_session.Limits.VariableRefresh ? "variable refresh" : "fixed refresh")}, frame generation {(_session.Limits.Factor > 1 ? $"{_session.Limits.Factor}×" : "off")}).");
    }

    /// <summary>Ends the session: remembers what was found for the game.</summary>
    private void End()
    {
        if (_session is { } session && _gameId is { } id)
        {
            string name = _gameName ?? id;
            int sustainable = session.SustainableFps;
            int failedAt = session.FailedAt;
            if (session.Seconds > 0 || sustainable > 0)
            {
                _page.Store.Update("Gaming", s =>
                {
                    var game = Learned(s.Gaming, id, name);
                    game.Sessions++;
                    if (sustainable > 0)
                    {
                        game.Sustainable = sustainable;
                    }

                    game.FailedAt = failedAt;
                });
                Log.Info(LogCategory, $"Adaptive pacing: {name} closed after {session.Seconds} s measured{(sustainable > 0 ? $", sustaining about {sustainable} FPS" : string.Empty)}.");
            }
        }

        _session = null;
        _processId = null;
        _gameId = null;
        _skipReason = null;
        GameName = null;
    }

    private async Task ApplyAsync(PacingAdvice advice)
    {
        var session = _session!;
        string id = _gameId!;
        string name = _gameName ?? id;
        int was = session.Cap;
        string verb = advice.Action switch { PacingAction.Lower => "Lowered", PacingAction.Raise => "Raised", _ => "Set" };
        string target = advice.Target.ToString(CultureInfo.CurrentCulture);
        _applying = true;
        try
        {
            if (!CanApply)
            {
                _suggested = true;
                RememberTarget(id, name, advice.Target, session.FailedAt);
                LastChangeText = $"Suggested {target} FPS for {name}. {advice.Reason}";
                StatusText = $"{name}: suggested {target} FPS. Set it in AMD Software (Frame Rate Target Control) or in the game.";
                Log.Info(LogCategory, $"Adaptive pacing: suggested {target} FPS for {name} (no NVIDIA driver to set it). {advice.Reason}");
                return;
            }

            var result = await _page.ApplyGameCapAsync(id, advice.Target, advice.Target, adaptive: true);
            if (result.Success)
            {
                session.Request(advice.Target);
                RememberTarget(id, name, advice.Target, session.FailedAt);
                string from = was > 0 ? $" (from {was.ToString(CultureInfo.CurrentCulture)} FPS)" : string.Empty;
                LastChangeText = $"{verb} {name}'s limit to {target} FPS{from}. {advice.Reason}";
                StatusText = $"{name}: {session.Advise().Reason}";
                Log.Info(LogCategory, $"Adaptive pacing: {verb.ToLowerInvariant()} {name}'s limit to {target} FPS{from}. {advice.Reason}");
            }
            else
            {
                _suggested = true; // don't try again every second
                StatusText = $"{name}: the driver didn't take the {target} FPS limit. {result.Message}";
                Log.Warn(LogCategory, $"Adaptive pacing: the driver didn't take {target} FPS for {name}: {result.Message}");
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void RememberTarget(string id, string name, int target, int failedAt) =>
        _page.Store.Update("Gaming", s =>
        {
            var game = Learned(s.Gaming, id, name);
            game.Target = target;
            game.FailedAt = failedAt;
            game.LastChanged = DateTime.UtcNow;
        });

    private PacingLimits Limits(AdaptivePacingGame? learned)
    {
        var status = _page.CurrentStatus;
        bool variable = status.HasNvidiaDriver ? status.Tweak(GamingTweak.GSync).IsOn : status.RefreshRate > 60;
        return new PacingLimits(status.RefreshRate, variable, learned?.FrameGeneration ?? 1);
    }

    internal static AdaptivePacingGame Learned(Coreloom.Core.Settings.GamingSettings settings, string id, string name)
    {
        var game = settings.PacingFor(id);
        if (game is null)
        {
            game = new AdaptivePacingGame { GameId = id, Name = name };
            settings.AdaptivePacingGames.Add(game);
        }

        game.Name = name;
        return game;
    }
}
