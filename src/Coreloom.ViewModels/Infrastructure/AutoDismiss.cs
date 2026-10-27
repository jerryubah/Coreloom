namespace Coreloom.ViewModels.Infrastructure;

/// <summary>
/// Error and warning messages leave by themselves after five seconds, so none stays on screen: a failed action's message,
/// a warning note, the update check that didn't get through. States (a health check that needs attention, a reading that's
/// unavailable) aren't messages and stay as long as they're true.
/// </summary>
public static class AutoDismiss
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>The UI thread's context, set by the app at startup. Without one (tests), messages stay until replaced.</summary>
    public static SynchronizationContext? Context { get; set; }

    private static readonly AsyncLocal<Func<TimeSpan, Action, IDisposable?>?> TestScheduler = new();

    /// <summary>
    /// Replaces the timer (tests): called with the delay and the dismissal; returns what cancels it. It applies to the code
    /// that set it (and what that code awaits), so tests running side by side don't see each other's.
    /// </summary>
    public static Func<TimeSpan, Action, IDisposable?>? Scheduler
    {
        get => TestScheduler.Value;
        set => TestScheduler.Value = value;
    }

    /// <summary>Runs <paramref name="dismiss"/> on the UI thread after <see cref="Delay"/>, unless the returned handle is disposed first.</summary>
    public static IDisposable? After(Action dismiss)
    {
        if (Scheduler is { } scheduler)
        {
            return scheduler(Delay, dismiss);
        }

        return Context is { } context ? After(dismiss, context, Delay) : null;
    }

    /// <summary>Runs <paramref name="dismiss"/> through <paramref name="context"/> after <paramref name="delay"/> unless cancelled.</summary>
    internal static IDisposable After(Action dismiss, SynchronizationContext context, TimeSpan delay) => new Pending(context, dismiss, delay);

    private sealed class Pending : IDisposable
    {
        private readonly Timer _timer;
        private volatile bool _cancelled;

        public Pending(SynchronizationContext context, Action dismiss, TimeSpan delay)
        {
            _timer = new Timer(_ => context.Post(_ =>
            {
                if (!_cancelled)
                {
                    dismiss();
                }
            }, null), null, delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _cancelled = true;
            _timer.Dispose();
        }
    }
}

/// <summary>One message's dismissal: scheduling again starts the five seconds over; <see cref="Cancel"/> keeps it.</summary>
public sealed class Dismissal
{
    private IDisposable? _pending;

    /// <summary>Dismisses after five seconds when <paramref name="showing"/> (a message is on screen), else cancels.</summary>
    public void Update(bool showing, Action dismiss)
    {
        _pending?.Dispose();
        _pending = showing ? AutoDismiss.After(dismiss) : null;
    }

    public void Cancel()
    {
        _pending?.Dispose();
        _pending = null;
    }
}
