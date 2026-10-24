using Coreloom.Core.Hardware;
using Coreloom.Hardware.Mac;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Providers;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Tests;

/// <summary>The macOS back-end's decoding and arithmetic, checked on every platform.</summary>
public sealed class DecodingTests
{
    [Theory]
    [InlineData("sp78", new byte[] { 0x21, 0x10 }, 33.0625)] // TC0P on the Intel runner
    [InlineData("sp87", new byte[] { 0x10, 0xbf }, 33.4921875)] // PCPC on the Intel runner
    [InlineData("flt ", new byte[] { 0x45, 0x14, 0x44, 0x42 }, 49.0198)] // PSTR, little-endian float
    [InlineData("flt ", new byte[] { 0x00, 0x40, 0x9c, 0x45 }, 5000)] // F0Mx
    [InlineData("fpe2", new byte[] { 0x1f, 0x40 }, 2000)] // an Intel Mac's fan speed
    [InlineData("ui8 ", new byte[] { 0x02 }, 2)]
    [InlineData("ui16", new byte[] { 0x01, 0x00 }, 256)]
    [InlineData("ui32", new byte[] { 0x00, 0x00, 0x02, 0x8d }, 653)] // #KEY
    [InlineData("si16", new byte[] { 0xff, 0xfe }, -2)]
    public void Smc_DecodesItsTypes(string type, byte[] bytes, double expected)
    {
        Assert.Equal(expected, Smc.Decode(bytes, type)!.Value, 3);
    }

    [Fact]
    public void Smc_UnknownTypesAreNotGuessed()
    {
        Assert.Null(Smc.Decode([1, 2, 3, 4], "ch8*"));
        Assert.Null(Smc.Decode([], "flt "));
    }

    [Fact]
    public void Smc_KeysAreBigEndianFourCharacterCodes()
    {
        uint key = Smc.Connection.Key("TC0P");
        Assert.Equal(0x54433050u, key);
        Assert.Equal("TC0P", Smc.Connection.KeyName(key));
        Assert.Equal(0u, Smc.Connection.Key("TOO-LONG"));
    }

    [Fact]
    public unsafe void NativeStructs_MatchTheSystemHeaders()
    {
        Assert.Equal(80, sizeof(Smc.KeyData));
        Assert.Equal(152, sizeof(Darwin.VmStatistics64));
        Assert.Equal(Darwin.HostVmInfo64Count * 4, sizeof(Darwin.VmStatistics64));
        Assert.Equal(32, sizeof(Darwin.XswUsage));
        Assert.Equal(96, sizeof(Darwin.ProcTaskInfo));
        Assert.Equal(296, sizeof(Darwin.RusageInfoV4));
        Assert.Equal(2168, sizeof(FileSystems.StatFs));
    }

    [Theory]
    [InlineData(5000, "mJ", 0.5, 10)]
    [InlineData(5_000_000, "uJ", 1, 5)]
    [InlineData(2_000_000_000, "nJ", 2, 1)]
    public void IOReport_EnergyOverTimeIsPower(long energy, string unit, double seconds, double watts)
    {
        Assert.Equal(watts, IOReportMath.Watts(energy, unit, seconds), 6);
    }

    [Fact]
    public void IOReport_UnknownUnitsAndBadIntervalsGiveNothing()
    {
        Assert.True(double.IsNaN(IOReportMath.Watts(100, "ticks", 1)));
        Assert.True(double.IsNaN(IOReportMath.Watts(100, "mJ", 0)));
    }

    [Theory]
    [InlineData("CPU Energy", EnergyKind.Cpu)]
    [InlineData("DIE_1_CPU Energy", EnergyKind.Cpu)]
    [InlineData("GPU Energy", EnergyKind.Gpu)]
    [InlineData("ANE", EnergyKind.NeuralEngine)]
    [InlineData("ANE0_1", EnergyKind.NeuralEngine)]
    [InlineData("DRAM", EnergyKind.Memory)]
    [InlineData("EACC_CPU0", EnergyKind.Other)]
    [InlineData("GPU SRAM", EnergyKind.Other)]
    public void IOReport_EnergyChannelsAreSortedByPart(string channel, EnergyKind kind)
    {
        Assert.Equal(kind, IOReportMath.EnergyChannel(channel));
    }

    [Theory]
    [InlineData("ECPU", false)]
    [InlineData("PCPU", true)]
    [InlineData("PCPU1", true)]
    [InlineData("EACC0", false)]
    public void IOReport_ClustersAreTold(string channel, bool performance)
    {
        Assert.Equal(performance, IOReportMath.ClusterKind(channel));
    }

    [Fact]
    public void IOReport_OtherChannelsAreNotClusters()
    {
        Assert.Null(IOReportMath.ClusterKind("GPUPH"));
    }

    [Fact]
    public void IOReport_ClockIsTheTimeWeightedActiveFrequency()
    {
        var states = new List<(string, long)> { ("IDLE", 500), ("V0P5", 250), ("V1P4", 250) };
        var (mhz, active) = IOReportMath.Frequency(states, [600, 1000]);
        Assert.Equal(800, mhz, 6);
        Assert.Equal(0.5, active, 6);
    }

    [Fact]
    public void IOReport_SeveralIdleStatesComeFirst()
    {
        var states = new List<(string, long)> { ("OFF", 100), ("DOWN", 100), ("IDLE", 200), ("P1", 0), ("P2", 600) };
        var (mhz, active) = IOReportMath.Frequency(states, [400, 1200]);
        Assert.Equal(1200, mhz, 6);
        Assert.Equal(0.6, active, 6);
    }

    [Fact]
    public void IOReport_AnIdleClusterReportsItsLowestClock()
    {
        var (mhz, active) = IOReportMath.Frequency(new List<(string, long)> { ("IDLE", 1000), ("V0", 0) }, [600, 1000]);
        Assert.Equal(600, mhz);
        Assert.Equal(0, active);
    }

    [Fact]
    public void IOReport_NothingToWeighGivesNothing()
    {
        Assert.True(double.IsNaN(IOReportMath.Frequency(new List<(string, long)>(), [600]).Mhz));
        Assert.True(double.IsNaN(IOReportMath.Frequency(new List<(string, long)> { ("V0", 5) }, []).Mhz));
    }

    [Fact]
    public void FrequencyTables_ReadHzAndKhzAndDropZeros()
    {
        var hz = Table((600_000_000, 800_000), (0, 0), (972_000_000, 900_000));
        Assert.Equal([600.0, 972.0], FrequencyTables.ParseMhz(hz));
        var khz = Table((1_020_000, 700), (4_512_000, 1000));
        Assert.Equal([1020.0, 4512.0], FrequencyTables.ParseMhz(khz));
    }

    private static byte[] Table(params (uint Frequency, uint Voltage)[] entries)
    {
        var bytes = new byte[entries.Length * 8];
        for (int i = 0; i < entries.Length; i++)
        {
            BitConverter.GetBytes(entries[i].Frequency).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(entries[i].Voltage).CopyTo(bytes, (i * 8) + 4);
        }

        return bytes;
    }

    [Fact]
    public void CpuTicks_LoadIsBusyOverAllTime()
    {
        uint[][] previous = [[100, 50, 850, 0], [0, 0, 1000, 0]];
        uint[][] current = [[200, 100, 1550, 50], [0, 0, 2000, 0]];
        var loads = CpuTickMath.Loads(previous, current, out double total);
        Assert.Equal(200 * 100.0 / 900, loads[0], 6); // user 100 + system 50 + nice 50 busy, 700 idle
        Assert.Equal(0, loads[1], 6);
        Assert.Equal(200 * 100.0 / 1900, total, 6);
    }

    [Fact]
    public void CpuTicks_SurviveACounterWrap()
    {
        uint[][] previous = [[uint.MaxValue - 9, 0, uint.MaxValue - 9, 0]];
        uint[][] current = [[10, 0, 10, 0]];
        var loads = CpuTickMath.Loads(previous, current, out double total);
        Assert.Equal(50, loads[0], 6);
        Assert.Equal(50, total, 6);
    }

    [Fact]
    public void CpuTicks_NoTimeGivesNoLoad()
    {
        uint[][] same = [[5, 5, 5, 5]];
        var loads = CpuTickMath.Loads(same, same, out double total);
        Assert.True(double.IsNaN(loads[0]));
        Assert.True(double.IsNaN(total));
    }

    [Fact]
    public void Clusters_TheBusiestOfATypeGivesItsClock()
    {
        ClusterReading[] clusters =
        [
            new("ECPU", false, 2000, 0.5),
            new("PCPU", true, 3000, 0.2),
            new("PCPU1", true, 3500, 0.8),
        ];
        Assert.Equal(3500, ClusterMath.Clock(clusters, performance: true));
        Assert.Equal(2000, ClusterMath.Clock(clusters, performance: false));
        Assert.True(double.IsNaN(ClusterMath.Clock([], performance: true)));
    }

    [Fact]
    public void Clusters_AverageWeighsCoresAndActivity()
    {
        ClusterReading[] clusters = [new("ECPU", false, 2000, 0.5), new("PCPU", true, 3000, 0.5)];
        Assert.Equal(2500, ClusterMath.Average(clusters, performanceCores: 4, efficiencyCores: 4), 6);

        // Twice the performance cores, equally busy: the average leans toward the performance clock.
        Assert.Equal((2000 * 4 + 3000 * 8) / 12.0, ClusterMath.Average(clusters, performanceCores: 8, efficiencyCores: 4), 6);
    }

    [Fact]
    public void Memory_CountsLikeActivityMonitor()
    {
        var vm = new Darwin.VmStatistics64
        {
            InternalPageCount = 300_000,
            PurgeableCount = 10_000,
            WireCount = 100_000,
            CompressorPageCount = 50_000,
            ExternalPageCount = 200_000,
        };
        const long page = 16384;
        const long total = 16L * 1024 * 1024 * 1024;
        var m = MemoryMath.FromStatistics(vm, page, total);
        Assert.Equal(290_000.0 * page, m.App);
        Assert.Equal(100_000.0 * page, m.Wired);
        Assert.Equal(50_000.0 * page, m.Compressed);
        Assert.Equal(210_000.0 * page, m.Cached);
        Assert.Equal((290_000.0 + 100_000 + 50_000) * page, m.Used);
        Assert.Equal(total - m.Used, m.Available);
        Assert.Equal(m.Used * 100 / total, m.LoadPercent, 6);
    }

    [Fact]
    public void Memory_UsedNeverExceedsThePhysicalMemory()
    {
        var vm = new Darwin.VmStatistics64 { InternalPageCount = 2_000_000, WireCount = 1_000_000 };
        var m = MemoryMath.FromStatistics(vm, 16384, 1024L * 1024 * 1024);
        Assert.Equal(1024.0 * 1024 * 1024, m.Used);
        Assert.Equal(0, m.Available);
        Assert.Equal(100, m.LoadPercent);
    }

    [Theory]
    [InlineData(1, "Normal")]
    [InlineData(2, "Warning")]
    [InlineData(4, "Critical")]
    public void Memory_PressureLevelsHaveNames(double level, string text)
    {
        Assert.Equal(text, MemoryMath.PressureText(level));
    }

    [Theory]
    [InlineData("disk3s1s1", "DISK3S1")]
    [InlineData("disk3s5", "DISK3S5")]
    [InlineData("disk4s2", "DISK4S2")]
    [InlineData("disk5", "DISK5")]
    public void Volumes_SnapshotsCountAsTheirVolume(string bsd, string key)
    {
        Assert.Equal(key, MacStorageProvider.VolumeKey(bsd));
    }

    [Theory]
    [InlineData("apfs", "APFS")]
    [InlineData("hfs", "Mac OS Extended")]
    [InlineData("exfat", "ExFAT")]
    [InlineData("zfs", "ZFS")]
    public void Volumes_FileSystemsHaveFinderNames(string fs, string name)
    {
        Assert.Equal(name, MacStorageProvider.FileSystemName(fs));
    }

    [Theory]
    [InlineData("/Applications/Safari.app/Contents/MacOS/Safari", "Safari")]
    [InlineData("/Applications/Google Chrome.app/Contents/Frameworks/Google Chrome Framework.framework/Helpers/Google Chrome Helper (Renderer).app/Contents/MacOS/Google Chrome Helper (Renderer)", "Google Chrome")]
    [InlineData("/System/Library/CoreServices/Finder.app/Contents/MacOS/Finder", "Finder")]
    public void Processes_GroupByTheirApp(string path, string app)
    {
        Assert.Equal(app, MacProcessSampler.AppName(path));
    }

    [Theory]
    [InlineData("/usr/sbin/mDNSResponder")]
    [InlineData("")]
    [InlineData((string?)null)]
    public void Processes_OutsideAppsKeepTheirName(string? path)
    {
        Assert.Null(MacProcessSampler.AppName(path));
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-8700B CPU @ 3.20GHz", "Intel Core i7-8700B")]
    [InlineData("Apple M4 Max", "Apple M4 Max")]
    [InlineData("Apple M1 (Virtual)", "Apple M1 (Virtual)")]
    public void SystemInfo_CleansProcessorNames(string brand, string name)
    {
        Assert.Equal(name, MacSystemInfo.CleanCpuName(brand));
    }

    [Theory]
    [InlineData("Macmini8,1", "Mac mini")]
    [InlineData("MacBookPro16,1", "MacBook Pro")]
    [InlineData("MacBookAir10,1", "MacBook Air")]
    [InlineData("iMac20,2", "iMac")]
    [InlineData("MacPro7,1", "Mac Pro")]
    [InlineData("Mac15,8", "Mac")]
    [InlineData("VirtualMac2,1", "Virtual Mac")]
    public void SystemInfo_NamesModelFamilies(string identifier, string family)
    {
        Assert.Equal(family, MacSystemInfo.FamilyName(identifier));
    }

    [Theory]
    [InlineData("13.6", "Ventura")]
    [InlineData("14.7.1", "Sonoma")]
    [InlineData("15.7.9", "Sequoia")]
    [InlineData("26.1", "Tahoe")]
    public void SystemInfo_NamesMacOSReleases(string version, string name)
    {
        Assert.Equal(name, MacSystemInfo.ReleaseName(version));
    }

    [Fact]
    public void SystemInfo_UnknownReleasesHaveNoName()
    {
        Assert.Null(MacSystemInfo.ReleaseName("99.0"));
        Assert.Null(MacSystemInfo.ReleaseName(null));
    }

    [Fact]
    public void Model_DisplayNameCarriesTheIdentifier()
    {
        Assert.Equal("Mac mini (Macmini8,1)", new MacModel("Macmini8,1", "Mac mini", null).DisplayName);
        Assert.Equal("Mac", new MacModel("Mac", "Mac", null).DisplayName);
    }

    [Theory]
    [InlineData("pACC MTR Temp Sensor3", "Performance cores 3")]
    [InlineData("eACC MTR Temp Sensor0", "Efficiency cores 0")]
    [InlineData("GPU MTR Temp Sensor1", "GPU 1")]
    [InlineData("ANE MTR Temp Sensor1", "Neural Engine 1")]
    [InlineData("NAND CH0 temp", "SSD")]
    [InlineData("gas gauge battery", "Battery")]
    [InlineData("PMU tdie5", "Die 5")]
    public void Hid_SensorsGetFriendlyLabels(string name, string label)
    {
        Assert.Equal(label, HidThermals.Label(name));
    }

    [Fact]
    public void Hid_CpuIsTheClusterAverage_ElseTheDieSensors()
    {
        TemperatureReading[] clusters = [new("pACC MTR Temp Sensor0", 50, "HID"), new("eACC MTR Temp Sensor0", 40, "HID"), new("GPU MTR Temp Sensor1", 30, "HID")];
        Assert.Equal(45, HidThermals.CpuTemperature(clusters));
        Assert.Equal(30, HidThermals.GpuTemperature(clusters));

        TemperatureReading[] pmu = [new("PMU tdie1", 60, "HID"), new("PMU tdie2", 62, "HID")];
        Assert.Equal(61, HidThermals.CpuTemperature(pmu));
        Assert.True(double.IsNaN(HidThermals.GpuTemperature(pmu)));
    }

    [Theory]
    [InlineData(-5, false)]
    [InlineData(0, false)]
    [InlineData(42.5, true)]
    [InlineData(150, false)]
    [InlineData(double.NaN, false)]
    public void Hid_ImplausibleReadingsAreDropped(double celsius, bool plausible)
    {
        Assert.Equal(plausible, HidThermals.IsPlausible(celsius));
    }

    [Fact]
    public void Board_TemperaturesAreGroupedByPart()
    {
        TemperatureReading[] readings =
        [
            new("pACC MTR Temp Sensor0", 50, "HID"),
            new("pACC MTR Temp Sensor1", 60, "HID"),
            new("GPU MTR Temp Sensor1", 40, "HID"),
            new("PMU tdie1", 70, "HID"),
            new("CPU proximity", 35, "SMC TC0P"),
        ];
        var groups = MacSystemProvider.TemperatureGroups(readings).ToArray();
        Assert.Equal([("CPU proximity", 35.0), ("GPU", 40.0), ("Performance cores", 55.0)], groups);
    }

    [Theory]
    [InlineData("Performance cores 3", "Performance cores")]
    [InlineData("SSD", "SSD")]
    [InlineData("Die 5", "")]
    [InlineData("Device 2", "")]
    public void Board_GroupLabelsDropTheSensorNumber(string label, string group)
    {
        Assert.Equal(group, MacSystemProvider.GroupLabel(label));
    }

    [Theory]
    [InlineData("Left fan", "left-fan")]
    [InlineData("Fan 2", "fan-2")]
    [InlineData("Performance cores", "performance-cores")]
    public void Board_SlugsAreStable(string name, string slug)
    {
        Assert.Equal(slug, MacSystemProvider.Slug(name));
    }

    [Theory]
    [InlineData(0, 1, "Fan")]
    [InlineData(0, 2, "Left fan")]
    [InlineData(1, 2, "Right fan")]
    [InlineData(2, 3, "Fan 3")]
    public void Fans_AreNamedByPosition(int index, int count, string name)
    {
        Assert.Equal(name, SmcKeys.FanName(index, count));
    }

    [Fact]
    public void Battery_AppleSiliconReportsPercentAndRawCapacity()
    {
        var p = new Dictionary<string, object?>
        {
            ["CurrentCapacity"] = 80L,
            ["MaxCapacity"] = 100L,
            ["DesignCapacity"] = 5000L,
            ["AppleRawMaxCapacity"] = 4500L,
            ["CycleCount"] = 120L,
            ["Temperature"] = 3055L,
            ["Voltage"] = 12500L,
            ["InstantAmperage"] = -1000L,
        };
        var b = BatteryReading.From(p);
        Assert.Equal(80, b.ChargePercent);
        Assert.Equal(90, b.HealthPercent, 6);
        Assert.Equal(120, b.Cycles);
        Assert.Equal(30.55, b.Celsius, 6);
        Assert.Equal(12.5, b.Watts, 6);
        Assert.Equal(4500, b.FullChargeMah);
    }

    [Fact]
    public void Battery_IntelReportsMahAndAnUnsignedCurrent()
    {
        var p = new Dictionary<string, object?>
        {
            ["CurrentCapacity"] = 3000L,
            ["MaxCapacity"] = 6000L,
            ["DesignCapacity"] = 6600L,
            ["Voltage"] = 11000L,
            ["Amperage"] = 64536L,
        };
        var b = BatteryReading.From(p);
        Assert.Equal(50, b.ChargePercent);
        Assert.Equal(6000 * 100.0 / 6600, b.HealthPercent, 6);
        Assert.Equal(11, b.Watts, 6);
        Assert.True(double.IsNaN(b.Celsius));
    }

    [Theory]
    [InlineData("AGXAcceleratorG13X", HardwareVendor.Apple)]
    [InlineData("AGXAcceleratorG16G", HardwareVendor.Apple)]
    [InlineData("AppleParavirtGPU", HardwareVendor.Apple)]
    [InlineData("AMDRadeonX6000_AMDNavi10GraphicsAccelerator", HardwareVendor.Amd)]
    [InlineData("IntelAccelerator", HardwareVendor.Intel)]
    public void Gpus_VendorComesFromTheDriver(string className, HardwareVendor vendor)
    {
        Assert.Equal(vendor, MacGpuProvider.VendorOf(className, null));
    }

    [Theory]
    [InlineData("en0", "en0")]
    [InlineData("utun3", "utun3")]
    [InlineData("bridge0", "bridge0")]
    public void Network_KeysComeFromTheInterfaceName(string bsd, string key)
    {
        Assert.Equal(key, MacNetworkProvider.AdapterKey(bsd));
    }

    [Theory]
    [InlineData(1_000_000_000, "1 Gbps")]
    [InlineData(2_500_000_000, "2.5 Gbps")]
    [InlineData(100_000_000, "100 Mbps")]
    public void Network_LinkSpeedsReadNaturally(long bits, string text)
    {
        Assert.Equal(text, MacNetworkProvider.FormatLinkSpeed(bits));
    }
}
