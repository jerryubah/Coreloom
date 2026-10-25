using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Coreloom.Mac.Services;

/// <summary>What Coreloom needs to know about the user's session: how long since the last keypress or click, and whether
/// this launch is the login item opening Coreloom as the user signs in.</summary>
public static partial class MacSession
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int CombinedSessionState = 0;
    private const uint AnyInputEvent = uint.MaxValue;

    /// <summary>Time since the last keyboard, mouse or trackpad input (no permission needed; zero when unknown).</summary>
    public static TimeSpan SinceLastInput()
    {
        try
        {
            double seconds = CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInputEvent);
            return double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// A login item can't be given arguments, so a launch counts as "at login" when Open at login is on and the user's
    /// session started moments ago (the Dock starts as the user signs in).
    /// </summary>
    public static bool LaunchedAtLogin(bool openAtLogin, TimeSpan? sinceSessionStart = null)
    {
        if (!openAtLogin)
        {
            return false;
        }

        var elapsed = sinceSessionStart ?? SinceSessionStart();
        return elapsed is { } value && value >= TimeSpan.Zero && value < TimeSpan.FromMinutes(2);
    }

    private static TimeSpan? SinceSessionStart()
    {
        try
        {
            var docks = Process.GetProcessesByName("Dock");
            try
            {
                return docks.Length == 0 ? null : DateTime.Now - docks.Min(d => d.StartTime);
            }
            finally
            {
                foreach (var dock in docks)
                {
                    dock.Dispose();
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    [LibraryImport(CoreGraphics)]
    private static partial double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
}

/// <summary>
/// Notices the Mac waking from sleep: the monotonic clock stops while the Mac sleeps and the wall clock doesn't, so a
/// check every few seconds that finds the wall clock far ahead knows a sleep happened in between.
/// </summary>
public sealed class WakeWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(20);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _monotonic = Stopwatch.StartNew();
    private DateTime _wall = DateTime.UtcNow;
    private TimeSpan _last;

    public WakeWatcher(Action woke)
    {
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) =>
        {
            var now = DateTime.UtcNow;
            var elapsed = _monotonic.Elapsed;
            if (Slept(now - _wall, elapsed - _last))
            {
                woke();
            }

            _wall = now;
            _last = elapsed;
        });
    }

    /// <summary>True when the wall clock moved much further than the monotonic clock (which stops during sleep).</summary>
    public static bool Slept(TimeSpan wall, TimeSpan monotonic) => wall - monotonic > Threshold;

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Stop();
}
