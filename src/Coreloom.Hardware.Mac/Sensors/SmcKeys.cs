using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Sensors;

/// <summary>
/// The SMC keys this Mac has, found once when the sensors open: its fans, the system's total power and, on Intel Macs,
/// the processor's and graphics' temperatures and power. Keys a Mac doesn't have are left out, so nothing is shown
/// for them.
/// </summary>
internal sealed class SmcKeys
{
    /// <summary>Intel Macs' temperature keys with what they measure (the SMC itself has no names).</summary>
    internal static readonly (string Key, string Name)[] IntelTemperatureKeys =
    [
        ("TC0D", "CPU die"),
        ("TC0E", "CPU die (filtered)"),
        ("TC0F", "CPU die (PECI)"),
        ("TCXC", "CPU PECI"),
        ("TC0P", "CPU proximity"),
        ("TC0H", "CPU heatsink"),
        ("TC1C", "CPU core 1"),
        ("TC2C", "CPU core 2"),
        ("TC3C", "CPU core 3"),
        ("TC4C", "CPU core 4"),
        ("TC5C", "CPU core 5"),
        ("TC6C", "CPU core 6"),
        ("TC7C", "CPU core 7"),
        ("TC8C", "CPU core 8"),
        ("TG0D", "GPU die"),
        ("TGDD", "GPU die (AMD)"),
        ("TG0P", "GPU proximity"),
        ("TG0H", "GPU heatsink"),
        ("TM0P", "Memory proximity"),
        ("Tm0P", "Mainboard"),
        ("TPCD", "Platform controller hub"),
        ("TH0P", "SSD proximity"),
        ("TH0a", "SSD"),
        ("TH0x", "SSD (NAND)"),
        ("TI0P", "Thunderbolt"),
        ("TB0T", "Battery"),
        ("Ts0P", "Palm rest"),
        ("TA0P", "Ambient"),
        ("TW0P", "Wi-Fi"),
        ("Th0H", "Heatpipe"),
        ("Tp0C", "Power supply"),
    ];

    private SmcKeys(int fanCount, IReadOnlyList<(string Name, string Key)> temperatures, string? systemPower, string? cpuPower, string? gpuPower, string? cpuTemp, string? gpuTemp)
    {
        FanCount = fanCount;
        Temperatures = temperatures;
        SystemPowerKey = systemPower;
        CpuPowerKey = cpuPower;
        GpuPowerKey = gpuPower;
        CpuTemperatureKey = cpuTemp;
        GpuTemperatureKey = gpuTemp;
    }

    public int FanCount { get; }

    public IReadOnlyList<(string Name, string Key)> Temperatures { get; }

    public string? SystemPowerKey { get; }

    public string? CpuPowerKey { get; }

    public string? GpuPowerKey { get; }

    public string? CpuTemperatureKey { get; }

    public string? GpuTemperatureKey { get; }

    public string Description =>
        $"{FanCount} fan{(FanCount == 1 ? string.Empty : "s")}, {Temperatures.Count} temperature keys"
        + (SystemPowerKey is null ? string.Empty : ", system power")
        + (CpuPowerKey is null ? string.Empty : ", CPU power");

    public static SmcKeys Discover(Smc.Connection smc)
    {
        int fans = smc.Number("FNum") is double n && n is >= 0 and <= 8 ? (int)n : 0;
        bool intel = !SensorHub.IsAppleSilicon;
        var temperatures = new List<(string, string)>();
        if (intel)
        {
            foreach (var (key, name) in IntelTemperatureKeys)
            {
                if (smc.Number(key) is double t && t is > 0 and < 150)
                {
                    temperatures.Add((name, key));
                }
            }
        }

        string? First(params string[] keys) => keys.FirstOrDefault(k => smc.Number(k) is double v && double.IsFinite(v) && v >= 0);
        string? cpuTemp = intel ? First("TC0D", "TC0E", "TC0F", "TCXC", "TC0P") : null;
        string? gpuTemp = intel ? First("TG0D", "TGDD", "TG0P") : null;
        return new SmcKeys(
            fans,
            temperatures,
            First("PSTR", "PDTR"),
            intel ? First("PCPT", "PCPC", "PC0C", "PC0R") : null,
            intel ? First("PCPG", "PG0R", "PG0C") : null,
            cpuTemp,
            gpuTemp);
    }

    public IEnumerable<FanReading> Fans(Smc.Connection smc)
    {
        for (int i = 0; i < FanCount; i++)
        {
            if (smc.Number($"F{i}Ac") is not double rpm || !double.IsFinite(rpm) || rpm < 0 || rpm > 20000)
            {
                continue;
            }

            double? min = smc.Number($"F{i}Mn") is double mn && mn is >= 0 and < 20000 ? mn : null;
            double? max = smc.Number($"F{i}Mx") is double mx && mx is > 0 and < 20000 ? mx : null;
            yield return new FanReading(i, FanName(i, FanCount), rpm, min, max);
        }
    }

    internal static string FanName(int index, int count) => count switch
    {
        1 => "Fan",
        2 => index == 0 ? "Left fan" : "Right fan",
        _ => $"Fan {index + 1}",
    };

    public double SystemPower(Smc.Connection smc) => Watts(smc, SystemPowerKey);

    public double CpuPower(Smc.Connection smc) => Watts(smc, CpuPowerKey);

    public double GpuPower(Smc.Connection smc) => Watts(smc, GpuPowerKey);

    public double CpuTemperature(Smc.Connection smc) => Celsius(smc, CpuTemperatureKey);

    public double GpuTemperature(Smc.Connection smc) => Celsius(smc, GpuTemperatureKey);

    private static double Watts(Smc.Connection smc, string? key) =>
        key is not null && smc.Number(key) is double w && double.IsFinite(w) && w is >= 0 and < 2000 ? w : double.NaN;

    private static double Celsius(Smc.Connection smc, string? key) =>
        key is not null && smc.Number(key) is double t && t is > 0 and < 150 ? t : double.NaN;
}
