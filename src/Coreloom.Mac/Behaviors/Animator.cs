using System.Diagnostics;
using Avalonia.Threading;

namespace Coreloom.Mac.Behaviors;

/// <summary>
/// A tiny tweening engine for the interface's motion (numbers counting, cards rising into place, gauges sweeping): one
/// shared timer at the display's pace drives every running tween, and stops when nothing moves. Tweens run on the UI
/// thread and can be cancelled or replaced at any time.
/// </summary>
public static class Animator
{
    private static readonly List<Tween> Running = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;

    /// <summary>Frames per second the shared timer runs at (120: ProMotion displays; others simply show every other frame).</summary>
    public static int FrameRate { get; set; } = 120;

    /// <summary>
    /// Runs <paramref name="step"/> with the eased progress (0 → 1) every frame for <paramref name="duration"/>, then
    /// <paramref name="completed"/>. Returns a handle that stops it.
    /// </summary>
    public static Tween Run(TimeSpan duration, Func<double, double> ease, Action<double> step, Action? completed = null)
    {
        var tween = new Tween(Clock.Elapsed, duration, ease, step, completed);
        if (duration <= TimeSpan.Zero)
        {
            step(1);
            completed?.Invoke();
            tween.IsFinished = true;
            return tween;
        }

        Running.Add(tween);
        step(ease(0));
        EnsureTimer();
        return tween;
    }

    /// <summary>A tween that never finishes on its own (a pulse, a spinner): <paramref name="step"/> gets the seconds since it began.</summary>
    public static Tween Loop(Action<double> step)
    {
        var tween = new Tween(Clock.Elapsed, TimeSpan.MaxValue, t => t, _ => { }, null) { LoopStep = step };
        Running.Add(tween);
        EnsureTimer();
        return tween;
    }

    private static void EnsureTimer()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(FrameRate, 30, 240)), DispatcherPriority.Render, (_, _) => Tick());
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private static void Tick()
    {
        var now = Clock.Elapsed;
        for (int i = Running.Count - 1; i >= 0; i--)
        {
            var tween = Running[i];
            if (tween.IsCancelled)
            {
                Running.RemoveAt(i);
                continue;
            }

            try
            {
                if (tween.LoopStep is { } loop)
                {
                    loop((now - tween.Start).TotalSeconds);
                    continue;
                }

                double t = Math.Clamp((now - tween.Start).TotalMilliseconds / tween.Duration.TotalMilliseconds, 0, 1);
                tween.Step(tween.Ease(t));
                if (t >= 1)
                {
                    Running.RemoveAt(i);
                    tween.IsFinished = true;
                    tween.Completed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // One broken animation must never stop the others (or the app).
                Running.RemoveAt(i);
                Coreloom.Core.Diagnostics.Log.WarnThrottled("animation", TimeSpan.FromMinutes(5), "UI", "An animation failed.", ex);
            }
        }

        if (Running.Count == 0)
        {
            _timer?.Stop();
        }
    }

    public sealed class Tween
    {
        internal Tween(TimeSpan start, TimeSpan duration, Func<double, double> ease, Action<double> step, Action? completed)
        {
            Start = start;
            Duration = duration;
            Ease = ease;
            Step = step;
            Completed = completed;
        }

        internal TimeSpan Start { get; }

        internal TimeSpan Duration { get; }

        internal Func<double, double> Ease { get; }

        internal Action<double> Step { get; }

        internal Action? Completed { get; }

        internal Action<double>? LoopStep { get; init; }

        public bool IsCancelled { get; private set; }

        public bool IsFinished { get; internal set; }

        public void Cancel() => IsCancelled = true;
    }
}

/// <summary>Easing curves (progress 0 → 1 in, eased progress out).</summary>
public static class Ease
{
    public static double Linear(double t) => t;

    public static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);

    public static double CubicInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    public static double QuadInOut(double t) => t < 0.5 ? 2 * t * t : 1 - (Math.Pow((-2 * t) + 2, 2) / 2);

    public static double SineInOut(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

    /// <summary>Overshoots a little and settles (BackEase out).</summary>
    public static Func<double, double> BackOut(double amplitude = 0.35) => t =>
    {
        double c1 = 1.70158 * amplitude;
        double c3 = c1 + 1;
        return 1 + (c3 * Math.Pow(t - 1, 3)) + (c1 * Math.Pow(t - 1, 2));
    };

    /// <summary>A springy settle with one overshoot (WPF's ElasticEase, one oscillation).</summary>
    public static double ElasticOut(double t)
    {
        if (t <= 0 || t >= 1)
        {
            return Math.Clamp(t, 0, 1);
        }

        return 1 - (Math.Exp(-5 * t) * Math.Cos(2 * Math.PI * 0.75 * t));
    }
}
