using System.Globalization;

namespace Coreloom.Core.Telemetry;

/// <summary>
/// Canonical metric identifiers. Several providers may report the same canonical id
/// (e.g. GPU temperature from NVML and from LibreHardwareMonitor); the highest priority wins.
/// </summary>
public static class MetricIds
{
    // ---- CPU -------------------------------------------------------------------------------
    public const string CpuLoad = "cpu/load";
    public const string CpuTemperature = "cpu/temp";
    public const string CpuPower = "cpu/power";
    public const string CpuVoltage = "cpu/voltage";
    public const string CpuClockAverage = "cpu/clock/avg";
    public const string CpuClockEffectiveAverage = "cpu/clock/eff-avg";
    /// <summary>Task-Manager style frequency: % Processor Performance × nominal frequency. Derived.</summary>
    public const string CpuClockDerived = "cpu/clock/derived";
    public const string CpuLoadMaxThread = "cpu/load/max-thread";

    /// <summary>% Processor Performance: how fast the processor runs relative to its base clock (over 100 % while boosting).</summary>
    public const string CpuPerformance = "cpu/performance";

    /// <summary>% Performance Limit: the performance the platform currently allows, relative to base; under 100 % means held back.</summary>
    public const string CpuPerformanceLimit = "cpu/perf-limit";

    // AMD Ryzen readings from the SMU and SVI telemetry (LibreHardwareMonitor; model dependent).
    public const string CpuPpt = "cpu/power/ppt";
    public const string CpuCoresPower = "cpu/power/cores";
    public const string CpuSocPower = "cpu/power/soc";
    public const string CpuMiscPower = "cpu/power/misc";
    public const string CpuTdc = "cpu/current/tdc";
    public const string CpuEdc = "cpu/current/edc";
    public const string CpuSocCurrent = "cpu/current/soc";
    public const string CpuSocVoltage = "cpu/voltage/soc";
    public const string CpuFabricClock = "cpu/clock/fabric";
    public const string CpuUncoreClock = "cpu/clock/uncore";
    public const string CpuMemoryClock = "cpu/clock/memory";
    public const string CpuSocTemperature = "cpu/temp/soc";
    public const string CpuIodTemperature = "cpu/temp/iod";

    public static string CpuThreadLoad(int logicalIndex) => $"cpu/thread/{Inv(logicalIndex)}/load";

    public static string CpuCoreClock(int coreNumber) => $"cpu/core/{Inv(coreNumber)}/clock";

    public static string CpuCoreEffectiveClock(int coreNumber) => $"cpu/core/{Inv(coreNumber)}/clock-eff";

    public static string CpuCoreTemperature(int coreNumber) => $"cpu/core/{Inv(coreNumber)}/temp";

    public static string CpuCorePower(int coreNumber) => $"cpu/core/{Inv(coreNumber)}/power";

    public static string CpuCcdTemperature(int ccdNumber) => $"cpu/ccd/{Inv(ccdNumber)}/temp";

    // ---- GPU (slot = canonical GPU index assigned by the GPU directory) --------------------
    public static string GpuLoad(int slot) => $"gpu/{Inv(slot)}/load";

    public static string GpuMemoryControllerLoad(int slot) => $"gpu/{Inv(slot)}/load/memctl";

    public static string GpuVideoEngineLoad(int slot) => $"gpu/{Inv(slot)}/load/video";

    public static string GpuEncoderLoad(int slot) => $"gpu/{Inv(slot)}/load/encoder";

    public static string GpuDecoderLoad(int slot) => $"gpu/{Inv(slot)}/load/decoder";

    public static string GpuBusLoad(int slot) => $"gpu/{Inv(slot)}/load/bus";

    public static string GpuTemperature(int slot) => $"gpu/{Inv(slot)}/temp";

    public static string GpuHotSpotTemperature(int slot) => $"gpu/{Inv(slot)}/temp/hotspot";

    public static string GpuMemoryJunctionTemperature(int slot) => $"gpu/{Inv(slot)}/temp/memjunction";

    /// <summary>Driver-reported temperature at which the GPU starts slowing down (static per device).</summary>
    public static string GpuSlowdownTemperature(int slot) => $"gpu/{Inv(slot)}/temp/slowdown";

    public static string GpuCoreClock(int slot) => $"gpu/{Inv(slot)}/clock/core";

    public static string GpuMemoryClock(int slot) => $"gpu/{Inv(slot)}/clock/memory";

    public static string GpuShaderClock(int slot) => $"gpu/{Inv(slot)}/clock/shader";

    public static string GpuVideoClock(int slot) => $"gpu/{Inv(slot)}/clock/video";

    public static string GpuMemoryUsed(int slot) => $"gpu/{Inv(slot)}/vram/used";

    public static string GpuMemoryTotal(int slot) => $"gpu/{Inv(slot)}/vram/total";

    public static string GpuMemoryLoad(int slot) => $"gpu/{Inv(slot)}/vram/load";

    public static string GpuPower(int slot) => $"gpu/{Inv(slot)}/power";

    public static string GpuPowerLimit(int slot) => $"gpu/{Inv(slot)}/power/limit";

    public static string GpuVoltage(int slot) => $"gpu/{Inv(slot)}/voltage";

    public static string GpuPerformanceState(int slot) => $"gpu/{Inv(slot)}/pstate";

    public static string GpuClockEventReasons(int slot) => $"gpu/{Inv(slot)}/clock-reasons";

    public static string GpuFanRpm(int slot, int fanNumber) => $"gpu/{Inv(slot)}/fan/{Inv(fanNumber)}/rpm";

    public static string GpuFanPercent(int slot, int fanNumber) => $"gpu/{Inv(slot)}/fan/{Inv(fanNumber)}/pct";

    public static string GpuPcieRx(int slot) => $"gpu/{Inv(slot)}/pcie/rx";

    public static string GpuPcieTx(int slot) => $"gpu/{Inv(slot)}/pcie/tx";

    public static string GpuPcieGeneration(int slot) => $"gpu/{Inv(slot)}/pcie/gen";

    public static string GpuPcieWidth(int slot) => $"gpu/{Inv(slot)}/pcie/width";

    public static string GpuDeviceId(int slot) => $"gpu/{Inv(slot)}";

    // ---- Memory ------------------------------------------------------------------------------
    public const string MemoryTotal = "mem/total";
    public const string MemoryUsed = "mem/used";
    public const string MemoryAvailable = "mem/available";
    public const string MemoryLoad = "mem/load";
    public const string MemoryCommitUsed = "mem/commit/used";
    public const string MemoryCommitLimit = "mem/commit/limit";
    public const string MemoryCache = "mem/cache";

    // ---- Storage -----------------------------------------------------------------------------
    public const string DiskTotalRead = "disk/total/read";
    public const string DiskTotalWrite = "disk/total/write";

    public static string DiskRead(int diskNumber) => $"disk/{Inv(diskNumber)}/read";

    public static string DiskWrite(int diskNumber) => $"disk/{Inv(diskNumber)}/write";

    public static string DiskActivity(int diskNumber) => $"disk/{Inv(diskNumber)}/active";

    public static string DiskTemperature(int diskNumber) => $"disk/{Inv(diskNumber)}/temp";

    public static string DiskLifeUsed(int diskNumber) => $"disk/{Inv(diskNumber)}/health/used";

    public static string DiskAvailableSpare(int diskNumber) => $"disk/{Inv(diskNumber)}/health/spare";

    public static string DiskDeviceId(int diskNumber) => $"disk/{Inv(diskNumber)}";

    public static string VolumeUsed(string letter) => $"vol/{Normalize(letter)}/used";

    public static string VolumeFree(string letter) => $"vol/{Normalize(letter)}/free";

    public static string VolumeTotal(string letter) => $"vol/{Normalize(letter)}/total";

    public static string VolumeLoad(string letter) => $"vol/{Normalize(letter)}/load";

    // ---- Network -----------------------------------------------------------------------------
    public const string NetworkActiveDown = "net/active/down";
    public const string NetworkActiveUp = "net/active/up";
    public const string NetworkActiveReceivedSession = "net/active/rx-session";
    public const string NetworkActiveSentSession = "net/active/tx-session";

    public static string NetworkDown(string adapterKey) => $"net/{adapterKey}/down";

    public static string NetworkUp(string adapterKey) => $"net/{adapterKey}/up";

    public static string NetworkReceivedSession(string adapterKey) => $"net/{adapterKey}/rx-session";

    public static string NetworkSentSession(string adapterKey) => $"net/{adapterKey}/tx-session";

    public static string NetworkDeviceId(string adapterKey) => $"net/{adapterKey}";

    // ---- System / application ----------------------------------------------------------------
    public const string SystemUptime = "sys/uptime";
    public const string AppCpu = "app/cpu";
    public const string AppWorkingSet = "app/memory";
    public const string AppPrivateBytes = "app/private";
    public const string AppManagedBytes = "app/managed";
    public const string AppHandles = "app/handles";

    // ---- Device ids --------------------------------------------------------------------------
    public const string CpuDeviceId = "cpu";
    public const string MemoryDeviceId = "memory";
    public const string BoardDeviceId = "board";
    public const string SystemDeviceId = "system";

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Normalize(string letter) => letter.TrimEnd(':', '\\', '/').ToUpperInvariant();
}
