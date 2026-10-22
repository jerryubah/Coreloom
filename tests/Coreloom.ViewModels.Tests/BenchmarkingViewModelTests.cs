using System.Diagnostics;
using Coreloom.Core.Benchmarks;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

internal sealed class FakeStressLoad(string name, string device) : IStressLoad
{
    private readonly Stopwatch _clock = new();
    private double _workBefore;
    private double _rate = 1000;
    private double _rateSince;

    public string Name { get; } = name;

    public string Device { get; } = device;

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public int Errors { get; set; }

    public string? Failure { get; set; }

    /// <summary>Work per second from now on (a slower rate is like throttling).</summary>
    public double Rate
    {
        get => _rate;
        set
        {
            double now = _clock.Elapsed.TotalSeconds;
            _workBefore += (now - _rateSince) * _rate;
            _rateSince = now;
            _rate = value;
        }
    }

    public long Work => (long)(_workBefore + ((_clock.Elapsed.TotalSeconds - _rateSince) * _rate));

    public void Start()
    {
        Started = true;
        _clock.Start();
    }

    public void Stop()
    {
        Stopped = true;
        _clock.Stop();
    }

    public void Dispose() => Stop();
}

internal sealed class FakeBenchmarkService : IHardwareBenchmarkService
{
    public bool GpuSupported { get; set; } = true;

    public List<GpuAdapter> Cards { get; } = [new("NVIDIA GeForce RTX 5090", 0x10DE, 0x2B85, 32e9, 1, false)];

    public double CpuSingle { get; set; } = 2500;

    public double CpuMulti { get; set; } = 20000;

    public double GpuScore { get; set; } = 30000;

    /// <summary>When set, the benchmarks wait for it (to see them running, or cancel them).</summary>
    public TaskCompletionSource? Hold { get; set; }

    public List<BenchmarkResult> Saved { get; set; } = [];

    public int Saves { get; private set; }

    public List<FakeStressLoad> Loads { get; } = [];

    public Action<FakeStressLoad>? OnLoad { get; set; }

    public IReadOnlyList<GpuAdapter> GraphicsCards() => Cards;

    public async Task<CpuBenchmarkScores> RunCpuAsync(CpuBenchmarkOptions options, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new BenchmarkProgress("File compression", 0.1, "Single-core"));
        if (Hold is not null)
        {
            await Hold.Task.WaitAsync(cancellationToken);
        }

        await Task.Delay(30, cancellationToken);
        var tests = CpuTests.All().Select(t => new CpuTestScore(t.Id, t.Name, t.Category, t.Unit, t.Reference * CpuSingle / 1000, t.Reference * CpuMulti / 1000, CpuSingle, CpuMulti)).ToList();
        return new CpuBenchmarkScores(tests, CpuSingle, CpuMulti, 32, 80, 72);
    }

    public async Task<GpuBenchmarkScores> RunGpuAsync(GpuAdapter? card, IProgress<BenchmarkProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new BenchmarkProgress("Lighting", 0.2, "Graphics"));
        if (Hold is not null)
        {
            await Hold.Task.WaitAsync(cancellationToken);
        }

        await Task.Delay(30, cancellationToken);
        var tests = GpuTests.All.Select(t => new BenchmarkTestResult(t.Id, t.Name, t.Category, t.Unit, t.Reference * GpuScore / 1000, GpuScore)).ToList();
        return new GpuBenchmarkScores(card ?? Cards[0], tests, GpuScore, GpuScore, GpuScore);
    }

    public IStressLoad ProcessorStress(string device) => Load("Processor", device);

    public IStressLoad? GraphicsStress(GpuAdapter? card) => GpuSupported ? Load("Graphics card", card?.Name ?? "GPU") : null;

    public IReadOnlyList<BenchmarkResult> LoadResults() => Saved;

    public void SaveResults(IReadOnlyList<BenchmarkResult> results)
    {
        Saved = [.. results];
        Saves++;
    }

    private FakeStressLoad Load(string name, string device)
    {
        var load = new FakeStressLoad(name, device);
        Loads.Add(load);
        OnLoad?.Invoke(load);
        return load;
    }
}

public class BenchmarkingViewModelTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 20, 18, 30, 0, TimeSpan.Zero);

    private static (BenchmarkingViewModel Page, FakeBenchmarkService Service, ScriptedProvider Provider, MonitorContext Monitor, SettingsService Settings) Create(
        FakeBenchmarkService? service = null, Func<ScriptedProvider, ScriptedProvider>? extra = null)
    {
        service ??= new FakeBenchmarkService();
        var (monitor, settings, provider) = Fixture.Create(extra: p => (extra ?? (x => x))(p
            .Define(MetricIds.CpuTemperature, MetricCategory.Cpu, MetricKind.Temperature, 72)
            .Define(MetricIds.CpuPower, MetricCategory.Cpu, MetricKind.Power, 180)
            .Define(MetricIds.CpuClockAverage, MetricCategory.Cpu, MetricKind.Clock, 5100)
            .Define(MetricIds.CpuCoreClock(1), MetricCategory.Cpu, MetricKind.Clock, 5650)
            .Define(MetricIds.CpuCoreClock(2), MetricCategory.Cpu, MetricKind.Clock, 5400)
            .Define(MetricIds.CpuVoltage, MetricCategory.Cpu, MetricKind.Voltage, 1.25)
            .Define(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 2850)
            .Define(MetricIds.GpuVoltage(0), MetricCategory.Gpu, MetricKind.Voltage, 1.05)));
        monitor.Engine.Devices.Upsert(new DeviceInfo(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "NVIDIA GeForce RTX 5090")
        {
            Properties = [new("Driver", "581.57"), new("Max core clock", "3,090 MHz"), new("Default power limit", "500 W")],
        });
        monitor.Engine.PollOnce();
        var context = Fixture.Context(monitor, settings);
        context = new PageContext { Monitor = context.Monitor, Settings = context.Settings, Timeline = context.Timeline, Tools = new ToolServices { Benchmarks = service } };
        var page = new BenchmarkingViewModel(context)
        {
            Tick = TimeSpan.FromMilliseconds(20),
        };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, service, provider, monitor, settings);
    }

    private static BenchmarkResult Saved(BenchmarkKind kind, double score, int hours, string device = "Device") => new()
    {
        Kind = kind,
        Score = score,
        Device = device,
        Finished = Earlier.AddHours(hours),
        Subscores = kind == BenchmarkKind.Cpu ? [new("Single-core", score / 8), new("Multi-core", score)] : [],
        Verdict = kind == BenchmarkKind.Stress ? "Passed" : string.Empty,
        Passed = kind == BenchmarkKind.Stress,
        Target = kind == BenchmarkKind.Stress ? "Processor" : string.Empty,
        Seconds = 900,
    };

    [Fact]
    public void SavedResults_AreListed_NewestFirst_WithABestBadgePerKind()
    {
        var service = new FakeBenchmarkService
        {
            Saved = [Saved(BenchmarkKind.Cpu, 18000, 1), Saved(BenchmarkKind.Cpu, 21000, 2), Saved(BenchmarkKind.Gpu, 25000, 3), Saved(BenchmarkKind.Cpu, 19500, 4), Saved(BenchmarkKind.Stress, 99.1, 5)],
        };
        var (page, _, _, _, settings) = Create(service);

        Assert.Equal(5, page.Results.Count);
        Assert.Equal(BenchmarkKind.Stress, page.Results[0].Result.Kind);
        Assert.Equal(new[] { false, false, true, true, false }, page.Results.Select(r => r.IsBest));
        Assert.Equal(21000, page.Results.Single(r => r.IsBest && r.Result.Kind == BenchmarkKind.Cpu).Result.Score);
        Assert.True(page.Cpu.HasBest);
        Assert.Equal(BenchmarkFormat.Score(21000), page.Cpu.Best);
        Assert.Contains("93 % of your best", page.Cpu.Latest);
        Assert.Equal(new[] { "Single-core", "Multi-core" }, page.Cpu.Parts.Select(p => p.Label));
        Assert.Equal(BenchmarkFormat.Score(25000), page.Gpu.Best);
        Assert.Equal(string.Empty, page.Gpu.Latest); // the latest is the best
        Assert.Equal("Passed", page.Results[0].VerdictText);
        Assert.Equal("5 saved results", page.ResultsSummary);
        settings.Dispose();
    }

    [Fact]
    public async Task TheCpuBenchmark_SavesItsScores_ReadingsAndTuning()
    {
        var (page, service, _, _, settings) = Create();
        page.CpuOptions = new CpuBenchmarkOptions(Seconds: 0.01, WarmupSeconds: 0, Threads: 1, Small: true);

        await page.RunCpuCommand.ExecuteAsync(null);

        var result = Assert.Single(service.Saved);
        Assert.Equal(BenchmarkKind.Cpu, result.Kind);
        Assert.Equal(20000, result.Score);
        Assert.Equal("AMD Ryzen 9 9950X3D", result.Device);
        Assert.Equal(new[] { "Single-core", "Multi-core" }, result.Subscores.Select(s => s.Name));
        Assert.Equal(8, result.Tests.Count);
        Assert.All(result.Tests, t => Assert.NotNull(t.SingleScore));
        Assert.Contains(result.Readings, r => r.Name == BenchmarkReadings.FastestCore && r.Max == 5650);
        Assert.Contains(result.Readings, r => r.Name == BenchmarkReadings.Temperature && r.Max == 72);
        Assert.Contains(result.Tuning, t => t.Name == "Fastest core clock");
        Assert.Contains(result.Tuning, t => t.Name == "Precision Boost Overdrive"); // a Ryzen desktop processor
        Assert.Contains(result.Tuning, t => t.Name == "Core voltage");
        Assert.Equal(16, result.System.Cores);
        Assert.Equal(32, result.Threads);
        Assert.Equal(80, result.MemoryBandwidth);
        Assert.True(page.Cpu.HasBest);
        Assert.True(page.Results[0].IsBest);
        Assert.False(page.IsRunning);
        Assert.False(page.Cpu.IsRunning);
        Assert.Equal("—", page.Cpu.Live[0].Value);
        Assert.Contains("Saved", page.Status);
        settings.Dispose();
    }

    [Fact]
    public async Task RunningBoth_SavesTwoResults_AndTheGpuOneReadsTheRaisedPowerLimit()
    {
        var (page, service, _, _, settings) = Create();
        Assert.True(page.Gpu.IsAvailable);
        Assert.Equal("NVIDIA GeForce RTX 5090", page.Gpu.Device);

        await page.RunBothCommand.ExecuteAsync(null);

        Assert.Equal(2, service.Saved.Count);
        var gpu = service.Saved.Single(r => r.Kind == BenchmarkKind.Gpu);
        Assert.Equal(30000, gpu.Score);
        Assert.Equal(new[] { "Graphics", "Compute" }, gpu.Subscores.Select(s => s.Name));
        Assert.Equal(5, gpu.Tests.Count);
        Assert.Equal("NVIDIA GeForce RTX 5090", gpu.System.GraphicsCard);
        Assert.Equal("581.57", gpu.System.GraphicsDriver);
        var limit = Assert.Single(gpu.Tuning, t => t.Name == "Power limit");
        Assert.True(limit.Raised); // 575 W against the 500 W default
        Assert.Contains("+15 %", limit.Detail);
        Assert.Contains(gpu.Tuning, t => t.Name == "Peak core clock" && !t.Raised); // 2,850 MHz of 3,090 MHz
        Assert.True(page.Gpu.HasBest);
        settings.Dispose();
    }

    [Fact]
    public async Task WhileRunning_TheCardShowsProgress_AndCancellingSavesNothing()
    {
        var service = new FakeBenchmarkService { Hold = new TaskCompletionSource() };
        var (page, _, _, _, settings) = Create(service);

        var run = page.RunCpuCommand.ExecuteAsync(null);
        await Task.Delay(100);
        Assert.True(page.IsRunning);
        Assert.True(page.Cpu.IsRunning);
        Assert.Equal("File compression", page.Cpu.Step);
        Assert.Equal(0.1, page.Cpu.Progress, 6);
        Assert.NotEqual("—", page.Cpu.Live[1].Value); // live temperature
        Assert.False(page.RunGpuCommand.CanExecute(null));
        Assert.False(page.StartStressCommand.CanExecute(null));

        page.CancelCommand.Execute(null);
        await run;

        Assert.Empty(service.Saved);
        Assert.False(page.IsRunning);
        Assert.Contains("Cancelled", page.Status);
        settings.Dispose();
    }

    [Fact]
    public void WithoutDirect3D_TheGpuBenchmarkAndItsStressTestAreOff()
    {
        var (page, _, _, _, settings) = Create(new FakeBenchmarkService { GpuSupported = false });

        Assert.False(page.Gpu.IsAvailable);
        Assert.NotNull(page.Gpu.UnavailableReason);
        Assert.False(page.RunGpuCommand.CanExecute(null));
        Assert.False(page.RunBothCommand.CanExecute(null));
        Assert.True(page.RunCpuCommand.CanExecute(null));
        page.SelectStressTargetCommand.Execute(page.StressTargets.Single(t => t.Key == "gpu"));
        Assert.False(page.StartStressCommand.CanExecute(null));
        page.SelectStressTargetCommand.Execute(page.StressTargets.Single(t => t.Key == "cpu"));
        Assert.True(page.StartStressCommand.CanExecute(null));
        settings.Dispose();
    }

    [Fact]
    public void Details_ShowEverything_AndDeletingTheBestPassesTheBadgeOn()
    {
        var best = Saved(BenchmarkKind.Cpu, 21000, 2) with
        {
            Tests = [new("matrix", "Machine learning", "Floating point", "GFLOPS", 380, 21000, 45, 2500)],
            Readings = [new(BenchmarkReadings.Processor, BenchmarkReadings.Clock, MetricKind.Clock, 4800, 5100, 5650)],
            Tuning = [new(BenchmarkReadings.Processor, "Precision Boost Overdrive", "Likely on", "Power reached 230 W.", Raised: true)],
            System = new BenchmarkSystem { Processor = "AMD Ryzen 9 9950X3D", Cores = 16, Threads = 32, Memory = 64e9, MemorySpeed = "6,000 MT/s" },
            MemoryBandwidth = 75,
            MemoryLatency = 70,
        };
        var service = new FakeBenchmarkService { Saved = [Saved(BenchmarkKind.Cpu, 18000, 1), best] };
        var (page, _, _, _, settings) = Create(service);

        page.OpenDetailsCommand.Execute(page.Results.Single(r => r.IsBest));

        var details = page.Details!;
        Assert.True(page.IsDetailsOpen);
        Assert.True(details.IsBest);
        Assert.True(details.HasSingle);
        var test = Assert.Single(details.Tests);
        Assert.Equal("380 GFLOPS", test.Rate);
        Assert.Equal("45.0 GFLOPS", test.SingleRate);
        var reading = Assert.Single(details.Readings);
        Assert.Contains("GHz", reading.Max);
        Assert.True(Assert.Single(details.Tuning).Raised);
        Assert.Contains(details.System, kv => kv.Key == "Memory bandwidth");
        Assert.Contains(details.System, kv => kv.Key == "Cores" && kv.Value == "16 cores, 32 threads");

        page.DeleteCommand.Execute(null);

        Assert.False(page.IsDetailsOpen);
        var left = Assert.Single(page.Results);
        Assert.True(left.IsBest);
        Assert.Equal(18000, left.Result.Score);
        Assert.Single(service.Saved);
        settings.Dispose();
    }

    [Fact]
    public void Filters_ShowOneKindAtATime()
    {
        var service = new FakeBenchmarkService { Saved = [Saved(BenchmarkKind.Cpu, 1, 1), Saved(BenchmarkKind.Gpu, 2, 2), Saved(BenchmarkKind.Stress, 99, 3)] };
        var (page, _, _, _, settings) = Create(service);

        page.SelectFilterCommand.Execute(page.Filters.Single(f => f.Key == "gpu"));
        Assert.Equal(BenchmarkKind.Gpu, Assert.Single(page.Results).Result.Kind);
        page.SelectFilterCommand.Execute(page.Filters.Single(f => f.Key == "stress"));
        Assert.Equal(BenchmarkKind.Stress, Assert.Single(page.Results).Result.Kind);
        page.SelectFilterCommand.Execute(page.Filters[0]);
        Assert.Equal(3, page.Results.Count);
        settings.Dispose();
    }

    private static void ShortStress(BenchmarkingViewModel page, double seconds = 1.2, double loop = 0.25)
    {
        page.DurationOverride = TimeSpan.FromSeconds(seconds);
        page.LoopSecondsOverride = loop;
    }

    [Fact]
    public async Task AStressTest_ThatHoldsItsSpeed_Passes()
    {
        var (page, service, _, _, settings) = Create();
        ShortStress(page);

        await page.StartStressCommand.ExecuteAsync(null);

        var load = Assert.Single(service.Loads);
        Assert.True(load.Started && load.Stopped);
        var result = Assert.Single(service.Saved);
        Assert.Equal(BenchmarkKind.Stress, result.Kind);
        Assert.True(result.Passed, result.Verdict);
        Assert.Equal("Passed", result.Verdict);
        Assert.Equal("Processor", result.Target);
        Assert.True(result.Loops.Count >= 3, $"{result.Loops.Count} loops");
        Assert.True(result.Score >= 97, $"stability {result.Score}");
        Assert.StartsWith("Passed", page.StressVerdict);
        Assert.False(page.IsStressRunning);
        Assert.Equal("—", page.StressLive[0].Value);
        Assert.False(page.Results[0].IsBest); // stress tests pass or fail; they're never "best"
        settings.Dispose();
    }

    [Fact]
    public async Task Throttling_FailsTheStressTest()
    {
        var (page, service, _, _, settings) = Create();
        ShortStress(page);
        service.OnLoad = load => _ = Task.Delay(500).ContinueWith(_ => load.Rate = 800, TaskScheduler.Default);

        await page.StartStressCommand.ExecuteAsync(null);

        var result = Assert.Single(service.Saved);
        Assert.False(result.Passed);
        Assert.StartsWith("Failed: stability", result.Verdict);
        Assert.True(result.Score < 97);
        Assert.Contains(result.Loops, l => l < 97);
        settings.Dispose();
    }

    [Fact]
    public async Task ACalculationError_StopsTheStressTest_AndFailsIt()
    {
        var (page, service, _, _, settings) = Create();
        ShortStress(page, seconds: 30);
        service.OnLoad = load => _ = Task.Delay(400).ContinueWith(_ =>
        {
            load.Failure = "Logical processor 7 got a matrix calculation wrong.";
            load.Errors = 1;
        }, TaskScheduler.Default);
        var clock = Stopwatch.StartNew();

        await page.StartStressCommand.ExecuteAsync(null);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        var result = Assert.Single(service.Saved);
        Assert.False(result.Passed);
        Assert.Equal(1, result.Errors);
        Assert.Equal("Failed: Logical processor 7 got a matrix calculation wrong.", result.Verdict);
        Assert.Equal("Failed", page.Results[0].VerdictText);
        settings.Dispose();
    }

    [Fact]
    public async Task TooHot_StopsTheStressTest()
    {
        var (page, service, provider, monitor, settings) = Create();
        ShortStress(page, seconds: 30);
        provider.Values[MetricIds.CpuTemperature] = 101;
        monitor.Engine.PollOnce();

        await page.StartStressCommand.ExecuteAsync(null);

        var result = Assert.Single(service.Saved);
        Assert.False(result.Passed);
        Assert.StartsWith("Stopped: the processor stayed at 100", result.Verdict);
        settings.Dispose();
    }

    [Fact]
    public async Task BothParts_RunTogether()
    {
        var (page, service, _, _, settings) = Create();
        ShortStress(page);
        page.SelectStressTargetCommand.Execute(page.StressTargets.Single(t => t.Key == "both"));

        await page.StartStressCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Processor", "Graphics card" }, service.Loads.Select(l => l.Name));
        var result = Assert.Single(service.Saved);
        Assert.Equal("Processor and graphics card", result.Target);
        Assert.Contains(result.Readings, r => r.Group == BenchmarkReadings.Graphics);
        Assert.Contains(result.Readings, r => r.Group == BenchmarkReadings.Processor);
        settings.Dispose();
    }

    [Fact]
    public async Task StoppingBeforeTheFirstLoop_SavesNothing()
    {
        var (page, service, _, _, settings) = Create();
        page.LoopSecondsOverride = 30;
        page.SelectDurationCommand.Execute(page.Durations.Single(d => d.Duration is null));

        var run = page.StartStressCommand.ExecuteAsync(null);
        await Task.Delay(100);
        Assert.True(page.IsStressRunning);
        Assert.False(page.RunCpuCommand.CanExecute(null));
        Assert.Equal(string.Empty, page.StressRemaining); // until stopped
        page.StopStressCommand.Execute(null);
        await run;

        Assert.Empty(service.Saved);
        Assert.Contains("nothing was saved", page.StressStatus);
        Assert.False(page.IsStressRunning);
        settings.Dispose();
    }
}
