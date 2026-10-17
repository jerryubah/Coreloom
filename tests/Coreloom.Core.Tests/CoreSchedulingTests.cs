using System.Buffers.Binary;
using Coreloom.Core.Gaming;

namespace Coreloom.Core.Tests;

/// <summary>Builds what GetLogicalProcessorInformationEx returns (64-bit layout), for the processors the tests describe.</summary>
internal static class Topology
{
    private const long MB = 1024 * 1024;

    public static byte[] Core(ulong mask, byte efficiency = 0, int group = 0)
    {
        var b = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(b, 0); // RelationProcessorCore
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 48);
        b[8] = (byte)(System.Numerics.BitOperations.PopCount(mask) > 1 ? 1 : 0); // LTP_PC_SMT
        b[9] = efficiency;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(30), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(32), mask);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(40), (ushort)group);
        return b;
    }

    public static byte[] Cache(int level, long bytes, ulong mask, int group = 0)
    {
        var b = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(b, 2); // RelationCache
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 56);
        b[8] = (byte)level;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(38), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(40), mask);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(48), (ushort)group);
        return b;
    }

    /// <summary>A record Coreloom doesn't read (a processor group or NUMA node).</summary>
    public static byte[] Other(int relationship, int size = 80)
    {
        var b = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(b, relationship);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)size);
        return b;
    }

    /// <summary>Cores with SMT: core i runs logical processors 2i and 2i+1.</summary>
    public static IEnumerable<byte[]> SmtCores(int first, int count, byte efficiency = 0) =>
        Enumerable.Range(first, count).Select(i => Core(3UL << (2 * i), efficiency));

    public static byte[] Join(IEnumerable<byte[]> records) => [.. records.SelectMany(r => r)];

    /// <summary>Ryzen 9 9950X3D: 2 CCDs of 8 cores; the first with 96 MB of L3 (3D V-Cache), the second with 32 MB.</summary>
    public static byte[] Ryzen9950X3D() => Join(
    [
        Other(4), Other(1),
        .. SmtCores(0, 16),
        .. Enumerable.Range(0, 16).Select(i => Cache(2, 1 * MB, 3UL << (2 * i))),
        Cache(3, 96 * MB, 0xFFFF),
        Cache(3, 32 * MB, 0xFFFF_0000),
    ]);

    /// <summary>Ryzen 9 9950X: 2 CCDs of 8 cores, 32 MB of L3 each.</summary>
    public static byte[] Ryzen9950X() => Join([.. SmtCores(0, 16), Cache(3, 32 * MB, 0xFFFF), Cache(3, 32 * MB, 0xFFFF_0000)]);

    /// <summary>Ryzen 7 9800X3D: one CCD of 8 cores with 96 MB.</summary>
    public static byte[] Ryzen9800X3D() => Join([.. SmtCores(0, 8), Cache(3, 96 * MB, 0xFFFF)]);

    /// <summary>Core i9-14900K: 8 P-cores with Hyper-Threading (class 1) and 16 E-cores (class 0), one 36 MB L3.</summary>
    public static byte[] Core14900K() => Join(
    [
        .. SmtCores(0, 8, efficiency: 1),
        .. Enumerable.Range(16, 16).Select(i => Core(1UL << i, 0)),
        Cache(3, 36 * MB, 0xFFFF_FFFF),
    ]);

    /// <summary>Ryzen AI 9 HX 370: 4 Zen 5 cores (class 1, 16 MB L3) and 8 Zen 5c cores (class 0, 8 MB L3).</summary>
    public static byte[] RyzenAi370() => Join([.. SmtCores(0, 4, efficiency: 1), .. SmtCores(4, 8, efficiency: 0), Cache(3, 16 * MB, 0xFF), Cache(3, 8 * MB, 0xFF_FF00)]);
}

public class CoreLayoutTests
{
    [Fact]
    public void ARyzen9950X3D_HasCacheCoresAndFrequencyCores()
    {
        var layout = CoreLayout.Parse(Topology.Ryzen9950X3D());

        Assert.True(layout.IsVCache);
        Assert.True(layout.HasChoice);
        Assert.True(layout.CanSetAffinity);
        Assert.Equal(32, layout.LogicalProcessors);
        Assert.Equal(["Cache cores", "Frequency cores"], layout.Groups.Select(g => g.Name));
        var cache = layout.Groups[0];
        Assert.Equal(CoreKind.Cache, cache.Kind);
        Assert.Equal(8, cache.Cores);
        Assert.Equal(Enumerable.Range(0, 16), cache.Processors);
        Assert.Equal(0xFFFFUL, cache.Mask);
        Assert.Equal("8 cores · 96 MB L3", cache.Description);
        Assert.Equal(0xFFFF_0000UL, layout.Groups[1].Mask);
        Assert.Equal("8 cores · 32 MB L3", layout.Groups[1].Description);
        Assert.Same(cache, layout.Preferred);
        Assert.Equal(0xFFFF_FFFFUL, layout.AllMask);
        Assert.Equal("2 CCDs · 3D V-Cache on one", layout.Summary);
    }

    [Fact]
    public void AHybridIntel_HasPerformanceAndEfficiencyCores()
    {
        var layout = CoreLayout.Parse(Topology.Core14900K());

        Assert.True(layout.IsHybrid);
        Assert.False(layout.IsVCache);
        Assert.Equal(["Performance cores", "Efficiency cores"], layout.Groups.Select(g => g.Name));
        Assert.Equal("8 cores · 16 threads", layout.Groups[0].Description);
        Assert.Equal("16 cores", layout.Groups[1].Description);
        Assert.Equal(CoreKind.Performance, layout.Preferred!.Kind);
        Assert.Equal(32, layout.LogicalProcessors);
    }

    [Fact]
    public void ZenFiveNextToZenFiveC_IsHybrid_NotVCache_EvenWithALargerL3OnTheFastCores()
    {
        var layout = CoreLayout.Parse(Topology.RyzenAi370());

        Assert.True(layout.IsHybrid);
        Assert.False(layout.IsVCache);
        Assert.Equal(4, layout.Group(CoreKind.Performance)!.Cores);
    }

    [Fact]
    public void CcdsWithTheSameCache_AreListedAsCcds_AndTheFirstIsPreferred()
    {
        var layout = CoreLayout.Parse(Topology.Ryzen9950X());

        Assert.False(layout.IsVCache);
        Assert.Equal(["CCD 1", "CCD 2"], layout.Groups.Select(g => g.Name));
        Assert.Equal("CCD 1", layout.Preferred!.Name);
        Assert.Equal("2 CCDs · the same cache on each", layout.Summary);
    }

    [Fact]
    public void OneCcd_HasNothingToChoose_AndTheVCacheIsOnEveryCore()
    {
        var layout = CoreLayout.Parse(Topology.Ryzen9800X3D());

        Assert.False(layout.HasChoice);
        Assert.True(layout.HasVCacheEverywhere);
        Assert.Null(layout.Preferred);
        Assert.Equal("All cores", Assert.Single(layout.Groups).Name);
        Assert.Equal("One CCD · 3D V-Cache on every core", layout.Summary);
    }

    [Fact]
    public void ABrokenBuffer_GivesAnUnknownLayout()
    {
        Assert.False(CoreLayout.Parse([]).IsKnown);
        Assert.False(CoreLayout.Parse([1, 2, 3, 4, 5, 6, 7, 8, 9]).IsKnown);
        var truncated = Topology.Ryzen9950X3D()[..100];
        Assert.True(CoreLayout.Parse(truncated).Groups.Count <= 1); // what was complete is read, the rest ignored
    }

    [Fact]
    public void ProcessorsInASecondGroup_CantBeChosenWithOneMask()
    {
        var layout = CoreLayout.Build(
            [new CoreUnit(0, 0b11, 0), new CoreUnit(1, 0b11, 0)],
            [new CacheUnit(3, 96L << 20, 0, 0b11), new CacheUnit(3, 32L << 20, 1, 0b11)]);

        Assert.True(layout.IsVCache);
        Assert.Equal([2, 3], layout.Groups[1].Processors);
        Assert.False(layout.CanSetAffinity);
    }
}

public class CorePlacementTests
{
    private static readonly CoreLayout X3D = CoreLayout.Parse(Topology.Ryzen9950X3D());

    private static double[] Loads(double cache, double frequency) => [.. Enumerable.Range(0, 32).Select(i => i < 16 ? cache : frequency)];

    private static CorePlacement Measure(CoreLayout layout, double[] loads, int times = CorePlacement.MinimumSamples)
    {
        var placement = new CorePlacement(layout);
        for (int i = 0; i < times; i++)
        {
            Assert.True(placement.Add(loads));
        }

        return placement;
    }

    [Fact]
    public void MostOfTheLoadOnTheCacheCores_IsGood()
    {
        var placement = Measure(X3D, Loads(40, 10));

        Assert.Equal(0.8, placement.Shares[0], 6);
        var verdict = CoreScheduling.Judge(X3D, placement, "Cyberpunk 2077");
        Assert.Equal(PlacementState.Good, verdict.State);
        Assert.Equal("Cyberpunk 2077: 80 % of the load on the cache cores", verdict.Text);
    }

    [Fact]
    public void ASplit_AndMostlyOnTheFrequencyCores_AreSaidSo()
    {
        Assert.Equal("Game: split, 50 % on the cache cores and 50 % on the frequency cores", CoreScheduling.Judge(X3D, Measure(X3D, Loads(20, 20)), "Game").Text);

        var off = CoreScheduling.Judge(X3D, Measure(X3D, Loads(5, 45)), "Game");
        Assert.Equal(PlacementState.Off, off.State);
        Assert.Equal("Game: mostly on the frequency cores (90 % of the load)", off.Text);
    }

    [Fact]
    public void TooFewReadings_OrAnIdleProcessor_GiveNoVerdict()
    {
        Assert.Equal(PlacementState.Measuring, CoreScheduling.Judge(X3D, Measure(X3D, Loads(40, 10), 2), "Game").State);
        Assert.Equal(PlacementState.Idle, CoreScheduling.Judge(X3D, Measure(X3D, Loads(1, 1)), "Game").State);
    }

    [Fact]
    public void OnlyTheNewestReadingsCount()
    {
        var placement = Measure(X3D, Loads(5, 45), CorePlacement.WindowSamples);
        for (int i = 0; i < CorePlacement.WindowSamples; i++)
        {
            placement.Add(Loads(45, 5));
        }

        Assert.Equal(0.9, placement.Shares[0], 6);
        Assert.Equal(CorePlacement.WindowSamples, placement.Samples);
    }

    [Fact]
    public void UnknownLoads_AreLeftOut()
    {
        var placement = new CorePlacement(X3D);
        Assert.False(placement.Add([.. Enumerable.Repeat(double.NaN, 32)]));
        Assert.False(placement.Add([]));
        Assert.Equal(0, placement.Samples);
    }

    [Fact]
    public void CcdsAlike_GoodWhenOnOneCcd_MixedWhenSpread()
    {
        var layout = CoreLayout.Parse(Topology.Ryzen9950X());

        Assert.Equal("Game: 90 % of the load on CCD 2", CoreScheduling.Judge(layout, Measure(layout, Loads(5, 45)), "Game").Text);
        var spread = CoreScheduling.Judge(layout, Measure(layout, Loads(25, 25)), "Game");
        Assert.Equal(PlacementState.Mixed, spread.State);
        Assert.Equal("Game: spread across the CCDs (50 % / 50 %)", spread.Text);
    }
}

public class CoreSchedulingChecksTests
{
    private static readonly CoreLayout X3D = CoreLayout.Parse(Topology.Ryzen9950X3D());
    private static readonly PowerPlan Balanced = new(PowerPlans.Balanced, "Balanced", true);

    [Fact]
    public void AWorkingSetup_PassesEveryCheck()
    {
        var amd = new AmdVCacheStatus(ServiceState.Running, ServiceState.Running, 0, []);

        var checks = CoreScheduling.Checks(X3D, amd, gameModeOn: true, Balanced);

        Assert.Equal(["AMD 3D V-Cache driver", "AMD 3D V-Cache service", "AMD preference", "Game Mode", "Power plan"], checks.Select(c => c.Name));
        Assert.All(checks, c => Assert.Equal(CheckState.Good, c.State));
        Assert.Equal("Frequency first", checks[2].Status);
    }

    [Fact]
    public void WhatBreaksAmdsScheduling_IsFlagged()
    {
        var ultimate = new PowerPlan(Guid.NewGuid(), "Ultimate Performance", true);

        var checks = CoreScheduling.Checks(X3D, AmdVCacheStatus.None, gameModeOn: false, ultimate);

        Assert.Equal(CheckState.Bad, checks.Single(c => c.Name == "AMD 3D V-Cache driver").State);
        Assert.Contains("chipset driver", checks[0].Detail);
        Assert.Equal(CheckState.Warn, checks.Single(c => c.Name == "AMD 3D V-Cache service").State);
        Assert.DoesNotContain(checks, c => c.Name == "AMD preference");
        Assert.Equal(CheckState.Bad, checks.Single(c => c.Name == "Game Mode").State);
        var plan = checks.Single(c => c.Name == "Power plan");
        Assert.Equal(CheckState.Info, plan.State); // shown in grey: a trade-off to know about, not an error
        Assert.Contains("core parking", plan.Detail);
        Assert.True(CoreScheduling.StopsParking(ultimate));
        Assert.True(CoreScheduling.StopsParking(new PowerPlan(PowerPlans.HighPerformance, "High performance", true)));
        Assert.False(CoreScheduling.StopsParking(Balanced));
    }

    [Fact]
    public void OnAHybridIntel_OnlyGameModeIsChecked()
    {
        var checks = CoreScheduling.Checks(CoreLayout.Parse(Topology.Core14900K()), AmdVCacheStatus.None, gameModeOn: false, Balanced);

        var check = Assert.Single(checks);
        Assert.Equal("Game Mode", check.Name);
        Assert.Equal(CheckState.Warn, check.State);
    }

    [Fact]
    public void AmdsPerProgramPreference_MatchesTheEndOfTheProgramsPath()
    {
        var amd = new AmdVCacheStatus(ServiceState.Running, ServiceState.Running, 0, [new AmdAppPreference("cp", "Cyberpunk2077.exe", CoreKind.Frequency)]);

        Assert.Equal(CoreKind.Frequency, amd.For(@"D:\Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe")!.Prefers);
        Assert.NotNull(amd.For("cyberpunk2077.exe"));
        Assert.Null(amd.For("witcher3.exe"));
        Assert.Null(amd.For(null));
        Assert.Equal(CoreKind.Cache, AmdVCacheStatus.Kind(1));
        Assert.Equal(CoreKind.Frequency, AmdVCacheStatus.Kind(0));
        Assert.Null(AmdVCacheStatus.Kind(7));
    }

    [Fact]
    public void ACustomAffinity_IsOneThatLeavesCoresOut()
    {
        Assert.True(new ProcessAffinity(0xFF, 0xFFFF).IsCustom);
        Assert.False(new ProcessAffinity(0xFFFF, 0xFFFF).IsCustom);
        Assert.False(new ProcessAffinity(0, 0xFFFF).IsCustom); // threads in several processor groups
    }

    [Theory]
    [InlineData(0.06, "+6 %")]
    [InlineData(-0.021, "−2 %")]
    [InlineData(0.001, "±0 %")]
    public void Gains_AreSigned(double gain, string text) => Assert.Equal(text, CoreScheduling.Gain(gain));
}

public class AffinityTrialTests
{
    private static readonly CoreGroup Cache = CoreLayout.Parse(Topology.Ryzen9950X3D()).Groups[0];

    private static PacingSecond Second(double fps, double p99Ms) => new(fps, 1000 / fps, p99Ms, (int)fps, 0, p99Ms);

    /// <summary>Plays the four rounds: <paramref name="windows"/> on Windows' choice, <paramref name="cache"/> on the cache cores.</summary>
    private static (AffinityTrial Trial, int Switches) Play(Func<int, PacingSecond> windows, Func<int, PacingSecond> cache)
    {
        var trial = new AffinityTrial(Cache);
        int switches = 0;
        for (int round = 0; round < AffinityTrial.Rounds; round++)
        {
            Assert.Equal(round % 2 == 1, trial.OnCandidate);
            for (int i = 0; i < AffinityTrial.RoundSeconds; i++)
            {
                if (trial.Add(trial.OnCandidate ? cache(round) : windows(round)))
                {
                    switches++;
                }
            }
        }

        return (trial, switches);
    }

    [Fact]
    public void AClearGainInBothRounds_IsProven()
    {
        var (trial, switches) = Play(_ => Second(100, 12.5), _ => Second(108, 11.8));

        Assert.True(trial.IsDone);
        Assert.Equal(3, switches); // Windows → cache → Windows → cache
        var result = trial.Result();
        Assert.True(result.Proven);
        Assert.Equal(0.08, result.FpsGain, 6);
        Assert.Equal("+8 % FPS, +6 % 1% lows", result.GainText);
    }

    [Fact]
    public void BetterLowsAlone_AreProvenToo()
    {
        var result = Play(_ => Second(141, 12), _ => Second(141, 10.5)).Trial.Result(); // a frame limit holds the average

        Assert.True(result.Proven);
        Assert.Equal(0, result.FpsGain, 6);
        Assert.True(result.LowGain > 0.1);
    }

    [Fact]
    public void ASmallOrInconsistentGain_IsNotProven()
    {
        Assert.False(Play(_ => Second(100, 12), _ => Second(101, 12)).Trial.Result().Proven); // 1 %

        // A big gain in one pair only (a lighter scene in round 2): not both rounds, so not proven.
        var uneven = Play(_ => Second(100, 12), round => round == 1 ? Second(130, 9) : Second(95, 12.5)).Trial.Result();
        Assert.True(uneven.FpsGain > 0.03);
        Assert.False(uneven.Proven);

        // More FPS but worse lows: not proven.
        Assert.False(Play(_ => Second(100, 12), _ => Second(110, 14)).Trial.Result().Proven);
    }

    [Fact]
    public void LoadingScreensAndTheFirstSecondsOfEachRound_DontCount()
    {
        var trial = new AffinityTrial(Cache);
        Assert.False(trial.Add(Second(5, 400)));
        Assert.False(trial.Add(new PacingSecond(60, 16, 16, 60, 0, 900)));
        Assert.Equal(2, trial.Skipped);
        Assert.Equal(0, trial.Seconds);

        // A spike in the settling seconds after a switch doesn't count against the round.
        for (int i = 0; i < AffinityTrial.RoundSeconds; i++)
        {
            trial.Add(Second(100, 12));
        }

        for (int i = 0; i < AffinityTrial.RoundSeconds; i++)
        {
            trial.Add(i < AffinityTrial.SettleSeconds ? Second(40, 60) : Second(110, 11));
        }

        Assert.Equal(2, trial.Round);
        Assert.Equal("Round 3 of 4 · Windows' choice · 30 s left", trial.ProgressText);
    }

    [Fact]
    public void AnUnfinishedTest_ProvesNothing()
    {
        var trial = new AffinityTrial(Cache);
        trial.Add(Second(100, 12));
        Assert.False(trial.Result().Proven);
        Assert.Equal(120, AffinityTrial.TotalSeconds);
    }
}

public class CoreProfileTests
{
    [Fact]
    public void Normalize_KeepsTheNewestPerProgram_AndNeverTheSlowerCores()
    {
        var profiles = CoreProfile.Normalize(
        [
            new CoreProfile { Program = @"D:\Games\Cyberpunk2077.exe", Name = "Cyberpunk 2077", FpsGain = 0.04 },
            new CoreProfile { Program = "CYBERPUNK2077.EXE", Name = "Cyberpunk 2077", FpsGain = 0.07 },
            new CoreProfile { Program = "witcher3.exe", Cores = CoreKind.Frequency },
            new CoreProfile { Program = "game.exe", Cores = CoreKind.Efficiency },
            new CoreProfile { Program = " ", Name = "nothing" },
            new CoreProfile { Program = "eldenring.exe", Name = " ", FpsGain = double.NaN },
        ]);

        Assert.Equal(["CYBERPUNK2077.EXE", "eldenring.exe"], profiles.Select(p => p.Program));
        Assert.Equal(0.07, profiles[0].FpsGain);
        Assert.Equal("eldenring.exe", profiles[1].Name);
        Assert.Equal(0, profiles[1].FpsGain);
        Assert.Same(profiles[0], CoreProfile.For(profiles, @"C:\x\cyberpunk2077.exe"));
        Assert.Null(CoreProfile.For(profiles, null));
    }
}
