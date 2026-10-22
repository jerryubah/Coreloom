using System.Diagnostics;
using Coreloom.Core.Benchmarks;
using Coreloom.Hardware.Gpu;

namespace Coreloom.Hardware.Tests;

/// <summary>
/// The GPU benchmark on WARP, Windows' software renderer, which every Windows PC (and the build machine) has: the
/// shaders compile, every test runs and is timed, and the answers are the same every time.
/// </summary>
public class GpuBenchmarkTests
{
    [Fact]
    public async Task EveryTest_RunsAndIsScored()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var reports = new List<BenchmarkProgress>();
        var scores = await GpuBenchmark.RunAsync(new GpuBenchmarkOptions(Seconds: 0.2, Small: true, Software: true), null, new InlineProgress<BenchmarkProgress>(reports.Add), CancellationToken.None);

        Assert.True(scores.Adapter.IsSoftware);
        Assert.Equal(GpuTests.All.Select(t => t.Id), scores.Tests.Select(t => t.Id));
        Assert.All(scores.Tests, t =>
        {
            Assert.True(t.Rate > 0, $"{t.Name}: {t.Rate}");
            Assert.True(t.Score > 0, t.Name);
        });
        Assert.True(scores.Score > 0 && scores.Graphics > 0 && scores.Compute > 0);
        Assert.Equal(1, reports[^1].Fraction, 6);
    }

    [Fact]
    public void Answers_AreTheSameEveryTime()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var gpu = Direct3D.Create(software: true);
        foreach (var workload in GpuWorkload.All(gpu, small: true))
        {
            long first = workload.Verify();
            Assert.Equal(first, workload.Verify());
            if (workload is not BandwidthWorkload)
            {
                Assert.NotEqual(0, first);
            }
        }
    }

    [Fact]
    public async Task Cancelling_StopsTheRun()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GpuBenchmark.RunAsync(new GpuBenchmarkOptions(Seconds: 30, Small: true, Software: true), null, null, cts.Token));
    }

    [Fact]
    public void StressLoad_Works_AndFindsNoErrors()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var load = new GpuStressLoad(null, software: true, small: true) { VerifySeconds = 0.5 };
        load.Start();
        var clock = Stopwatch.StartNew();
        while (load.Work < 4 && load.Failure is null && clock.Elapsed < TimeSpan.FromSeconds(90))
        {
            Thread.Sleep(50);
        }

        Thread.Sleep(700); // at least one check of the answers
        load.Stop();
        Assert.Null(load.Failure);
        Assert.Equal(0, load.Errors);
        Assert.True(load.Work >= 4, $"work {load.Work}");
        Assert.Contains("WARP", load.Device);
    }

    [Fact]
    public void Adapters_LeaveOutSoftwareRenderers()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        foreach (var adapter in GpuBenchmark.Adapters())
        {
            Assert.False(adapter.IsSoftware, adapter.Name);
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
        }
    }

    [Fact]
    public void ABrokenShader_SaysWhy()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var error = Assert.Throws<InvalidOperationException>(() => Direct3D.Compile("float4 Broken() : SV_Target { return nope; }", "Broken", "ps_5_0"));
        Assert.Contains("nope", error.Message);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
