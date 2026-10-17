using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>One second of a game's frames, summed up.</summary>
public readonly record struct PacingSecond(double Fps, double MedianMs, double P99Ms, int Frames, int Spikes, double LongestMs, double GpuLoad = double.NaN)
{
    /// <summary>The second's 1% low (frames per second).</summary>
    public double LowFps => P99Ms > 0 ? 1000 / P99Ms : 0;

    /// <summary>
    /// Sums up the newest frames covering about one second (frame times in milliseconds, oldest first). A spike is a frame
    /// well over the typical one (as on the overlay's graph). Null with fewer than two frames.
    /// </summary>
    public static PacingSecond? From(ReadOnlySpan<double> frameTimes, double gpuLoad = double.NaN)
    {
        int start = frameTimes.Length;
        double covered = 0;
        while (start > 0 && covered < 1000)
        {
            double ms = frameTimes[start - 1];
            if (!double.IsFinite(ms) || ms <= 0)
            {
                break;
            }

            covered += ms;
            start--;
        }

        int n = frameTimes.Length - start;
        if (n < 2 || covered <= 0)
        {
            return null;
        }

        var sorted = frameTimes[start..].ToArray();
        Array.Sort(sorted);
        double median = sorted[n / 2];
        double p99 = sorted[Math.Clamp((int)Math.Ceiling(0.99 * (n - 1)), 0, n - 1)];
        int spikes = 0;
        foreach (double ms in frameTimes[start..])
        {
            if (ms > median * 1.6 && ms > median + 2)
            {
                spikes++;
            }
        }

        return new PacingSecond(n * 1000 / covered, median, p99, n, spikes, sorted[^1], gpuLoad);
    }
}

/// <summary>What bounds a game's pacing target: the display's refresh rate, variable refresh, and frame generation.</summary>
public readonly record struct PacingLimits(int RefreshHz, bool VariableRefresh, int FrameGeneration = 1)
{
    private int Refresh => RefreshHz > 0 ? RefreshHz : 60;

    /// <summary>Frames shown per frame rendered: 1 without frame generation, up to 4 (multi-frame generation).</summary>
    public int Factor => Math.Clamp(FrameGeneration, 1, 4);

    /// <summary>
    /// The highest target: just under the refresh rate with G-SYNC or FreeSync (so frames stay in the variable-refresh
    /// range), the refresh rate without. With frame generation, a whole number of shown frames per rendered frame.
    /// </summary>
    public int Ceiling
    {
        get
        {
            int top = VariableRefresh ? FrameCap.AutoTarget(Refresh) : Refresh;
            return Factor > 1 ? Math.Max(Factor * 10, top / Factor * Factor) : top;
        }
    }

    /// <summary>
    /// The lowest target: 30 FPS; with frame generation 40 rendered frames per second (below that generated frames add
    /// noticeable delay), never above the ceiling.
    /// </summary>
    public int Floor => Math.Min(Ceiling, Factor > 1 ? 40 * Factor : 30);

    /// <summary>Targets go in steps of 5 FPS with variable refresh (of 5 × the factor with frame generation).</summary>
    public int Step => 5 * Factor;

    /// <summary>
    /// The highest allowed target at or below <paramref name="fps"/>: a step with variable refresh; without it a whole
    /// fraction of the refresh rate (144, 72, 48), so every frame stays on screen for the same number of refreshes.
    /// Never below <see cref="Floor"/> or above <see cref="Ceiling"/>.
    /// </summary>
    public int SnapDown(double fps)
    {
        if (!double.IsFinite(fps) || fps <= 0)
        {
            return Floor;
        }

        if (fps >= Ceiling)
        {
            return Ceiling;
        }

        if (!VariableRefresh)
        {
            int best = 0;
            for (int n = 1; n <= 8; n++)
            {
                int candidate = Refresh / n;
                if (candidate < Floor)
                {
                    break;
                }

                best = candidate;
                if (candidate <= fps)
                {
                    return candidate;
                }
            }

            return best > 0 ? best : Floor;
        }

        int snapped = (int)Math.Floor(fps / Step) * Step;
        return Math.Clamp(snapped, Floor, Ceiling);
    }
}

public enum PacingAction
{
    None,

    /// <summary>The game has no limit of its own yet: set the one found.</summary>
    Set,

    /// <summary>The game can't hold its limit: lower it (by at most 15 % at a time).</summary>
    Lower,

    /// <summary>The game holds its limit easily with the graphics card well short of busy: try a step higher.</summary>
    Raise,
}

/// <summary>What the engine suggests now, and why (shown on the Gaming page and written to the log).</summary>
public readonly record struct PacingAdvice(PacingAction Action, int Target, int SustainableFps, string Reason);

/// <summary>
/// Adaptive frame pacing for one play session of one game: watches each second of frames (rolling two minutes), works
/// out the frame rate the game can sustain with steady 1% lows, and suggests a limit — set when the game has none,
/// lowered a step at a time when it keeps missing its limit, raised a step when it holds the limit easily with the
/// graphics card well short of busy. Changes are gradual and rare: none until enough play is measured, none smaller
/// than 5 %, none while a change is waiting to show in the frames, and never above a limit the game couldn't hold
/// before. Platform-neutral; the Gaming page applies what it suggests through the graphics driver.
/// </summary>
public sealed class AdaptivePacingSession
{
    /// <summary>Seconds of play kept (rolling).</summary>
    public const int WindowSeconds = 120;

    /// <summary>Seconds of play before the first suggestion (and after each change that shows).</summary>
    public const int MinimumSeconds = 45;

    /// <summary>Seconds of play after a change to see whether it shows in the frames.</summary>
    public const int VerifySeconds = 12;

    private readonly List<PacingSecond> _window = [];
    private readonly List<PacingSecond> _verify = [];
    private PacingLimits _limits;

    public AdaptivePacingSession(int cap, PacingLimits limits, int failedAt = 0)
    {
        Cap = cap > 0 ? FrameCap.Clamp(cap) : 0;
        _limits = limits;
        FailedAt = failedAt > 0 ? failedAt : 0;
    }

    /// <summary>The limit pacing the frames now (0 = none).</summary>
    public int Cap { get; private set; }

    /// <summary>A new limit waiting to show in the frames (0 = none).</summary>
    public int Requested { get; private set; }

    /// <summary>The last change didn't show while the game ran: it applies from the game's next start. No more changes this session.</summary>
    public bool WaitingForRestart { get; private set; }

    /// <summary>A limit the game couldn't hold (raising stays below it).</summary>
    public int FailedAt { get; private set; }

    public PacingLimits Limits
    {
        get => _limits;
        set => _limits = value;
    }

    /// <summary>Seconds of play in the window.</summary>
    public int Seconds => _window.Count;

    /// <summary>Seconds left out (loading screens, pauses, the game in the background).</summary>
    public int Skipped { get; private set; }

    /// <summary>The frame rate the game sustains with steady 1% lows, from the window (0 until measured).</summary>
    public int SustainableFps { get; private set; }

    /// <summary>
    /// Adds a second of play. Seconds that aren't gameplay — under 10 FPS, or with a frame over half a second (a
    /// loading screen or a pause) — are left out. Returns whether it counted.
    /// </summary>
    public bool Add(PacingSecond second)
    {
        if (!(second.Fps >= 10) || second.LongestMs > 500 || second.Frames < 2)
        {
            Skipped++;
            return false;
        }

        if (Requested > 0 && !WaitingForRestart)
        {
            _verify.Add(second);
            if (_verify.Count >= VerifySeconds)
            {
                Verify();
            }

            return true;
        }

        _window.Add(second);
        if (_window.Count > WindowSeconds)
        {
            _window.RemoveAt(0);
        }

        return true;
    }

    /// <summary>Records that <paramref name="target"/> was set; the frames then show whether it took effect.</summary>
    public void Request(int target)
    {
        if (Cap > 0 && target < Cap)
        {
            FailedAt = FailedAt > 0 ? Math.Min(FailedAt, Cap) : Cap;
        }

        Requested = FrameCap.Clamp(target);
        _verify.Clear();
    }

    /// <summary>What to do now, from the seconds measured.</summary>
    public PacingAdvice Advise()
    {
        string Fps(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
        if (WaitingForRestart)
        {
            return new PacingAdvice(PacingAction.None, Requested, SustainableFps, $"The {Requested} FPS limit applies from the game's next start.");
        }

        if (Requested > 0)
        {
            return new PacingAdvice(PacingAction.None, Requested, SustainableFps, $"Checking that the {Requested} FPS limit took effect.");
        }

        if (_window.Count < MinimumSeconds)
        {
            return new PacingAdvice(PacingAction.None, Cap, SustainableFps, $"Measuring: {_window.Count} of {MinimumSeconds} seconds of play.");
        }

        var fps = _window.Select(s => s.Fps).Order().ToArray();
        double p20 = Percentile(fps, 0.2);
        double median = Percentile(fps, 0.5);
        double low = Percentile(_window.Select(s => s.LowFps).Order().ToArray(), 0.5);
        var gpu = _window.Select(s => s.GpuLoad).Where(double.IsFinite).Order().ToArray();
        double gpuBusy = gpu.Length > 0 ? Percentile(gpu, 0.5) : double.NaN;

        // Sustainable: what the game reaches in 80 % of seconds, and no more than its 1% lows allow (lows within 15 %).
        double sustainable = Math.Min(p20, low / 0.85);
        SustainableFps = (int)Math.Round(sustainable);
        int found = _limits.SnapDown(sustainable);
        int ceiling = _limits.Ceiling;

        if (Cap <= 0)
        {
            return new PacingAdvice(PacingAction.Set, found, SustainableFps,
                $"Holds about {Fps(sustainable)} FPS with steady 1% lows (typically {Fps(median)}, lows {Fps(low)}); a {found} FPS limit keeps frame times even.");
        }

        double missRate = _window.Count(s => s.Fps < Cap * 0.95) / (double)_window.Count;
        bool steady = median >= Cap * 0.97 && missRate <= 0.1 && low >= Cap * 0.8;
        int hysteresis = Math.Max(3, (int)Math.Ceiling(Cap * 0.05));
        if (steady)
        {
            if (double.IsFinite(gpuBusy) && gpuBusy < 70 && Cap < ceiling)
            {
                int raise = _limits.SnapDown(Math.Min(ceiling, Cap * 1.1));
                if (FailedAt > 0 && raise >= FailedAt)
                {
                    raise = _limits.SnapDown(FailedAt - 1);
                }

                if (raise - Cap >= hysteresis)
                {
                    return new PacingAdvice(PacingAction.Raise, raise, SustainableFps,
                        $"Held {Cap} FPS steadily with the graphics card {Fps(gpuBusy)} % busy; trying {raise} FPS.");
                }
            }

            return new PacingAdvice(PacingAction.None, Cap, SustainableFps, $"Steady at {Cap} FPS (1% lows {Fps(low)}).");
        }

        int lower = Math.Max(found, _limits.SnapDown(Cap * 0.85));
        if (Cap - lower >= hysteresis)
        {
            return new PacingAdvice(PacingAction.Lower, lower, SustainableFps,
                $"Missed the {Cap} FPS limit in {Fps(missRate * 100)} % of the last {_window.Count} s (1% lows {Fps(low)}); {lower} FPS is steadier.");
        }

        return new PacingAdvice(PacingAction.None, Cap, SustainableFps, $"Close to the best limit at {Cap} FPS (1% lows {Fps(low)}).");
    }

    /// <summary>
    /// After a change: frames over the new (lower) limit in most seconds mean the driver keeps the old one until the game
    /// restarts. A raise is only seen when frames go over the old limit; otherwise it waits for the next start too.
    /// </summary>
    private void Verify()
    {
        bool lowering = Cap <= 0 || Requested < Cap;
        double over = lowering
            ? _verify.Count(s => s.Fps > Requested * 1.04) / (double)_verify.Count
            : _verify.Count(s => s.Fps > Cap * 1.04) / (double)_verify.Count;
        bool live = lowering ? over < 0.5 : over >= 0.3;
        _verify.Clear();
        if (live)
        {
            Cap = Requested;
            Requested = 0;
            _window.Clear(); // measure the game at its new limit
        }
        else
        {
            WaitingForRestart = true;
        }
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
}

/// <summary>What adaptive pacing remembers about a game between sessions.</summary>
public sealed class AdaptivePacingGame
{
    public string GameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Frames shown per frame rendered: 1 (off), 2, 3 or 4 (DLSS / FSR frame generation), set by the user.</summary>
    public int FrameGeneration { get; set; } = 1;

    /// <summary>The limit adaptive pacing set or suggested last (0 = none yet).</summary>
    public int Target { get; set; }

    /// <summary>A limit the game couldn't hold; raising stays below it (0 = none).</summary>
    public int FailedAt { get; set; }

    /// <summary>The frame rate the game sustained last time.</summary>
    public int Sustainable { get; set; }

    public int Sessions { get; set; }

    public DateTime? LastChanged { get; set; }

    public static List<AdaptivePacingGame> Normalize(List<AdaptivePacingGame>? games) =>
        (games ?? [])
            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.GameId))
            .Select(g =>
            {
                g.Name ??= string.Empty;
                g.FrameGeneration = Math.Clamp(g.FrameGeneration, 1, 4);
                g.Target = FrameCap.Clamp(g.Target);
                g.FailedAt = FrameCap.Clamp(g.FailedAt);
                g.Sustainable = Math.Clamp(g.Sustainable, 0, FrameCap.Maximum);
                g.Sessions = Math.Max(0, g.Sessions);
                return g;
            })
            .DistinctBy(g => g.GameId, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
