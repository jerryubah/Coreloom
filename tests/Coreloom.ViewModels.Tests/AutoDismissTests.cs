using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Tests;

/// <summary>
/// Stands in for the five-second timer: dismissals wait here until <see cref="Elapse"/>. It applies only to the test that
/// creates it (and what that test awaits).
/// </summary>
internal sealed class ManualDismissals : IDisposable
{
    private readonly List<Pending> _pending = [];

    public ManualDismissals()
    {
        AutoDismiss.Scheduler = (delay, dismiss) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(5), delay);
            var pending = new Pending(dismiss);
            _pending.Add(pending);
            return pending;
        };
    }

    /// <summary>How many messages are waiting to leave.</summary>
    public int Waiting => _pending.Count(p => !p.Cancelled);

    /// <summary>Five seconds pass: every waiting message leaves.</summary>
    public void Elapse()
    {
        var due = _pending.Where(p => !p.Cancelled).ToList();
        _pending.Clear();
        foreach (var pending in due)
        {
            pending.Dismiss();
        }
    }

    public void Dispose() => AutoDismiss.Scheduler = null;

    private sealed class Pending(Action dismiss) : IDisposable
    {
        public bool Cancelled { get; private set; }

        public void Dismiss() => dismiss();

        public void Dispose() => Cancelled = true;
    }
}

public sealed class AutoDismissTests
{
    [Fact]
    public void ADismissal_StartsOverWhenTheMessageChanges_AndCanBeKept()
    {
        using var timer = new ManualDismissals();
        var dismissal = new Dismissal();
        string? message = "first";

        dismissal.Update(true, () => message = null);
        dismissal.Update(true, () => message = null); // a new message: the five seconds start over
        Assert.Equal(1, timer.Waiting);

        dismissal.Update(false, () => message = null); // nothing showing: nothing to dismiss
        Assert.Equal(0, timer.Waiting);

        dismissal.Update(true, () => message = null);
        dismissal.Cancel();
        timer.Elapse();
        Assert.Equal("first", message);

        dismissal.Update(true, () => message = null);
        timer.Elapse();
        Assert.Null(message);
    }

    [Fact]
    public void WithoutATimerOrUiThread_MessagesStay()
    {
        Assert.Null(AutoDismiss.Scheduler);
        Assert.Null(AutoDismiss.After(() => throw new InvalidOperationException("must not run")));
    }

    [Fact]
    public async Task TheTimer_RunsTheDismissalOnTheUiThread()
    {
        var context = new RecordingContext();
        var ran = new TaskCompletionSource();
        using var pending = AutoDismiss.After(ran.SetResult, context, TimeSpan.FromMilliseconds(50));
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, context.Posts);
    }

    [Fact]
    public async Task ACancelledTimer_NeverDismisses()
    {
        var context = new RecordingContext();
        bool ran = false;
        AutoDismiss.After(() => ran = true, context, TimeSpan.FromMilliseconds(50)).Dispose();
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Assert.False(ran);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => _posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            d(state);
        }
    }
}
