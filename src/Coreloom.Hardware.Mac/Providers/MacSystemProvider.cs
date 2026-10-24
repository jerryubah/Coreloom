using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Hardware.Mac.Sensors;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>
/// The Mac itself: its model and macOS version, its fans and the other temperature sensors (from the SMC and, on
/// Apple silicon, the HID temperature sensors), the whole system's power draw and, on a MacBook, the battery.
/// </summary>
public sealed class MacSystemProvider : ITelemetryProvider
{
    public const int Priority = 60;
    private readonly SystemSummary _summary;
    private readonly SensorHub _sensors;
    private readonly Dictionary<int, MetricHandle> _fans = [];
    private readonly Dictionary<string, MetricHandle> _temperatures = new(StringComparer.Ordinal);
    private MetricHandle? _systemPower;
    private MetricHandle? _batteryCharge;
    private MetricHandle? _batteryHealth;
    private MetricHandle? _batteryCycles;
    private MetricHandle? _batteryTemperature;
    private MetricHandle? _batteryPower;
    private uint _battery;
    private bool _acquired;

    public MacSystemProvider(SystemSummary summary, SensorHub sensors)
    {
        _summary = summary;
        _sensors = sensors;
    }

    public string Name => "Mac sensors";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _acquired = true;
        _sensors.Acquire();
        var model = MacSystemInfo.Model;
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var systemProps = new List<KeyValuePair<string, string>>
        {
            new("Computer name", _summary.ComputerName),
            new("Operating system", MacSystemInfo.OsDisplayName()),
            new("Version", _summary.OsVersion),
            new("Model", model.DisplayName),
            new("Chip", _summary.CpuName),
        };
        if (_summary.InstalledMemoryBytes > 0)
        {
            systemProps.Add(new("Memory", f.Bytes(_summary.InstalledMemoryBytes, 0).ToString()));
        }

        if (model.Firmware is { } firmware)
        {
            systemProps.Add(new("Firmware", firmware));
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.SystemDeviceId, MetricCategory.System, _summary.ComputerName)
        {
            Kind = "Mac",
            Vendor = "Apple",
            Source = "macOS",
            Properties = systemProps,
        });

        var sample = _sensors.Sample();
        var boardProps = new List<KeyValuePair<string, string>>
        {
            new("Model", model.Name),
            new("Model identifier", model.Identifier),
        };
        if (model.Firmware is { } fw)
        {
            boardProps.Add(new("Firmware", fw));
        }

        foreach (var fan in sample.Fans)
        {
            string slug = Slug(fan.Name);
            _fans[fan.Index] = context.Register(new MetricDescriptor($"board/fan/{slug}", fan.Name, MetricCategory.Motherboard, MetricKind.Fan)
            {
                DeviceId = MetricIds.BoardDeviceId,
                DeviceName = model.Name,
                Group = "Fans",
                SortOrder = fan.Index,
            }, Provenance.Measured, Priority, $"SMC key F{fan.Index}Ac (actual speed)");
            if (fan.MinRpm is double min && fan.MaxRpm is double max)
            {
                boardProps.Add(new($"{fan.Name} range", $"{min:0} – {max:0} RPM"));
            }
        }

        foreach (var (label, _) in TemperatureGroups(sample.Temperatures))
        {
            _temperatures[label] = context.Register(new MetricDescriptor($"board/temp/{Slug(label)}", label, MetricCategory.Motherboard, MetricKind.Temperature)
            {
                DeviceId = MetricIds.BoardDeviceId,
                DeviceName = model.Name,
                Group = "Temperatures",
            }, Provenance.Measured, Priority, MacSystemInfo.IsAppleSilicon ? "Average of the HID temperature sensors of this part" : "SMC temperature key");
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.BoardDeviceId, MetricCategory.Motherboard, model.Name)
        {
            Kind = "Mac",
            Vendor = "Apple",
            Source = "macOS",
            Properties = boardProps,
        });

        if (_sensors.HasSmc || double.IsFinite(sample.SystemPower))
        {
            _systemPower = context.Register(new MetricDescriptor(MetricIds.SystemPower, "System power", MetricCategory.System, MetricKind.Power)
            {
                DeviceId = MetricIds.SystemDeviceId,
                DeviceName = _summary.ComputerName,
                Group = "Power",
            }, Provenance.Measured, Priority, "SMC key PSTR: the whole Mac's power draw");
        }
        else
        {
            context.SetUnavailableReason(MetricIds.SystemPower, SensorHub.MissingReason);
        }

        RegisterBattery(context);
        string fans = sample.Fans.Count == 0 ? (MacSystemInfo.IsAppleSilicon ? "no fans reported" : "no fans") : $"{sample.Fans.Count} fan(s)";
        return ProviderInitResult.Running($"{model.DisplayName}; {fans}; {_temperatures.Count} temperature groups; sensors: {_sensors.Description}");
    }

    private void RegisterBattery(IProviderContext context)
    {
        _battery = IOKit.IOServiceGetMatchingService(0, IOKit.IOServiceMatching("AppleSmartBattery"));
        if (_battery == 0)
        {
            return;
        }

        var properties = IOKit.Properties(_battery);
        if (properties is null || CF.Number(properties, "DesignCapacity") is not double design || design <= 0)
        {
            IOKit.IOObjectRelease(_battery);
            _battery = 0;
            return;
        }

        MetricHandle Reg(string id, string name, MetricKind kind, string note, bool track = true) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.System, kind)
            {
                DeviceId = MetricIds.BatteryDeviceId,
                DeviceName = "Battery",
                Group = "Battery",
                TrackHistory = track,
            }, Provenance.Measured, Priority, note);

        const string source = "The battery's own gauge (AppleSmartBattery)";
        _batteryCharge = Reg(MetricIds.BatteryCharge, "Battery charge", MetricKind.Level, source);
        _batteryHealth = Reg(MetricIds.BatteryHealth, "Battery health", MetricKind.Level, "Full-charge capacity ÷ design capacity (AppleSmartBattery)", track: false);
        _batteryCycles = Reg(MetricIds.BatteryCycles, "Charge cycles", MetricKind.Count, source, track: false);
        _batteryTemperature = Reg(MetricIds.BatteryTemperature, "Battery temperature", MetricKind.Temperature, source);
        _batteryPower = Reg(MetricIds.BatteryPower, "Battery power", MetricKind.Power, "Voltage × current (charging or discharging)");

        var reading = BatteryReading.From(properties);
        var props = new List<KeyValuePair<string, string>>
        {
            new("Design capacity", $"{design:0} mAh"),
        };
        if (reading.FullChargeMah is double full)
        {
            props.Add(new("Full-charge capacity", $"{full:0} mAh"));
        }

        if (CF.Text(properties, "DeviceName") is { } device)
        {
            props.Add(new("Model", device));
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.BatteryDeviceId, MetricCategory.System, "Battery")
        {
            Kind = "Battery",
            Source = "macOS",
            Properties = props,
        });
    }

    public void Poll(IMetricWriter writer)
    {
        var sample = _sensors.Sample();
        foreach (var fan in sample.Fans)
        {
            if (_fans.TryGetValue(fan.Index, out var handle))
            {
                writer.Write(handle, fan.Rpm);
            }
        }

        foreach (var (label, value) in TemperatureGroups(sample.Temperatures))
        {
            if (_temperatures.TryGetValue(label, out var handle))
            {
                writer.Write(handle, value);
            }
        }

        if (_systemPower is not null)
        {
            writer.Write(_systemPower, sample.SystemPower);
        }

        if (_battery != 0 && _batteryCharge is not null)
        {
            var properties = IOKit.Properties(_battery);
            if (properties is not null)
            {
                var reading = BatteryReading.From(properties);
                writer.Write(_batteryCharge, reading.ChargePercent);
                writer.Write(_batteryHealth!, reading.HealthPercent);
                writer.Write(_batteryCycles!, reading.Cycles);
                writer.Write(_batteryTemperature!, reading.Celsius);
                writer.Write(_batteryPower!, reading.Watts);
            }
        }
    }

    /// <summary>The temperature sensors grouped by the part they measure, averaged ("Performance cores", "GPU", "SSD" …).</summary>
    internal static IEnumerable<(string Label, double Celsius)> TemperatureGroups(IReadOnlyList<TemperatureReading> readings)
    {
        return readings
            .Where(r => r.Source == "HID" || r.Source.StartsWith("SMC", StringComparison.Ordinal))
            .Select(r => (Label: r.Source == "HID" ? GroupLabel(HidThermals.Label(r.Name)) : r.Name, r.Celsius))
            .Where(r => r.Label.Length > 0)
            .GroupBy(r => r.Label, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Average(r => r.Celsius)));
    }

    /// <summary>"Performance cores 3" → "Performance cores"; unnamed PMU sensors are left out (they duplicate the parts).</summary>
    internal static string GroupLabel(string label)
    {
        string trimmed = label.TrimEnd().TrimEnd("0123456789".ToCharArray()).TrimEnd();
        return trimmed is "Die" or "Device" || trimmed.StartsWith("PMU", StringComparison.Ordinal) ? string.Empty : trimmed;
    }

    internal static string Slug(string name) =>
        string.Join('-', new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public void Shutdown()
    {
        if (_battery != 0)
        {
            IOKit.IOObjectRelease(_battery);
            _battery = 0;
        }

        _fans.Clear();
        _temperatures.Clear();
        _systemPower = null;
        _batteryCharge = null;
        if (_acquired)
        {
            _acquired = false;
            _sensors.Release();
        }
    }

    public void Dispose() => Shutdown();
}

/// <summary>A reading of the battery's gauge.</summary>
internal readonly record struct BatteryReading(double ChargePercent, double HealthPercent, double Cycles, double Celsius, double Watts, double? FullChargeMah)
{
    public static BatteryReading From(IReadOnlyDictionary<string, object?> p)
    {
        double current = CF.Number(p, "CurrentCapacity") ?? double.NaN;
        double max = CF.Number(p, "MaxCapacity") ?? double.NaN;
        double design = CF.Number(p, "DesignCapacity") ?? double.NaN;

        // Apple silicon reports CurrentCapacity/MaxCapacity as percentages and the mAh figures as "AppleRaw…".
        double? rawMax = CF.Number(p, "AppleRawMaxCapacity") ?? CF.Number(p, "NominalChargeCapacity");
        double fullMah = rawMax ?? max;
        double charge = max > 0 ? Math.Clamp(current * 100.0 / max, 0, 100) : double.NaN;
        double health = design > 0 && fullMah > 0 ? Math.Clamp(fullMah * 100.0 / design, 0, 150) : double.NaN;
        double cycles = CF.Number(p, "CycleCount") ?? double.NaN;
        double temperature = CF.Number(p, "Temperature") is double t ? t / 100.0 : double.NaN;
        double volts = (CF.Number(p, "Voltage") ?? double.NaN) / 1000.0;
        double amps = (CF.Number(p, "InstantAmperage") ?? CF.Number(p, "Amperage") ?? double.NaN) / 1000.0;
        if (amps > 32.768)
        {
            amps -= 65.536; // some Intel Macs report the signed current as an unsigned 16-bit number
        }

        double watts = Math.Abs(volts * amps);
        return new BatteryReading(charge, health, cycles, temperature is > 0 and < 100 ? temperature : double.NaN, double.IsFinite(watts) ? watts : double.NaN,
            fullMah > 0 && fullMah > 100 ? fullMah : null);
    }
}
