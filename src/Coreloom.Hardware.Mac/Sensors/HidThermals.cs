using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Sensors;

/// <summary>
/// The temperature sensors Apple silicon publishes through the HID event system ("pACC MTR Temp Sensor0", "GPU MTR Temp
/// Sensor1", "NAND CH0 temp", "gas gauge battery" …). The same sensors macOS's own thermal management reads.
/// </summary>
internal sealed class HidThermals : IDisposable
{
    private const int UsagePageVendor = 0xff00;
    private const int UsageTemperature = 5;
    private readonly nint _client;
    private readonly List<(nint Service, string Name)> _services;

    private HidThermals(nint client, List<(nint, string)> services)
    {
        _client = client;
        _services = services;
    }

    public int Count => _services.Count;

    public static HidThermals? Open()
    {
        nint client = Hid.IOHIDEventSystemClientCreate(0);
        if (client == 0)
        {
            return null;
        }

        nint matching = CF.MutableDictionary();
        nint page = CF.Number(UsagePageVendor);
        nint usage = CF.Number(UsageTemperature);
        CF.Set(matching, "PrimaryUsagePage", page);
        CF.Set(matching, "PrimaryUsage", usage);
        CF.CFRelease(page);
        CF.CFRelease(usage);
        Hid.IOHIDEventSystemClientSetMatching(client, matching);
        CF.CFRelease(matching);

        var services = new List<(nint, string)>();
        nint array = Hid.IOHIDEventSystemClientCopyServices(client);
        if (array != 0)
        {
            nint productKey = CF.String("Product");
            try
            {
                int count = (int)CF.CFArrayGetCount(array);
                for (int i = 0; i < count; i++)
                {
                    nint service = CF.CFArrayGetValueAtIndex(array, i);
                    nint product = Hid.IOHIDServiceClientCopyProperty(service, productKey);
                    string? name = CF.ToStringValue(product);
                    if (product != 0)
                    {
                        CF.CFRelease(product);
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        CF.CFRetain(service);
                        services.Add((service, name.Trim()));
                    }
                }
            }
            finally
            {
                CF.CFRelease(productKey);
                CF.CFRelease(array);
            }
        }

        if (services.Count == 0)
        {
            CF.CFRelease(client);
            return null;
        }

        return new HidThermals(client, services);
    }

    /// <summary>Every sensor that reports a plausible temperature right now.</summary>
    public IReadOnlyList<TemperatureReading> Read()
    {
        var result = new List<TemperatureReading>(_services.Count);
        int field = Hid.FieldBase(Hid.EventTypeTemperature);
        foreach (var (service, name) in _services)
        {
            nint hidEvent = Hid.IOHIDServiceClientCopyEvent(service, Hid.EventTypeTemperature, 0, 0);
            if (hidEvent == 0)
            {
                continue;
            }

            double value = Hid.IOHIDEventGetFloatValue(hidEvent, field);
            CF.CFRelease(hidEvent);
            if (IsPlausible(value))
            {
                result.Add(new TemperatureReading(name, value, "HID"));
            }
        }

        return result;
    }

    internal static bool IsPlausible(double celsius) => double.IsFinite(celsius) && celsius is > 0 and < 150;

    /// <summary>The processor clusters' die temperature: the average of the cluster sensors, else of the PMU's die sensors.</summary>
    public static double CpuTemperature(IReadOnlyList<TemperatureReading> readings)
    {
        double clusters = Average(readings, r => IsClusterSensor(r.Name));
        return double.IsNaN(clusters) ? Average(readings, r => r.Name.Contains("tdie", StringComparison.OrdinalIgnoreCase)) : clusters;
    }

    public static double GpuTemperature(IReadOnlyList<TemperatureReading> readings) =>
        Average(readings, r => r.Name.StartsWith("GPU", StringComparison.OrdinalIgnoreCase) && r.Name.Contains("MTR", StringComparison.OrdinalIgnoreCase));

    internal static bool IsClusterSensor(string name) =>
        name.Contains("MTR", StringComparison.OrdinalIgnoreCase)
        && (name.StartsWith("pACC", StringComparison.OrdinalIgnoreCase) || name.StartsWith("eACC", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("PACC", StringComparison.Ordinal) || name.StartsWith("EACC", StringComparison.Ordinal)
            || name.StartsWith("CPU", StringComparison.OrdinalIgnoreCase));

    public static double Average(IReadOnlyList<TemperatureReading> readings, Func<TemperatureReading, bool> predicate)
    {
        double sum = 0;
        int count = 0;
        foreach (var reading in readings)
        {
            if (predicate(reading))
            {
                sum += reading.Celsius;
                count++;
            }
        }

        return count == 0 ? double.NaN : sum / count;
    }

    /// <summary>A friendlier label for a HID sensor ("pACC MTR Temp Sensor3" → "Performance cluster 3").</summary>
    internal static string Label(string name)
    {
        string digits = new(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        string suffix = digits.Length > 0 ? " " + digits : string.Empty;
        if (name.StartsWith("pACC", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PACC", StringComparison.Ordinal))
        {
            return "Performance cores" + suffix;
        }

        if (name.StartsWith("eACC", StringComparison.OrdinalIgnoreCase))
        {
            return "Efficiency cores" + suffix;
        }

        if (name.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return "GPU" + suffix;
        }

        if (name.StartsWith("ANE", StringComparison.OrdinalIgnoreCase))
        {
            return "Neural Engine" + suffix;
        }

        if (name.StartsWith("SOC", StringComparison.OrdinalIgnoreCase))
        {
            return "SoC" + suffix;
        }

        if (name.StartsWith("ISP", StringComparison.OrdinalIgnoreCase))
        {
            return "Image processor" + suffix;
        }

        if (name.StartsWith("NAND", StringComparison.OrdinalIgnoreCase))
        {
            return "SSD" + suffix;
        }

        if (name.Contains("battery", StringComparison.OrdinalIgnoreCase))
        {
            return "Battery" + suffix;
        }

        if (name.Contains("tdie", StringComparison.OrdinalIgnoreCase))
        {
            return "Die" + suffix;
        }

        if (name.Contains("tdev", StringComparison.OrdinalIgnoreCase))
        {
            return "Device" + suffix;
        }

        return name;
    }

    public void Dispose()
    {
        foreach (var (service, _) in _services)
        {
            CF.CFRelease(service);
        }

        _services.Clear();
        CF.CFRelease(_client);
    }
}
