using Coreloom.Core.Benchmarks;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

public class CpuBenchmarkTests
{
    [Fact]
    public void EveryTest_GivesTheSameAnswer_ForTheSameSeed()
    {
        foreach (var test in CpuTests.All(small: true))
        {
            if (test.Id == "navigation")
            {
                continue; // each run picks the next route, so its answers differ by design
            }

            var first = test.CreateWorker(3);
            var second = test.CreateWorker(3);
            long answer = first.Run();
            Assert.Equal(answer, first.Run());
            Assert.Equal(answer, second.Run());
        }
    }

    [Fact]
    public void Navigation_FindsTheSameRoutes_InTheSameOrder()
    {
        var test = new NavigationTest(small: true);
        var a = test.CreateWorker(1);
        var b = test.CreateWorker(1);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(a.Run(), b.Run());
        }
    }

    [Fact]
    public void Tests_CoverIntegerFloatingPointAndCryptography_WithReferences()
    {
        var tests = CpuTests.All();
        Assert.Equal(8, tests.Count);
        Assert.Equal(tests.Count, tests.Select(t => t.Id).Distinct().Count());
        Assert.Contains(tests, t => t.Category == "Integer");
        Assert.Contains(tests, t => t.Category == "Floating point");
        Assert.Contains(tests, t => t.Category == "Cryptography");
        Assert.All(tests, t =>
        {
            Assert.True(t.Reference > 0, t.Id);
            Assert.True(t.WorkPerIteration > 0, t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Id);
        });
    }

    [Fact]
    public void Score_Is1000_AtTheReference_AndTheOverallIsTheGeometricMean()
    {
        Assert.Equal(1000, CpuBenchmark.Score(15, 15), 6);
        Assert.Equal(2000, CpuBenchmark.Score(30, 15), 6);
        Assert.Equal(0, CpuBenchmark.Score(0, 15));
        Assert.Equal(2000, CpuBenchmark.GeometricMean([1000, 4000]), 6);
        Assert.Equal(0, CpuBenchmark.GeometricMean([1000, 0]));
        Assert.Equal(0, CpuBenchmark.GeometricMean([]));
    }

    [Fact]
    public void Measure_AddsUpEveryThreadsWork()
    {
        var test = new TextProcessingTest(small: true);
        double one = CpuBenchmark.Measure(test, 1, 0.1, 0.02, ThreadPriority.Normal, default);
        double two = CpuBenchmark.Measure(test, 2, 0.1, 0.02, ThreadPriority.Normal, default);
        Assert.True(one > 0);
        Assert.True(two > one * 0.9, $"two threads {two} vs one {one}"); // never less than one thread's (allowing for noise)
    }

    [Fact]
    public async Task Run_ScoresEveryTest_OnOneThreadAndOnAll()
    {
        var reports = new List<BenchmarkProgress>();
        var scores = await CpuBenchmark.RunAsync(new CpuBenchmarkOptions(Seconds: 0.05, WarmupSeconds: 0.01, Threads: 2, Small: true), new InlineProgress<BenchmarkProgress>(reports.Add), default);

        Assert.Equal(8, scores.Tests.Count);
        Assert.All(scores.Tests, t =>
        {
            Assert.True(t.SingleRate > 0 && t.MultiRate > 0, t.Id);
            Assert.True(t.SingleScore > 0 && t.MultiScore > 0, t.Id);
        });
        Assert.Equal(CpuBenchmark.GeometricMean(scores.Tests.Select(t => t.SingleScore)), scores.SingleScore, 6);
        Assert.Equal(CpuBenchmark.GeometricMean(scores.Tests.Select(t => t.MultiScore)), scores.MultiScore, 6);
        Assert.Equal(2, scores.Threads);
        Assert.True(scores.MemoryBandwidthGBs > 0);
        Assert.True(scores.MemoryLatencyNs > 0);
        Assert.Equal(1, reports[^1].Fraction, 6);
        Assert.True(reports.Select(r => r.Fraction).SequenceEqual(reports.Select(r => r.Fraction).Order()), "progress only moves forward");
    }

    [Fact]
    public async Task Run_StopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CpuBenchmark.RunAsync(new CpuBenchmarkOptions(Seconds: 30, WarmupSeconds: 0.01, Threads: 1, Small: true), null, cts.Token));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public class BenchmarkHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static BenchmarkResult Result(BenchmarkKind kind, double score, int minutes, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Kind = kind,
        Score = score,
        Finished = Start.AddMinutes(minutes),
    };

    [Fact]
    public void Best_IsTheHighestScoreOfItsKind_AndStressTestsAreNeverBest()
    {
        var cpuLow = Result(BenchmarkKind.Cpu, 9000, 1);
        var cpuHigh = Result(BenchmarkKind.Cpu, 12000, 2);
        var gpu = Result(BenchmarkKind.Gpu, 4000, 3);
        var stress = Result(BenchmarkKind.Stress, 99.1, 4);
        var history = new BenchmarkHistory([cpuLow, gpu, cpuHigh, stress]);

        Assert.Equal(cpuHigh.Id, history.Best(BenchmarkKind.Cpu)!.Id);
        Assert.Equal(gpu.Id, history.Best(BenchmarkKind.Gpu)!.Id);
        Assert.Null(history.Best(BenchmarkKind.Stress));
        Assert.True(history.IsBest(cpuHigh));
        Assert.False(history.IsBest(cpuLow));
        Assert.False(history.IsBest(stress));
        Assert.Equal(0.75, history.FractionOfBest(cpuLow), 6);
        Assert.Equal(1, history.FractionOfBest(cpuHigh), 6);
    }

    [Fact]
    public void Results_AreNewestFirst_AndATieGoesToTheEarlierRun()
    {
        var first = Result(BenchmarkKind.Gpu, 5000, 1);
        var second = Result(BenchmarkKind.Gpu, 5000, 2);
        var history = new BenchmarkHistory();
        history.Add(first);
        history.Add(second);

        Assert.Equal(second.Id, history.Results[0].Id);
        Assert.Equal(first.Id, history.Best(BenchmarkKind.Gpu)!.Id);
        Assert.Equal(second.Id, history.Latest(BenchmarkKind.Gpu)!.Id);
    }

    [Fact]
    public void Removing_TheBest_PassesTheBadgeOn()
    {
        var a = Result(BenchmarkKind.Cpu, 100, 1);
        var b = Result(BenchmarkKind.Cpu, 200, 2);
        var history = new BenchmarkHistory([a, b]);
        Assert.True(history.Remove(b.Id));
        Assert.Equal(a.Id, history.Best(BenchmarkKind.Cpu)!.Id);
        Assert.False(history.Remove("missing"));
    }

    [Fact]
    public void Trimming_DropsTheOldest_ButKeepsEachKindsBest()
    {
        var oldBest = Result(BenchmarkKind.Cpu, 99999, -1000);
        var history = new BenchmarkHistory([oldBest]);
        for (int i = 0; i < BenchmarkHistory.Capacity + 20; i++)
        {
            history.Add(Result(BenchmarkKind.Cpu, 100 + i, i));
        }

        Assert.Equal(BenchmarkHistory.Capacity, history.Results.Count);
        Assert.Contains(history.Results, r => r.Id == oldBest.Id);
        Assert.Equal(oldBest.Id, history.Best(BenchmarkKind.Cpu)!.Id);
    }

    [Fact]
    public void Store_SavesAndLoadsEverything()
    {
        string file = Path.Combine(Path.GetTempPath(), "coreloom-bench-" + Guid.NewGuid().ToString("N"), BenchmarkStore.FileName);
        var store = new BenchmarkStore(file);
        var result = Result(BenchmarkKind.Cpu, 12345, 5) with
        {
            Device = "AMD Ryzen 9 9950X3D",
            Subscores = [new("Single-core", 2345), new("Multi-core", 12345)],
            Tests = [new("matrix", "Machine learning", "Floating point", "GFLOPS", 300, 16000, 40, 2200)],
            Readings = [new("Processor", "Core clock", MetricKind.Clock, 4300, 5100, 5750)],
            Tuning = [new("Processor", "Precision Boost Overdrive", "On", "Power limits above stock.", Raised: true)],
            System = new BenchmarkSystem { Processor = "AMD Ryzen 9 9950X3D", Cores = 16, Threads = 32, Memory = 64e9 },
            Loops = [100, 99.2],
        };

        store.Save([result]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(result.Id, loaded.Id);
        Assert.Equal(BenchmarkKind.Cpu, loaded.Kind);
        Assert.Equal(12345, loaded.Score);
        Assert.Equal(2345, loaded.Subscores[0].Score);
        Assert.Equal(2200, loaded.Tests[0].SingleScore);
        Assert.Equal(MetricKind.Clock, loaded.Readings[0].Kind);
        Assert.True(loaded.Tuning[0].Raised);
        Assert.Equal(32, loaded.System.Threads);
        Assert.Equal(new double[] { 100, 99.2 }, loaded.Loops);
        Assert.Equal(result.Finished, loaded.Finished);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }

    [Fact]
    public void Store_KeepsAnUnreadableFile_AndStartsEmpty()
    {
        string folder = Path.Combine(Path.GetTempPath(), "coreloom-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, BenchmarkStore.FileName);
        File.WriteAllText(file, "{ not json");

        Assert.Empty(new BenchmarkStore(file).Load());
        Assert.True(File.Exists(file + ".bad"));
        Assert.Empty(new BenchmarkStore(Path.Combine(folder, "missing.json")).Load());
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Readings_KeepTheLowestAverageAndHighest_InTheOrderSeen()
    {
        var recorder = new ReadingRecorder();
        recorder.Add("Processor", "Temperature", MetricKind.Temperature, 60);
        recorder.Add("Processor", "Temperature", MetricKind.Temperature, 80);
        recorder.Add("Processor", "Power", MetricKind.Power, 150);
        recorder.Add("Processor", "Temperature", MetricKind.Temperature, double.NaN);
        recorder.Add("Processor", "Temperature", MetricKind.Temperature, 70);

        var readings = recorder.Readings();
        Assert.Equal(new[] { "Temperature", "Power" }, readings.Select(r => r.Name));
        Assert.Equal(60, readings[0].Min);
        Assert.Equal(70, readings[0].Average, 6);
        Assert.Equal(80, readings[0].Max);
        Assert.Equal(80, recorder.Max("Processor", "Temperature"));
        Assert.True(double.IsNaN(recorder.Max("Graphics", "Temperature")));
    }

    [Fact]
    public void GpuScores_AreGeometricMeans_OverallAndPerCategory()
    {
        BenchmarkTestResult[] tests =
        [
            new("lighting", "Lighting", "Graphics", "fps", 160, 2000),
            new("raymarching", "Ray marching", "Graphics", "fps", 70, 8000),
            new("compute", "Compute", "Compute", "GFLOPS", 4400, 1000),
        ];
        var (overall, graphics, compute) = GpuTests.Scores(tests);
        Assert.Equal(4000, graphics, 6);
        Assert.Equal(1000, compute, 6);
        Assert.Equal(Math.Cbrt(2000.0 * 8000 * 1000), overall, 6);
        Assert.Equal(5, GpuTests.All.Count);
    }
}

public class StressTestTests
{
    [Fact]
    public void Stability_IsTheWorstLoopAgainstTheBest()
    {
        Assert.Equal(90, StressScoring.Stability([100, 95, 90, 99]), 6);
        Assert.Equal(100, StressScoring.Stability([50]), 6);
        Assert.Equal(0, StressScoring.Stability([]));
        Assert.Equal(new double[] { 100, 50 }, StressScoring.Relative([10, 5]));
    }

    [Fact]
    public void Passing_NeedsTheFullRun_NoErrors_TwoLoops_And97Percent()
    {
        Assert.True(StressScoring.Passed(StressStopReason.Finished, 0, [100, 98]));
        Assert.False(StressScoring.Passed(StressStopReason.Finished, 0, [100, 96]));
        Assert.False(StressScoring.Passed(StressStopReason.Finished, 1, [100, 100]));
        Assert.False(StressScoring.Passed(StressStopReason.Finished, 0, [100]));
        Assert.False(StressScoring.Passed(StressStopReason.Cancelled, 0, [100, 100]));
        Assert.False(StressScoring.Passed(StressStopReason.ProcessorTooHot, 0, [100, 100]));
    }

    [Fact]
    public void Loops_AreTenSeconds_OrFiveForShortRuns()
    {
        Assert.Equal(5, StressScoring.LoopSeconds(TimeSpan.FromMinutes(1)));
        Assert.Equal(10, StressScoring.LoopSeconds(TimeSpan.FromMinutes(15)));
        Assert.Equal(10, StressScoring.LoopSeconds(null));

        var loops = new StressLoops(5);
        Assert.False(loops.Add(1, 100));
        Assert.True(loops.Add(5, 500));
        Assert.False(loops.Add(9, 800));
        Assert.True(loops.Add(10, 900));
        Assert.Equal(new double[] { 100, 80 }, loops.Loops);
    }

    [Fact]
    public void Guard_StopsOnlyWhenItStaysTooHot()
    {
        var guard = new StressGuard(processorLimit: 100, graphicsLimit: 95, seconds: 3);
        Assert.Equal(StressStopReason.None, guard.Check(100, 60));
        Assert.Equal(StressStopReason.None, guard.Check(101, 60));
        Assert.Equal(StressStopReason.None, guard.Check(99, 60)); // cooled down: starts again
        Assert.Equal(StressStopReason.None, guard.Check(100, 96));
        Assert.Equal(StressStopReason.None, guard.Check(100, 96));
        Assert.Equal(StressStopReason.ProcessorTooHot, guard.Check(100, 96));
        Assert.Equal(StressStopReason.GraphicsTooHot, new StressGuard(seconds: 1).Check(double.NaN, 95));
        Assert.Equal(StressStopReason.None, new StressGuard(seconds: 1).Check(double.NaN, double.NaN));
    }

    [Fact]
    public void CpuLoad_KeepsEveryThreadBusy_WithoutErrors()
    {
        using var load = new CpuStressLoad("Test CPU", threads: 2, small: true);
        Assert.Equal("Processor", load.Name);
        load.Start();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (load.Work < 20 && clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(20);
        }

        load.Stop();
        long done = load.Work;
        Assert.True(done >= 20, $"work {done}");
        Assert.Equal(0, load.Errors);
        Assert.Null(load.Failure);
        Thread.Sleep(50);
        Assert.Equal(done, load.Work); // stopped means stopped
    }
}

public class BenchmarkTuningTests
{
    private static readonly Formatting.UnitFormatter F = new(culture: System.Globalization.CultureInfo.InvariantCulture);

    private static ReadingRecorder Processor(double performanceLimit = 100)
    {
        var r = new ReadingRecorder();
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.FastestCore, MetricKind.Clock, 5750);
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.Performance, MetricKind.Level, 150);
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.Power, MetricKind.Power, 200);
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.Voltage, MetricKind.Voltage, 1.3);
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.PerformanceLimit, MetricKind.Level, performanceLimit);
        r.Add(BenchmarkReadings.Processor, BenchmarkReadings.Temperature, MetricKind.Temperature, 88);
        return r;
    }

    [Fact]
    public void Pbo_IsLikelyOn_WhenPowerGoesWellAboveTheStockLimit()
    {
        var ryzen = Hardware.AmdProcessors.Create("AMD Ryzen 9 9950X3D 16-Core Processor");
        var notes = BenchmarkTuning.Processor(Processor(), ryzen, [260, 262, 259, 261], wattsFromPpt: true, F);

        var pbo = Assert.Single(notes, n => n.Name == "Precision Boost Overdrive");
        Assert.Equal("Likely on", pbo.Value);
        Assert.True(pbo.Raised);
        Assert.Contains("230 W", pbo.Detail);
        Assert.True(Assert.Single(notes, n => n.Name == "Peak power").Raised);
        Assert.Equal("5.75 GHz", Assert.Single(notes, n => n.Name == "Fastest core clock").Value);
        Assert.Equal("150 % of base clock", Assert.Single(notes, n => n.Name == "Boost").Value);
        Assert.Equal("No", Assert.Single(notes, n => n.Name == "Held back").Value);
    }

    [Fact]
    public void Pbo_IsOff_WhenPowerHoldsAtTheStockLimit_AndIntelHasNoPboNote()
    {
        var ryzen = Hardware.AmdProcessors.Create("AMD Ryzen 9 9950X3D 16-Core Processor");
        var notes = BenchmarkTuning.Processor(Processor(performanceLimit: 84), ryzen, [228, 229, 230, 229, 228, 230], wattsFromPpt: true, F);
        Assert.Equal("Off (stock limits)", Assert.Single(notes, n => n.Name == "Precision Boost Overdrive").Value);
        Assert.Equal("Down to 84 %", Assert.Single(notes, n => n.Name == "Held back").Value);

        var intel = BenchmarkTuning.Processor(Processor(), null, [250, 251, 252], wattsFromPpt: false, F);
        Assert.DoesNotContain(intel, n => n.Name == "Precision Boost Overdrive");
        Assert.False(Assert.Single(intel, n => n.Name == "Peak power").Raised);
    }

    private static ReadingRecorder Graphics(double clock, double limit)
    {
        var r = new ReadingRecorder();
        r.Add(BenchmarkReadings.Graphics, BenchmarkReadings.Clock, MetricKind.Clock, clock);
        r.Add(BenchmarkReadings.Graphics, BenchmarkReadings.PowerLimit, MetricKind.Power, limit);
        r.Add(BenchmarkReadings.Graphics, BenchmarkReadings.Power, MetricKind.Power, 430);
        r.Add(BenchmarkReadings.Graphics, BenchmarkReadings.Temperature, MetricKind.Temperature, 70);
        r.Add(BenchmarkReadings.Graphics, BenchmarkReadings.HotSpot, MetricKind.Temperature, 82);
        return r;
    }

    private static DeviceInfo Card() => new(MetricIds.GpuDeviceId(0), MetricCategory.Gpu, "NVIDIA GeForce RTX 4090")
    {
        Properties = [new("Max core clock", "3,105 MHz"), new("Max memory clock", "10,501 MHz"), new("Default power limit", "450 W")],
    };

    [Fact]
    public void GraphicsNotes_CompareWithTheCardsOwnDefaults()
    {
        var stock = BenchmarkTuning.Graphics(Graphics(2790, 450), Card(), 0x1, F);
        var limit = Assert.Single(stock, n => n.Name == "Power limit");
        Assert.Equal("The card's default.", limit.Detail);
        Assert.False(limit.Raised);
        Assert.False(Assert.Single(stock, n => n.Name == "Peak core clock").Raised);
        Assert.Equal("Nothing reported", Assert.Single(stock, n => n.Name == "Held back by").Value); // "Idle" doesn't count
        Assert.Contains("Hot spot: 82 °C", Assert.Single(stock, n => n.Name == "Hottest").Detail);

        var tuned = BenchmarkTuning.Graphics(Graphics(3200, 600), Card(), 0x4 | 0x20, F);
        Assert.True(Assert.Single(tuned, n => n.Name == "Peak core clock").Raised);
        var raised = Assert.Single(tuned, n => n.Name == "Power limit");
        Assert.True(raised.Raised);
        Assert.Contains("+33 %", raised.Detail);
        Assert.Equal("Power limit, Thermal limit", Assert.Single(tuned, n => n.Name == "Held back by").Value);

        var saving = BenchmarkTuning.Graphics(Graphics(2500, 315), Card(), 0, F);
        Assert.StartsWith("Lowered from the 450 W default (-30 %)", Assert.Single(saving, n => n.Name == "Power limit").Detail);
    }

    [Fact]
    public void Numbers_AreReadFromProperties()
    {
        Assert.Equal(3090, BenchmarkTuning.Number("3,090 MHz"));
        Assert.Equal(575, BenchmarkTuning.Number("575 W"));
        Assert.Equal(1.05, BenchmarkTuning.Number("1.05 V"));
        Assert.True(double.IsNaN(BenchmarkTuning.Number(null)));
        Assert.True(double.IsNaN(BenchmarkTuning.Number("none")));
    }

    [Fact]
    public void ProcessorReadings_TakeTheFastestCore_AndPreferPpt()
    {
        var samples = new Dictionary<string, MetricSample>
        {
            [MetricIds.CpuCoreClock(1)] = new(5500, null!),
            [MetricIds.CpuCoreClock(2)] = new(5700, null!),
            [MetricIds.CpuCoreEffectiveClock(2)] = new(9999, null!), // effective clocks aren't the core clock
            [MetricIds.CpuPower] = new(190, null!),
            [MetricIds.CpuPpt] = new(210, null!),
        };
        var recorder = new ReadingRecorder();
        var (watts, fromPpt) = BenchmarkReadings.RecordProcessor(recorder, new TelemetryFrame(1, 0, DateTime.Now, TimeSpan.Zero, samples));

        Assert.Equal(5700, recorder.Max(BenchmarkReadings.Processor, BenchmarkReadings.FastestCore));
        Assert.Equal(190, recorder.Max(BenchmarkReadings.Processor, BenchmarkReadings.Power));
        Assert.Equal(210, watts);
        Assert.True(fromPpt);
    }
}
