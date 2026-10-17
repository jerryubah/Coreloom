using Coreloom.Core.History;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

public class TelemetryEngineTests
{
    private static (TelemetryEngine Engine, FakeClock Clock) Create(params ITelemetryProvider[] providers)
    {
        var clock = new FakeClock();
        var engine = new TelemetryEngine(providers, new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemCapabilities.Unknown, clock);
        return (engine, clock);
    }

    [Fact]
    public void ATemporaryInterval_SlowsPolling_WithoutResizingHistory()
    {
        var (engine, _) = Create();
        engine.Interval = TimeSpan.FromSeconds(1);
        int capacity = engine.History.Capacity;
        engine.TemporaryInterval = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(2), engine.EffectiveInterval);
        Assert.Equal(capacity, engine.History.Capacity);
        engine.TemporaryInterval = TimeSpan.FromMilliseconds(500); // never faster than the chosen interval
        Assert.Equal(TimeSpan.FromSeconds(1), engine.EffectiveInterval);
        engine.TemporaryInterval = TimeSpan.FromMinutes(5); // clamped
        Assert.Equal(TelemetryEngine.MaximumInterval, engine.EffectiveInterval);
        engine.TemporaryInterval = null;
        Assert.Equal(TimeSpan.FromSeconds(1), engine.EffectiveInterval);
        Assert.Null(engine.TemporaryInterval);
    }

    [Theory]
    [InlineData(1000, true, true, 2000, false, 2000)]
    [InlineData(1000, true, true, 2000, true, 1000)] // the overlay's readings keep updating every second
    [InlineData(2000, true, true, 5000, true, 2000)] // never faster than chosen
    [InlineData(5000, true, true, 2000, false, 5000)]
    [InlineData(500, true, false, 2000, false, 500)]
    [InlineData(500, false, true, 2000, false, 500)]
    public void AdaptiveIntervals(int chosen, bool game, bool lighter, int gaming, bool overlay, int expected) =>
        Assert.Equal(expected, AdaptiveMonitoring.IntervalMs(chosen, game, lighter, gaming, overlay));

    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, false, false)] // an ordinary window
    [InlineData(true, true, false, true, false)] // the desktop fills the screen too
    [InlineData(false, true, true, false, false)] // Coreloom itself
    public void GameInFront(bool other, bool fills, bool known, bool desktop, bool expected) =>
        Assert.Equal(expected, AdaptiveMonitoring.IsGameInFront(other, fills, known, desktop));

    [Fact]
    public void HigherPrioritySourceWins_ForSameCanonicalMetric()
    {
        var low = new FakeProvider("low").Define("gpu/0/temp", MetricCategory.Gpu, MetricKind.Temperature, priority: 10);
        var high = new FakeProvider("high").Define("gpu/0/temp", MetricCategory.Gpu, MetricKind.Temperature, priority: 100);
        low.Values["gpu/0/temp"] = 40;
        high.Values["gpu/0/temp"] = 38;
        var (engine, _) = Create(low, high);

        engine.InitializeAll();
        var frame = engine.PollOnce();

        Assert.True(frame.TryGetSample("gpu/0/temp", out var sample));
        Assert.Equal(38, sample.Value);
        Assert.Equal("high", sample.Source.Provider);
        Assert.Same(sample.Source, engine.Catalog.Get("gpu/0/temp")!.ActiveSource);
    }

    [Fact]
    public void LowerPrioritySourceIsUsed_WhenHigherOneHasNoValue()
    {
        var low = new FakeProvider("low").Define("x", priority: 1);
        var high = new FakeProvider("high").Define("x", priority: 9);
        low.Values["x"] = 5;
        var (engine, _) = Create(low, high);

        engine.InitializeAll();
        var frame = engine.PollOnce();

        Assert.Equal(5, frame.GetValue("x"));
    }

    [Fact]
    public void NonFiniteValues_AreDropped()
    {
        var p = new FakeProvider("p").Define("a").Define("b");
        p.Values["a"] = double.NaN;
        p.Values["b"] = 1;
        var (engine, _) = Create(p);

        engine.InitializeAll();
        var frame = engine.PollOnce();

        Assert.False(frame.Contains("a"));
        Assert.True(frame.Contains("b"));
    }

    [Fact]
    public void TrackedMetrics_AreAppendedToHistory_UntrackedAreNot()
    {
        var p = new FakeProvider("p").Define("tracked").Define("raw", trackHistory: false);
        p.Values["tracked"] = 1;
        p.Values["raw"] = 2;
        var (engine, clock) = Create(p);

        engine.InitializeAll();
        engine.PollOnce();
        clock.Advance(1000);
        engine.PollOnce();

        Assert.Equal(2, engine.History.Get("tracked")!.Count);
        Assert.Null(engine.History.Get("raw"));
    }

    [Fact]
    public void SlowProviders_CarryForwardValuesBetweenPolls()
    {
        var slow = new FakeProvider("slow") { MinimumPollInterval = TimeSpan.FromSeconds(5) }.Define("disk/0/temp");
        slow.Values["disk/0/temp"] = 35;
        var (engine, clock) = Create(slow);

        engine.InitializeAll();
        engine.PollOnce();
        clock.Advance(1000);
        var second = engine.PollOnce();

        Assert.Equal(1, slow.PollCalls);
        Assert.Equal(35, second.GetValue("disk/0/temp"));

        clock.Advance(5000);
        slow.Values["disk/0/temp"] = 36;
        var third = engine.PollOnce();
        Assert.Equal(2, slow.PollCalls);
        Assert.Equal(36, third.GetValue("disk/0/temp"));
    }

    [Fact]
    public void RepeatedPollFailures_FaultProvider_ThenReinitializeAfterBackoff()
    {
        var p = new FakeProvider("flaky").Define("v");
        p.Values["v"] = 1;
        var (engine, clock) = Create(p);
        engine.InitializeAll();

        p.ThrowOnPoll = true;
        for (int i = 0; i < 3; i++)
        {
            engine.PollOnce();
            clock.Advance(1000);
        }

        Assert.Equal(ProviderState.Faulted, engine.ProviderStatuses[0].State);
        Assert.Equal(1, p.ShutdownCalls);

        // Still inside back-off window: no re-init.
        engine.PollOnce();
        Assert.Equal(1, p.InitializeCalls);

        p.ThrowOnPoll = false;
        clock.Advance(TelemetryEngine.BackoffMilliseconds(1));
        var frame = engine.PollOnce();

        Assert.Equal(2, p.InitializeCalls);
        Assert.Equal(ProviderState.Running, engine.ProviderStatuses[0].State);
        Assert.Equal(1, frame.GetValue("v"));
    }

    [Fact]
    public void FailedInitialization_IsRetriedWithBackoff()
    {
        var p = new FakeProvider("broken") { ThrowOnInitialize = true }.Define("v");
        var (engine, clock) = Create(p);

        engine.InitializeAll();
        Assert.Equal(ProviderState.Faulted, engine.ProviderStatuses[0].State);

        p.ThrowOnInitialize = false;
        clock.Advance(TelemetryEngine.BackoffMilliseconds(1) + 1);
        engine.PollOnce();
        Assert.Equal(ProviderState.Running, engine.ProviderStatuses[0].State);
    }

    [Fact]
    public void UnavailableProviders_AreNeverPolled()
    {
        var p = new FakeProvider("nvml") { InitResult = ProviderInitResult.Unavailable("No NVIDIA driver") }.Define("v");
        p.Values["v"] = 1;
        var (engine, _) = Create(p);

        engine.InitializeAll();
        engine.PollOnce();

        Assert.Equal(0, p.PollCalls);
        Assert.Equal(ProviderState.Unavailable, engine.ProviderStatuses[0].State);
        Assert.Equal("No NVIDIA driver", engine.ProviderStatuses[0].Message);
    }

    [Fact]
    public void DisabledSection_SkipsProvider_AndFiltersMetricsByCategory()
    {
        var gpu = new FakeProvider("gpu", MonitoringSection.Gpu).Define("gpu/0/load", MetricCategory.Gpu);
        var mixed = new FakeProvider("lhm").Define("gpu/0/hotspot", MetricCategory.Gpu).Define("cpu/temp", MetricCategory.Cpu);
        gpu.Values["gpu/0/load"] = 50;
        mixed.Values["gpu/0/hotspot"] = 60;
        mixed.Values["cpu/temp"] = 70;
        var (engine, _) = Create(gpu, mixed);

        engine.InitializeAll();
        engine.SetSectionEnabled(MonitoringSection.Gpu, false);
        var frame = engine.PollOnce();

        Assert.Equal(0, gpu.PollCalls);
        Assert.False(frame.Contains("gpu/0/hotspot"));
        Assert.True(frame.Contains("cpu/temp"));

        engine.SetSectionEnabled(MonitoringSection.Gpu, true);
        frame = engine.PollOnce();
        Assert.True(frame.Contains("gpu/0/load"));
        Assert.True(frame.Contains("gpu/0/hotspot"));
    }

    [Fact]
    public void InitStages_AreRespected()
    {
        var log = new List<int>();
        var later = new FakeProvider("later", stage: 1) { InitOrderLog = log };
        var first = new FakeProvider("first", stage: 0) { InitOrderLog = log };
        var (engine, _) = Create(later, first);

        engine.InitializeAll();

        Assert.Equal(new[] { 0, 1 }, log);
    }

    [Fact]
    public void Interval_IsClampedAndResizesHistory()
    {
        var (engine, _) = Create();
        engine.Interval = TimeSpan.FromMilliseconds(10);
        Assert.Equal(TelemetryEngine.MinimumInterval, engine.Interval);
        Assert.Equal(HistoryStore.CapacityFor(TelemetryEngine.MinimumInterval), engine.History.Capacity);

        engine.Interval = TimeSpan.FromMinutes(5);
        Assert.Equal(TelemetryEngine.MaximumInterval, engine.Interval);
    }

    [Fact]
    public void FrameProduced_IsRaised_AndSubscriberExceptionsAreContained()
    {
        var p = new FakeProvider("p").Define("v");
        p.Values["v"] = 3;
        var (engine, _) = Create(p);
        int received = 0;
        engine.FrameProduced += _ => throw new InvalidOperationException("bad subscriber");
        engine.FrameProduced += f => received += f.Count;

        engine.InitializeAll();
        engine.PollOnce();

        Assert.Equal(1, received);
        Assert.Equal(1, engine.LatestFrame.Sequence);
    }

    [Fact]
    public void BackgroundThread_ProducesFrames()
    {
        var p = new FakeProvider("p").Define("v");
        p.Values["v"] = 1;
        using var engine = new TelemetryEngine([p], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemCapabilities.Unknown);
        engine.Interval = TelemetryEngine.MinimumInterval;
        using var produced = new ManualResetEventSlim();
        int frames = 0;
        engine.FrameProduced += f =>
        {
            if (f.Contains("v") && Interlocked.Increment(ref frames) >= 2)
            {
                produced.Set();
            }
        };

        engine.Start();
        Assert.True(produced.Wait(TimeSpan.FromSeconds(10)), "Expected frames from the polling thread.");
        engine.Stop();
        Assert.False(engine.IsRunning);
    }
}
