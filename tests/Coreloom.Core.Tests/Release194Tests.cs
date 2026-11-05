using System.Buffers.Binary;
using System.Text;
using Coreloom.Core.Gaming;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

/// <summary>1.9.4: the thinking behind the Mac's eight new Gaming boosts.</summary>
public class Release194Tests
{
    private static readonly DateTime T0 = new(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);

    // ---- High Power Mode automation -------------------------------------------------------------------------

    [Fact]
    public void Workload_StartsWithAGame_OrALongBusyGpu_AndEndsAfterAQuietSpell()
    {
        var detector = new WorkloadDetector();
        Assert.True(detector.Update(T0, "Ember Road", 10));
        Assert.Equal("Ember Road", detector.Reason);

        // The game quits: still active through loading screens and menus, until 45 quiet seconds.
        Assert.True(detector.Update(T0.AddSeconds(2), null, 5));
        Assert.True(detector.Update(T0.AddSeconds(40), null, 5));
        Assert.False(detector.Update(T0.AddSeconds(48), null, 5));
        Assert.Null(detector.Reason);

        // A busy GPU without a known game counts after 20 seconds, not before.
        var gpu = new WorkloadDetector();
        Assert.False(gpu.Update(T0, null, 85));
        Assert.False(gpu.Update(T0.AddSeconds(15), null, 85));
        Assert.True(gpu.Update(T0.AddSeconds(21), null, 85));
        Assert.Equal("a graphics workload", gpu.Reason);

        // A dip under 60 % restarts the count.
        var dip = new WorkloadDetector();
        dip.Update(T0, null, 80);
        dip.Update(T0.AddSeconds(10), null, 40);
        Assert.False(dip.Update(T0.AddSeconds(25), null, 80));
    }

    [Fact]
    public void Power_RaisesOnTheCharger_RestoresAfter_AndForgetsAHandChange()
    {
        // Up on the charger when a workload starts, from Automatic.
        var raise = HighPowerAutomation.Decide(hasHighPowerMode: true, onCharger: true, workload: true, chargerMode: MacPowerModes.Automatic, raisedFrom: null);
        Assert.Equal(PowerStep.RaiseToHigh, raise.Step);

        // On battery or already High Power: nothing.
        Assert.Equal(PowerStep.None, HighPowerAutomation.Decide(true, false, true, MacPowerModes.Automatic, null).Step);
        Assert.Equal(PowerStep.None, HighPowerAutomation.Decide(true, true, true, MacPowerModes.HighPower, null).Step);

        // No High Power Mode on this Mac: never.
        Assert.Equal(PowerStep.None, HighPowerAutomation.Decide(false, true, true, MacPowerModes.Automatic, null).Step);

        // Still playing: keep it. Done: back to what it was (Low Power, say).
        Assert.Equal(PowerStep.None, HighPowerAutomation.Decide(true, true, true, MacPowerModes.HighPower, MacPowerModes.LowPower).Step);
        var restore = HighPowerAutomation.Decide(true, true, false, MacPowerModes.HighPower, MacPowerModes.LowPower);
        Assert.Equal(PowerStep.Restore, restore.Step);
        Assert.Equal(MacPowerModes.LowPower, restore.RestoreTo);

        // Someone changed it by hand meanwhile: Coreloom leaves it and forgets.
        Assert.Equal(PowerStep.Forget, HighPowerAutomation.Decide(true, true, false, MacPowerModes.Automatic, MacPowerModes.Automatic).Step);
    }

    [Fact]
    public void Power_SudoersRule_AllowsOnlyTheChargersPowerMode_ForASafeUserName()
    {
        Assert.Equal("jerry", HighPowerAutomation.SafeUserName("jerry"));
        Assert.Equal("j.ubah_2-x", HighPowerAutomation.SafeUserName("j.ubah_2-x"));
        Assert.Null(HighPowerAutomation.SafeUserName("jerry ALL=(ALL) ALL"));
        Assert.Null(HighPowerAutomation.SafeUserName("-jerry"));
        Assert.Null(HighPowerAutomation.SafeUserName("jé"));
        Assert.Null(HighPowerAutomation.SafeUserName(""));

        string rule = HighPowerAutomation.SudoersRule("jerry");
        Assert.Equal("jerry ALL=(root) NOPASSWD: /usr/bin/pmset -c powermode 0, /usr/bin/pmset -c powermode 1, /usr/bin/pmset -c powermode 2", rule);
        Assert.DoesNotContain("*", rule);
        Assert.DoesNotContain(".", Path.GetFileName(HighPowerAutomation.SudoersFile)); // sudo skips files with a dot
    }

    // ---- Memory pressure guard -------------------------------------------------------------------------------

    private static MemorySnapshot Memory(MemoryPressure pressure, long swap = 0) => new(pressure, 16L << 30, 14L << 30, 3L << 30, swap,
    [
        new("Ember Road", 6L << 30), new("Google Chrome", 2_500_000_000), new("Slack", 900_000_000), new("WindowServer", 700_000_000),
        new("Photos", 620_000_000), new("Music", 200_000_000),
    ]);

    [Fact]
    public void MemoryGuard_SuggestsTheBiggestApps_NeverTheGameOrMacOS()
    {
        var verdict = new MemoryGuard().Evaluate(Memory(MemoryPressure.Warning), T0, "Ember Road");
        Assert.Equal("Elevated", verdict.Level);
        Assert.True(verdict.Concern);
        Assert.Equal(["Google Chrome", "Slack", "Photos"], verdict.Suggested.Select(a => a.Name));
        Assert.Contains("Quit Google Chrome (2.3 GB), Slack (858 MB) and Photos (591 MB)", verdict.Advice);
        Assert.Contains("Ember Road", verdict.Advice);

        Assert.Equal("High", new MemoryGuard().Evaluate(Memory(MemoryPressure.Critical), T0, null).Level);
        var calm = new MemoryGuard().Evaluate(Memory(MemoryPressure.Normal), T0, "Ember Road");
        Assert.Equal("Normal", calm.Level);
        Assert.Null(calm.Advice);
    }

    [Fact]
    public void MemoryGuard_GrowingSwapDuringAGame_IsElevated_EvenAtNormalPressure()
    {
        var guard = new MemoryGuard();
        guard.Evaluate(Memory(MemoryPressure.Normal, swap: 0), T0, "Ember Road");
        var verdict = guard.Evaluate(Memory(MemoryPressure.Normal, swap: 200L << 20), T0.AddMinutes(1), "Ember Road");
        Assert.Equal("Elevated", verdict.Level);
        Assert.True(verdict.SwapGrowthPerMinute >= MemoryGuard.SwapGrowthWarning);

        // Without a game, the same growth is just how macOS works.
        var idle = new MemoryGuard();
        idle.Evaluate(Memory(MemoryPressure.Normal, swap: 0), T0, null);
        Assert.Equal("Normal", idle.Evaluate(Memory(MemoryPressure.Normal, swap: 200L << 20), T0.AddMinutes(1), null).Level);
    }

    // ---- Thermal headroom ------------------------------------------------------------------------------------

    [Fact]
    public void Thermal_HeadroomFollowsMacOSsState_AndCountsSlowdowns()
    {
        var tracker = new ThermalTracker();
        tracker.Add(new ThermalSample(T0, ThermalPressure.Nominal, 62, 58, 85, 90, 4000, 1300, 2000));
        Assert.Equal("Plenty", tracker.Evaluate().Headroom);
        Assert.Equal(0, tracker.Events);

        tracker.Add(new ThermalSample(T0.AddSeconds(2), ThermalPressure.Fair, 80, 76, 85, 90, 4000, 1300, 3500));
        Assert.Equal("Some", tracker.Evaluate().Headroom);

        // Still busy, but the GPU's clock is 20 % under its peak: slowed by heat, counted once.
        tracker.Add(new ThermalSample(T0.AddSeconds(4), ThermalPressure.Fair, 92, 96, 85, 92, 4000, 1040, 5000));
        var slowed = tracker.Evaluate();
        Assert.True(slowed.Throttling);
        Assert.Equal("Little", slowed.Headroom);
        Assert.Equal(1, slowed.Events);
        Assert.Equal(0.2, slowed.ClockDrop!.Value, 3);
        tracker.Add(new ThermalSample(T0.AddSeconds(6), ThermalPressure.Serious, 95, 97, 85, 92, 4000, 1000, 5600));
        Assert.Equal(1, tracker.Events); // the same slowdown going on

        tracker.Add(new ThermalSample(T0.AddSeconds(8), ThermalPressure.Critical, 99, 99, 85, 92, 3000, 900, 6000));
        Assert.Equal("None", tracker.Evaluate().Headroom);
        Assert.Contains("Pause", tracker.Evaluate().Advice);
    }

    [Fact]
    public void Thermal_AnIdleChipRunningSlow_IsNotThrottling()
    {
        var tracker = new ThermalTracker();
        tracker.Add(new ThermalSample(T0, ThermalPressure.Nominal, 60, 55, 90, 90, 4000, 1300, null));
        tracker.Add(new ThermalSample(T0.AddSeconds(2), ThermalPressure.Nominal, 55, 50, 10, 5, 1200, 400, null));
        Assert.False(tracker.Evaluate().Throttling);
        Assert.Null(tracker.Evaluate().ClockDrop);
    }

    // ---- Performance cores -----------------------------------------------------------------------------------

    private static CoreTimes Times(int pid, string name, double cores, double pShare, int qos, int seconds) =>
        Build(pid, name, (ulong)(cores * 1e9 * seconds), pShare, qos);

    private static CoreTimes Build(int pid, string name, ulong ticks, double pShare, int qos)
    {
        // The rusage figures in another unit (×41.67, as on Apple silicon): only their shares matter.
        ulong total = (ulong)(ticks * 41.67);
        return new CoreTimes(pid, name, ticks, total, (ulong)(total * pShare),
            qos == 0 ? total : 0, qos == 1 ? total : 0, qos == 2 ? total : 0, qos == 3 ? total : 0, qos == 4 ? total : 0);
    }

    [Fact]
    public void Cores_ShareTheirTimeOut_AndFindWhatCompetesWithTheGame()
    {
        var monitor = new CoreWorkloadMonitor();
        Assert.Empty(monitor.Update([Times(1, "Ember Road", 3, 0.95, 0, 0), Times(2, "Google Chrome", 0.4, 0.9, 1, 0), Times(3, "Spotlight", 0.3, 0.05, 4, 0),
            Times(4, "WindowServer", 0.4, 0.8, 0, 0), Times(5, "Music", 0.05, 0.1, 3, 0)], T0, 1e-9, "Ember Road"));
        var users = monitor.Update([Times(1, "Ember Road", 3, 0.95, 0, 4), Times(2, "Google Chrome", 0.4, 0.9, 1, 4), Times(3, "Spotlight", 0.3, 0.05, 4, 4),
            Times(4, "WindowServer", 0.4, 0.8, 0, 4), Times(5, "Music", 0.05, 0.1, 3, 4)], T0.AddSeconds(4), 1e-9, "Ember Road");

        Assert.Equal("Ember Road", users[0].Name);
        Assert.Equal(3, users[0].CoresUsed, 2);
        Assert.Equal(0.95, users[0].PerformanceShare, 2);
        Assert.True(users[0].IsGame);
        Assert.False(users[0].Competes);

        var chrome = users.Single(u => u.Name == "Google Chrome");
        Assert.Equal(0.4, chrome.CoresUsed, 2);
        Assert.Equal("High", chrome.Priority);
        Assert.True(chrome.Competes);

        Assert.False(users.Single(u => u.Name == "Spotlight").Competes); // background work, on efficiency cores
        Assert.Equal("Background", users.Single(u => u.Name == "Spotlight").Priority);
        Assert.False(users.Single(u => u.Name == "WindowServer").Competes); // macOS's own
        Assert.Equal("Utility", users.Single(u => u.Name == "Music").Priority);

        // Without a game nothing competes.
        var quiet = new CoreWorkloadMonitor();
        quiet.Update([Times(2, "Google Chrome", 0.4, 0.9, 1, 0)], T0, 1e-9, null);
        Assert.False(quiet.Update([Times(2, "Google Chrome", 0.4, 0.9, 1, 4)], T0.AddSeconds(4), 1e-9, null).Single().Competes);
    }

    [Fact]
    public void Cores_ProcessesOfOneApp_AreAddedUp_AndRestartedOnesAreSkipped()
    {
        var monitor = new CoreWorkloadMonitor();
        monitor.Update([Times(1, "Google Chrome", 0.2, 0.9, 1, 0), Times(2, "Google Chrome", 0.2, 0.9, 1, 0), Times(3, "Slack", 1, 0.5, 2, 10)], T0, 1e-9, null);
        var users = monitor.Update([Times(1, "Google Chrome", 0.2, 0.9, 1, 2), Times(2, "Google Chrome", 0.2, 0.9, 1, 2), Times(3, "Slack", 1, 0.5, 2, 1)], T0.AddSeconds(2), 1e-9, null);
        Assert.Equal(0.4, users.Single(u => u.Name == "Google Chrome").CoresUsed, 2);
        Assert.DoesNotContain(users, u => u.Name == "Slack"); // its counters went backwards: a new process with the same id
    }

    // ---- MetalFX ---------------------------------------------------------------------------------------------

    [Fact]
    public void MetalFx_FindsEachFeaturesClassName()
    {
        byte[] Text(string s) => Encoding.ASCII.GetBytes(s);
        Assert.Equal(MetalFxFeatures.None, MetalFx.Scan(Text("Metal shaders and nothing else")));
        var found = MetalFx.Scan(Text("/System/Library/Frameworks/MetalFX.framework/Versions/A/MetalFX _OBJC_CLASS_$_MTLFXTemporalScalerDescriptor _OBJC_CLASS_$_MTLFXFrameInterpolatorDescriptor"));
        Assert.Equal(MetalFxFeatures.Linked | MetalFxFeatures.TemporalUpscaling | MetalFxFeatures.FrameInterpolation, found);
        Assert.Equal("Temporal upscaling · Frame interpolation", MetalFx.Describe(found));
        Assert.Contains("frame generation", MetalFx.Advice(found));

        var denoised = MetalFx.Scan(Text("MTLFXTemporalDenoisedScalerDescriptor"));
        Assert.Equal(MetalFxFeatures.Denoising, denoised); // not mistaken for the plain temporal scaler
        Assert.Contains("denoiser", MetalFx.Advice(denoised));
        Assert.Equal("MetalFX, features not named", MetalFx.Describe(MetalFxFeatures.Linked));
        Assert.Equal("Not used", MetalFx.Describe(MetalFxFeatures.None));
    }

    [Fact]
    public void MetalFx_StreamScan_FindsANameAcrossTwoPieces()
    {
        byte[] data = new byte[3000];
        byte[] name = Encoding.ASCII.GetBytes("MTLFXSpatialScalerDescriptor");
        name.CopyTo(data, 1000 - 10); // straddles the 1000-byte pieces
        Assert.Equal(MetalFxFeatures.SpatialUpscaling, MetalFx.Scan(new MemoryStream(data), chunk: 1000));
        Assert.Equal(MetalFxFeatures.None, MetalFx.Scan(new MemoryStream(data), maxBytes: 900, chunk: 1000));
    }

    /// <summary>A small 64-bit Mach-O program: __TEXT with a __cstring section, __LINKEDIT, and filler in between.</summary>
    private static byte[] Program(string cstring, string linkedit, string filler, int cpuType = MachO.CpuTypeArm64)
    {
        const int commandsSize = 72 + 80 + 72;
        byte[] file = new byte[4096];
        var span = file.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], cpuType);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], commandsSize);
        int at = 32;
        // LC_SEGMENT_64 __TEXT with one section, __cstring at 1024
        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 4)..], 72 + 80);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(file, at + 8);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(at + 40)..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(at + 48)..], 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 64)..], 1);
        int section = at + 72;
        Encoding.ASCII.GetBytes("__cstring").CopyTo(file, section);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(file, section + 16);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(section + 40)..], (ulong)cstring.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 48)..], 1024);
        at += 72 + 80;
        // LC_SEGMENT_64 __LINKEDIT at 3072
        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 4)..], 72);
        Encoding.ASCII.GetBytes("__LINKEDIT").CopyTo(file, at + 8);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(at + 40)..], 3072);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(at + 48)..], 1024);
        Encoding.ASCII.GetBytes(cstring).CopyTo(file, 1024);
        Encoding.ASCII.GetBytes(filler).CopyTo(file, 2200); // code, not a name: not read
        Encoding.ASCII.GetBytes(linkedit).CopyTo(file, 3072);
        return file;
    }

    [Fact]
    public void MetalFx_InAProgram_ReadsOnlyWhereNamesAreKept()
    {
        byte[] program = Program("MTLFXTemporalScalerDescriptor", "_OBJC_CLASS_$_MTLFXFrameInterpolatorDescriptor", "MTLFXSpatialScalerDescriptor");
        var regions = MachO.NameRegions(program);
        Assert.Contains(regions, r => r.Offset == 1024 && r.Length == "MTLFXTemporalScalerDescriptor".Length);
        Assert.Contains(regions, r => r.Offset == 3072 && r.Length == 1024);
        Assert.Equal(MetalFxFeatures.TemporalUpscaling | MetalFxFeatures.FrameInterpolation, MetalFx.ScanProgram(new MemoryStream(program)));

        // Not a program: nothing.
        Assert.Equal(MetalFxFeatures.None, MetalFx.ScanProgram(new MemoryStream(Encoding.ASCII.GetBytes("#!/bin/sh\nMTLFXTemporalScalerDescriptor\n"))));
    }

    [Fact]
    public void MachO_UniversalProgram_PicksTheAppleSiliconSlice()
    {
        byte[] intel = Program("nothing", "nothing", "x", MachO.CpuTypeX86_64);
        byte[] arm = Program("MTLFXSpatialScalerDescriptor", "nothing", "x", MachO.CpuTypeArm64);
        byte[] fat = new byte[4096 + intel.Length + arm.Length];
        var span = fat.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 2);
        void Entry(int index, int cpu, int offset, int size)
        {
            var e = fat.AsSpan(8 + (index * 20));
            BinaryPrimitives.WriteInt32BigEndian(e, cpu);
            BinaryPrimitives.WriteUInt32BigEndian(e[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(e[12..], (uint)size);
        }

        Entry(0, MachO.CpuTypeX86_64, 4096, intel.Length);
        Entry(1, MachO.CpuTypeArm64, 4096 + intel.Length, arm.Length);
        intel.CopyTo(fat, 4096);
        arm.CopyTo(fat, 4096 + intel.Length);

        Assert.Equal(CpuArchitecture.Universal, MachO.Architecture(fat));
        Assert.Equal((4096L + intel.Length, (long)arm.Length), MachO.PreferredSlice(fat, fat.Length));
        Assert.Equal(MetalFxFeatures.SpatialUpscaling, MetalFx.ScanProgram(new MemoryStream(fat)));

        Assert.Equal(CpuArchitecture.Arm64, MachO.Architecture(arm));
        Assert.Equal(CpuArchitecture.X86_64, MachO.Architecture(intel));
        Assert.True(NativeApps.RunsTranslated(MachO.Architecture(intel)));
        Assert.Contains("Rosetta 2", NativeApps.Advice("Neon Drift", CpuArchitecture.X86_64));

        // A Java class file shares the universal magic; its "count" is a version in the 40s or more.
        byte[] java = [0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x41, .. new byte[100]];
        Assert.Equal(CpuArchitecture.Unknown, MachO.Architecture(java));
        Assert.Equal(CpuArchitecture.Unknown, MachO.Architecture(Encoding.ASCII.GetBytes("MZ Windows program")));
    }

    // ---- Displays --------------------------------------------------------------------------------------------

    [Fact]
    public void Displays_SuggestTheFasterRefresh_AndTheDefaultSize()
    {
        var verdicts = DisplayPipeline.Evaluate(
        [
            new DisplayDescription("Built-in Liquid Retina XDR Display", true, new DisplayMode(3024, 1964, 1512, 982, 120), 120, true, 16, 10, 3024, 1964),
            new DisplayDescription("Built-in display", true, new DisplayMode(3024, 1964, 1512, 982, 60), 120, false, 16, 10, 3024, 1964),
            new DisplayDescription("Desk monitor", false, new DisplayMode(2560, 1440, 2560, 1440, 60), 165, false, 1, 8, 2560, 1440),
            new DisplayDescription("Built-in display", true, new DisplayMode(3456, 2234, 1728, 1117, 120), 120, true, 16, 10, 3024, 1964),
        ]);

        Assert.True(verdicts[0].Good);
        Assert.Equal("Up to 120 Hz ProMotion · 3024 × 1964", verdicts[0].Value);
        Assert.Contains("HDR ready (16× SDR brightness)", verdicts[0].Detail);
        Assert.Contains("10-bit colour", verdicts[0].Detail);
        Assert.Contains("looks like 1512 × 982", verdicts[0].Detail);

        Assert.False(verdicts[1].Good);
        Assert.Contains("choose ProMotion", verdicts[1].Detail);

        Assert.False(verdicts[2].Good);
        Assert.Contains("165 Hz", verdicts[2].Detail);
        Assert.Contains("SDR", verdicts[2].Detail);

        Assert.False(verdicts[3].Good); // "More Space": draws more than the panel has
        Assert.Contains("Default size", verdicts[3].Detail);
    }

    // ---- Background apps -------------------------------------------------------------------------------------

    [Fact]
    public void Governor_ListsTheHeavyAppsFirst_NeverTheGameMacOSOrLaunchers()
    {
        var apps = new List<RunningAppInfo>
        {
            new(1, "Ember Road", "com.example.ember", "/Applications/Ember Road.app", 300, 6L << 30, 80, true),
            new(2, "Notes", "com.apple.Notes", "/System/Applications/Notes.app", 0.2, 90_000_000, 0, true),
            new(3, "Google Chrome", "com.google.Chrome", "/Applications/Google Chrome.app", 14, 2_500_000_000, 3, true),
            new(4, "Steam", "com.valvesoftware.steam", "/Applications/Steam.app", 4, 500_000_000, 1, true),
            new(5, "Finder", "com.apple.finder", "/System/Library/CoreServices/Finder.app", 0.5, 100_000_000, 0, true),
            new(6, "Slack", "com.tinyspeck.slackmacgap", "/Applications/Slack.app", 1, 700_000_000, 0, true),
            new(7, "Helper", "com.example.helper", null, 9, 10_000_000, null, false),
        };

        var ranked = BackgroundGovernor.Rank(apps, "Ember Road", 1);
        Assert.Equal(["Google Chrome", "Slack", "Notes"], ranked.Select(c => c.App.Name));
        Assert.True(ranked[0].Heavy);
        Assert.True(ranked[1].Heavy); // 700 MB
        Assert.False(ranked[2].Heavy);
        Assert.Equal("14 % CPU · 3 % GPU · 2.3 GB", ranked[0].Usage);
        Assert.Equal("0.2 % CPU · 86 MB", ranked[2].Usage);
    }

    [Fact]
    public void QuitProfiles_AreKeptTidy_AndOlderSettingsHaveTheBoostsOff()
    {
        var gaming = new GamingSettings
        {
            QuitProfiles =
            [
                new() { GameId = "steam:1", Name = "Ember Road", BundleIds = ["com.google.Chrome", "com.google.Chrome", " ", "com.tinyspeck.slackmacgap"] },
                new() { GameId = "steam:1", Name = "Again", BundleIds = ["x"] },
                new() { GameId = "steam:2", Name = "Empty", BundleIds = [] },
                new() { GameId = "", BundleIds = ["y"] },
            ],
            AppsToReopen = ["/Applications/Slack.app", "/Applications/Slack.app", ""],
            AutomationRaisedFrom = 7,
        };
        gaming.Normalize();
        Assert.Single(gaming.QuitProfiles);
        Assert.Equal(["com.google.Chrome", "com.tinyspeck.slackmacgap"], gaming.QuitProfiles[0].BundleIds);
        Assert.Equal(["/Applications/Slack.app"], gaming.AppsToReopen);
        Assert.Null(gaming.AutomationRaisedFrom);
        Assert.Equal("Ember Road", gaming.QuitProfileFor("STEAM:1")!.Name);

        var fresh = new AppSettings().Gaming;
        Assert.False(fresh.HighPowerAutomation || fresh.MemoryGuard || fresh.ThermalHeadroom || fresh.CoreWorkload
            || fresh.MetalFx || fresh.DisplayPipeline || fresh.NativeApps || fresh.BackgroundGovernor);

        string json = SettingsStore.Serialize(new AppSettings());
        var read = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize();
        Assert.Empty(read.Gaming.QuitProfiles);
    }
}
