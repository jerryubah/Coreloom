using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Native;
using Coreloom.Hardware.Providers;

namespace Coreloom.Hardware.Tests;

public class SensorMappingTests
{
    [Theory]
    [InlineData("Core (Tctl/Tdie)", 40)]
    [InlineData("Core (Tdie)", 39)]
    [InlineData("Core (Tctl)", 38)]
    [InlineData("CPU Package", 40)]
    public void CpuTemperatureCandidates_MapToCanonicalIdWithPriorities(string name, int priority)
    {
        var mapping = SensorMapping.MapCpu(name, SensorValueKind.Temperature)!;
        Assert.Equal(MetricIds.CpuTemperature, mapping.Id);
        Assert.Equal(priority, mapping.Priority);
        Assert.Equal(MetricKind.Temperature, mapping.Kind);
    }

    [Fact]
    public void CcdAndCoreSensors_AreParsed()
    {
        Assert.Equal(MetricIds.CpuCcdTemperature(2), SensorMapping.MapCpu("CCD2 (Tdie)", SensorValueKind.Temperature)!.Id);
        Assert.Equal(MetricIds.CpuCoreTemperature(5), SensorMapping.MapCpu("CPU Core #5", SensorValueKind.Temperature)!.Id);
        Assert.Equal(MetricIds.CpuCoreClock(3), SensorMapping.MapCpu("Core #3", SensorValueKind.Clock)!.Id);
        Assert.Equal(MetricIds.CpuCoreEffectiveClock(3), SensorMapping.MapCpu("Core #3 (Effective)", SensorValueKind.Clock)!.Id);
        Assert.Equal(MetricIds.CpuCoreClock(12), SensorMapping.MapCpu("CPU Core #12", SensorValueKind.Clock)!.Id);
        Assert.Equal(MetricIds.CpuClockEffectiveAverage, SensorMapping.MapCpu("Cores (Average Effective)", SensorValueKind.Clock)!.Id);
        Assert.Equal(MetricIds.CpuPower, SensorMapping.MapCpu("Package", SensorValueKind.Power)!.Id);
        Assert.Equal(MetricIds.CpuCorePower(7), SensorMapping.MapCpu("Core #7 (SMU)", SensorValueKind.Power)!.Id);
        Assert.Equal(MetricIds.CpuVoltage, SensorMapping.MapCpu("Core (SVI3 TFN)", SensorValueKind.Voltage)!.Id);
        Assert.Null(SensorMapping.MapCpu("Core #1 VID", SensorValueKind.Voltage));
        Assert.Null(SensorMapping.MapCpu("Bus Speed", SensorValueKind.Clock));
    }

    [Theory]
    [InlineData("CPU PPT", SensorValueKind.Power, "cpu/power/ppt")]
    [InlineData("SOC Power", SensorValueKind.Power, "cpu/power/soc")]
    [InlineData("SoC", SensorValueKind.Power, "cpu/power/soc")]
    [InlineData("Core Power", SensorValueKind.Power, "cpu/power/cores")]
    [InlineData("Misc Power", SensorValueKind.Power, "cpu/power/misc")]
    [InlineData("TDC", SensorValueKind.Current, "cpu/current/tdc")]
    [InlineData("EDC", SensorValueKind.Current, "cpu/current/edc")]
    [InlineData("SoC", SensorValueKind.Current, "cpu/current/soc")]
    [InlineData("SoC (SVI2 TFN)", SensorValueKind.Voltage, "cpu/voltage/soc")]
    [InlineData("VDDCR SoC", SensorValueKind.Voltage, "cpu/voltage/soc")]
    [InlineData("VDDCR", SensorValueKind.Voltage, "cpu/voltage")]
    [InlineData("Fabric", SensorValueKind.Clock, "cpu/clock/fabric")]
    [InlineData("Uncore", SensorValueKind.Clock, "cpu/clock/uncore")]
    [InlineData("Memory", SensorValueKind.Clock, "cpu/clock/memory")]
    [InlineData("SoC", SensorValueKind.Temperature, "cpu/temp/soc")]
    [InlineData("IOD Hotspot", SensorValueKind.Temperature, "cpu/temp/iod")]
    public void AmdSmuAndSviSensors_MapToCanonicalIds(string name, SensorValueKind kind, string expected)
    {
        var mapping = SensorMapping.MapCpu(name, kind)!;
        Assert.Equal(expected, mapping.Id);
        Assert.Equal(MetricCategory.Cpu, mapping.Category);
    }

    [Fact]
    public void SviCoreVoltage_WinsOverTheSmuTablesVddcr()
    {
        Assert.True(SensorMapping.MapCpu("Core (SVI2 TFN)", SensorValueKind.Voltage)!.Priority > SensorMapping.MapCpu("VDDCR", SensorValueKind.Voltage)!.Priority);
        Assert.Null(SensorMapping.MapCpu("GFX", SensorValueKind.Voltage));
        Assert.Null(SensorMapping.MapCpu("Core #1", SensorValueKind.Factor));
    }

    [Fact]
    public void CpuLoadFromLhm_HasLowerPriorityThanPdh()
    {
        var mapping = SensorMapping.MapCpu("CPU Total", SensorValueKind.Load)!;
        Assert.True(mapping.Priority < CpuProvider.Priority);
    }

    [Theory]
    [InlineData("GPU Core", SensorValueKind.Temperature, "gpu/0/temp")]
    [InlineData("GPU Hot Spot", SensorValueKind.Temperature, "gpu/0/temp/hotspot")]
    [InlineData("GPU Memory Junction", SensorValueKind.Temperature, "gpu/0/temp/memjunction")]
    [InlineData("GPU Core", SensorValueKind.Load, "gpu/0/load")]
    [InlineData("GPU Memory Controller", SensorValueKind.Load, "gpu/0/load/memctl")]
    [InlineData("GPU Memory", SensorValueKind.Load, "gpu/0/vram/load")]
    [InlineData("GPU Core", SensorValueKind.Clock, "gpu/0/clock/core")]
    [InlineData("GPU Memory", SensorValueKind.Clock, "gpu/0/clock/memory")]
    [InlineData("GPU Package", SensorValueKind.Power, "gpu/0/power")]
    [InlineData("GPU Core Voltage", SensorValueKind.Voltage, "gpu/0/voltage")]
    [InlineData("GPU Fan 2", SensorValueKind.Fan, "gpu/0/fan/2/rpm")]
    [InlineData("GPU Fan", SensorValueKind.Fan, "gpu/0/fan/1/rpm")]
    [InlineData("GPU", SensorValueKind.Fan, "gpu/0/fan/1/rpm")]
    [InlineData("GPU Fan 1", SensorValueKind.Control, "gpu/0/fan/1/pct")]
    [InlineData("GPU Memory Used", SensorValueKind.SmallData, "gpu/0/vram/used")]
    [InlineData("GPU PCIe Rx", SensorValueKind.Throughput, "gpu/0/pcie/rx")]
    public void GpuSensors_MapToCanonicalIds(string name, SensorValueKind kind, string expected)
    {
        var mapping = SensorMapping.MapGpu(0, name, kind)!;
        Assert.Equal(expected, mapping.Id);
        Assert.True(mapping.Priority < NvmlGpuProvider.Priority, "NVML must win over LibreHardwareMonitor for shared metrics.");
    }

    [Fact]
    public void UnknownGpuSensors_AreNotMapped()
    {
        Assert.Null(SensorMapping.MapGpu(0, "D3D 3D", SensorValueKind.Load));
        Assert.Null(SensorMapping.MapGpu(0, "12VHPWR Pin 1", SensorValueKind.Voltage));
    }

    [Fact]
    public void Scale_ConvertsLhmDataUnitsToBytes()
    {
        Assert.Equal(1024.0 * 1024 * 1024, SensorMapping.ScaleFor(SensorValueKind.Data));
        Assert.Equal(1024.0 * 1024, SensorMapping.ScaleFor(SensorValueKind.SmallData));
        Assert.Equal(1.0, SensorMapping.ScaleFor(SensorValueKind.Temperature));
    }

    [Fact]
    public void StorageAndBoardMapping()
    {
        Assert.Equal(MetricIds.DiskTemperature(1), SensorMapping.MapStorage(1, "Composite Temperature", SensorValueKind.Temperature)!.Id);
        Assert.Equal(MetricIds.DiskLifeUsed(1), SensorMapping.MapStorage(1, "Percentage Used", SensorValueKind.Level)!.Id);
        Assert.Equal("board/fan/cpu-fan", SensorMapping.MapBoard("CPU Fan", SensorValueKind.Fan)!.Id);
        Assert.Equal("board/temp/temperature-3", SensorMapping.MapBoard("Temperature #3", SensorValueKind.Temperature)!.Id);
        Assert.Null(SensorMapping.MapBoard("Vcore", SensorValueKind.Voltage));
    }
}

public class GpuDirectoryTests
{
    [Fact]
    public void SameGpuFromTwoLibraries_SharesSlot_IntegratedGpuGetsItsOwn()
    {
        var gpus = new GpuDirectory();
        int nvml = gpus.Register("nvml:0", "NVIDIA", "NVIDIA GeForce RTX 5090", false);
        int lhmAmd = gpus.Register("lhm:/gpu-amd/0", "AMD", "AMD Radeon(TM) Graphics", GpuDirectory.LooksIntegrated("AMD", "AMD Radeon(TM) Graphics"));
        int lhmNvidia = gpus.Register("lhm:/gpu-nvidia/0", "NVIDIA", "NVIDIA GeForce RTX 5090", false);

        Assert.Equal(0, nvml);
        Assert.Equal(nvml, lhmNvidia);
        Assert.Equal(1, lhmAmd);
        Assert.Equal(0, gpus.PrimarySlot);
        Assert.Equal(lhmNvidia, gpus.Register("lhm:/gpu-nvidia/0", "NVIDIA", "NVIDIA GeForce RTX 5090", false));
    }

    [Fact]
    public void TwoIdenticalCards_GetDistinctSlots()
    {
        var gpus = new GpuDirectory();
        int a = gpus.Register("nvml:0", "NVIDIA", "NVIDIA GeForce RTX 4090", false);
        int b = gpus.Register("nvml:1", "NVIDIA", "NVIDIA GeForce RTX 4090", false);
        int c = gpus.Register("lhm:/gpu-nvidia/0", "NVIDIA", "NVIDIA GeForce RTX 4090", false);
        int d = gpus.Register("lhm:/gpu-nvidia/1", "NVIDIA", "NVIDIA GeForce RTX 4090", false);
        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
        Assert.Equal(b, d);
    }

    [Fact]
    public void PrimarySlot_PrefersDiscreteGpu()
    {
        var gpus = new GpuDirectory();
        gpus.Register("lhm:/gpu-amd/0", "AMD", "AMD Radeon(TM) Graphics", true);
        gpus.Register("lhm:/gpu-nvidia/0", "NVIDIA", "NVIDIA GeForce RTX 5090", false);
        Assert.Equal(1, gpus.PrimarySlot);
    }

    [Theory]
    [InlineData("AMD", "AMD Radeon(TM) Graphics", true)]
    [InlineData("AMD", "AMD Radeon 780M", true)]
    [InlineData("AMD", "AMD Radeon RX 9070 XT", false)]
    [InlineData("Intel", "Intel(R) UHD Graphics 770", true)]
    [InlineData("Intel", "Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("NVIDIA", "NVIDIA GeForce RTX 5090", false)]
    public void LooksIntegrated(string vendor, string name, bool expected)
    {
        Assert.Equal(expected, GpuDirectory.LooksIntegrated(vendor, name));
    }
}

public class ParsingTests
{
    [Fact]
    public void ProcessorInformationInstances_AreOrderedByGroupThenIndex()
    {
        var instances = new[] { "0,10", "0,_Total", "0,2", "1,0", "_Total", "0,0" }
            .Select(n => new KeyValuePair<string, double>(n, 0));
        var map = CpuProvider.BuildInstanceMap(instances);
        Assert.Equal(4, map.Count);
        Assert.Equal(0, map["0,0"]);
        Assert.Equal(1, map["0,2"]);
        Assert.Equal(2, map["0,10"]);
        Assert.Equal(3, map["1,0"]);
    }

    [Theory]
    [InlineData("0 C:", true, 0)]
    [InlineData("1 D: E:", true, 1)]
    [InlineData("12", true, 12)]
    [InlineData("_Total", false, 0)]
    public void PhysicalDiskInstance_Parsing(string instance, bool ok, int number)
    {
        Assert.Equal(ok, StorageProvider.TryParseDiskNumber(instance, out int parsed));
        if (ok)
        {
            Assert.Equal(number, parsed);
        }
    }

    [Theory]
    [InlineData("", "Samsung SSD 990 PRO 2TB", "Samsung SSD 990 PRO 2TB")]
    [InlineData("NVMe", "Samsung SSD 990 PRO 2TB", "Samsung SSD 990 PRO 2TB")]
    [InlineData("WDC", "WD_BLACK SN850X 2000GB", "WDC WD_BLACK SN850X 2000GB")]
    [InlineData("Samsung", "Samsung Portable SSD T7", "Samsung Portable SSD T7")]
    [InlineData("", "", "Disk 3")]
    public void DiskModel_Composition(string vendor, string product, string expected)
    {
        Assert.Equal(expected, StorageInterop.ComposeModel(vendor, product, 3));
    }

    [Fact]
    public void BusTypes()
    {
        Assert.Equal("NVMe", StorageInterop.BusTypeName(17));
        Assert.Equal("SATA", StorageInterop.BusTypeName(11));
        Assert.Equal("USB", StorageInterop.BusTypeName(7));
        Assert.Equal("Unknown", StorageInterop.BusTypeName(99));
    }

    [Fact]
    public void NetworkHelpers()
    {
        Assert.Equal("4a1b2c3d00001111", NetworkProvider.AdapterKey("{4A1B2C3D-0000-1111}"));
        Assert.Equal("2.5 Gbps", NetworkProvider.FormatLinkSpeed(2_500_000_000));
        Assert.Equal("100 Mbps", NetworkProvider.FormatLinkSpeed(100_000_000));
    }

    [Fact]
    public void DiskDirectory_MatchesByModelAndSkipsClaimed()
    {
        var disks = new DiskDirectory();
        disks.Set([
            new DiskDirectory.DiskEntry(0, "Samsung SSD 990 PRO 2TB", null, "NVMe", true, 2_000_000_000_000),
            new DiskDirectory.DiskEntry(1, "Samsung SSD 990 PRO 2TB", null, "NVMe", true, 2_000_000_000_000),
        ]);
        var claimed = new HashSet<int>();
        int? first = disks.MatchByName("Samsung SSD 990 PRO 2TB", claimed);
        claimed.Add(first!.Value);
        int? second = disks.MatchByName("Samsung SSD 990 PRO 2TB", claimed);
        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Null(disks.MatchByName("Crucial T705", claimed));
    }
}
