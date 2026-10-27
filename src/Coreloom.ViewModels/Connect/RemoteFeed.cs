using System.Globalization;
using System.Text.Json;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Connect;

/// <summary>
/// What a paired phone sees: the computer's readings as one JSON object, formatted the way Coreloom shows them (units,
/// colors by level, gauge bands, the Overview's cards in the user's order and the same secondary lines). Built only while
/// a phone is watching, from the same frames the window shows, and kept current while the window is hidden.
/// </summary>
/// <summary>An update the computer has found, for the phone's notice.</summary>
/// <param name="Version">"1.8.1".</param>
/// <param name="Ready">Downloaded and verified: it installs with one click (or by itself) on the computer.</param>
public sealed record RemoteUpdate(string Version, bool Ready);

public sealed class RemoteFeed
{
    /// <summary>Readings whose recent history a phone draws as charts.</summary>
    public static IReadOnlyList<string> ChartIds(int gpuSlot) =>
    [
        MetricIds.CpuLoad, MetricIds.GpuLoad(gpuSlot), MetricIds.CpuTemperature, MetricIds.GpuTemperature(gpuSlot),
        MetricIds.MemoryLoad, MetricIds.GpuPower(gpuSlot), MetricIds.CpuPower,
        MetricIds.NetworkActiveDown, MetricIds.NetworkActiveUp, MetricIds.DiskTotalRead, MetricIds.DiskTotalWrite,
    ];

    private static readonly TimeSpan HistorySpan = TimeSpan.FromMinutes(2);
    private const int HistoryPoints = 120;

    private readonly PageContext _context;
    private SystemSpecsInfo? _info;
    private int _deviceVersion = -1;
    private int _gpuSlot = -1;
    private HistoryPoint[] _buffer = new HistoryPoint[512];

    public RemoteFeed(PageContext context)
    {
        _context = context;
    }

    /// <summary>The update the computer has found, if any (sent so the phone can say so).</summary>
    public Func<RemoteUpdate?>? UpdateNotice { get; set; }

    private bool IsMac => _context.IsMac;

    private UnitFormatter F => _context.Settings.Formatter;

    /// <summary>The readings of one frame as UTF-8 JSON.</summary>
    public byte[] Build(TelemetryFrame frame)
    {
        int slot = _context.GpuSlot;
        var devices = _context.Monitor.Engine.Devices;
        if (_info is null || devices.Version != _deviceVersion || slot != _gpuSlot)
        {
            _deviceVersion = devices.Version;
            _gpuSlot = slot;
            _info = SystemSpecs.Describe(_context.Monitor, slot, IsMac, F);
        }

        using var stream = new MemoryStream(8192);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteNumber("t", frame.Timestamp);
            w.WriteString("time", frame.LocalTime.ToString("o", CultureInfo.InvariantCulture));
            w.WriteNumber("interval", (int)_context.Monitor.Engine.EffectiveInterval.TotalMilliseconds);
            w.WriteString("accent", AccentPalette.IsValidHex(_context.Settings.Current.Appearance.Accent) ? _context.Settings.Current.Appearance.Accent : AccentPalette.Default);
            WritePc(w, frame);
            WriteHealth(w);
            WriteOverview(w, frame, slot);
            WriteGpu(w, frame, slot);
            WriteCpu(w, frame);
            WriteMemory(w, frame);
            WriteStorage(w, frame);
            WriteNetwork(w, frame);
            WriteBattery(w, frame);
            if (UpdateNotice?.Invoke() is { } update)
            {
                w.WritePropertyName("update");
                w.WriteStartObject();
                w.WriteString("version", update.Version);
                w.WriteBoolean("ready", update.Ready);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The last two minutes of the charted readings: {"now": t, "series": {id: [[t, v], …]}}, or null.</summary>
    public byte[]? History()
    {
        var history = _context.Monitor.Engine.History;
        var latest = _context.Monitor.Engine.LatestFrame;
        if (latest.Sequence == 0)
        {
            return null;
        }

        long now = latest.Timestamp;
        long from = now - (long)HistorySpan.TotalMilliseconds;
        using var stream = new MemoryStream(16384);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("now", now);
            w.WritePropertyName("series");
            w.WriteStartObject();
            foreach (string id in ChartIds(_context.GpuSlot))
            {
                if (history.Get(id) is not { } series)
                {
                    continue;
                }

                int count = series.CopyRange(from, now, ref _buffer);
                if (count == 0)
                {
                    continue;
                }

                w.WritePropertyName(id);
                w.WriteStartArray();
                int step = Math.Max(1, (int)Math.Ceiling(count / (double)HistoryPoints));
                for (int i = 0; i < count; i += step)
                {
                    var point = _buffer[i];
                    w.WriteStartArray();
                    w.WriteNumberValue(point.T);
                    w.WriteNumberValue(Math.Round(point.V, 2));
                    w.WriteEndArray();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    // ---- Sections ---------------------------------------------------------------------------------------------------

    private void WritePc(Utf8JsonWriter w, TelemetryFrame frame)
    {
        var info = _info!;
        var summary = _context.Monitor.Summary;
        w.WritePropertyName("pc");
        w.WriteStartObject();
        w.WriteString("name", summary.ComputerName);
        w.WriteString("title", info.Title);
        w.WriteString("subtitle", info.Subtitle);
        w.WriteString("platform", IsMac ? "macos" : "windows");
        w.WriteString("kind", IsMac ? MacKind(info.Title) : "pc");
        w.WritePropertyName("specs");
        w.WriteStartArray();
        string? storage = SystemSpecs.StorageLine(info, frame, _context.Monitor.Engine.Devices, IsMac, F);
        foreach (var item in info.Left.Concat(storage is null ? [] : [new KeyValueItem("Storage", storage)]).Concat(info.Right))
        {
            w.WriteStartArray();
            w.WriteStringValue(item.Key);
            w.WriteStringValue(item.Value);
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>The Mac's shape for the phone's picture: "macbook", "imac", "macmini", "macstudio", "macpro".</summary>
    internal static string MacKind(string title) =>
        title.Contains("MacBook", StringComparison.OrdinalIgnoreCase) ? "macbook"
        : title.Contains("iMac", StringComparison.OrdinalIgnoreCase) ? "imac"
        : title.Contains("Studio", StringComparison.OrdinalIgnoreCase) ? "macstudio"
        : title.Contains("Mac Pro", StringComparison.OrdinalIgnoreCase) ? "macpro"
        : "macmini";

    private void WriteHealth(Utf8JsonWriter w)
    {
        var health = _context.Health;
        w.WritePropertyName("health");
        w.WriteStartObject();
        w.WriteString("key", health.OverallKey);
        w.WriteString("text", health.OverallText);
        w.WriteString("summary", health.OverallSummary);
        w.WritePropertyName("parts");
        w.WriteStartArray();
        foreach (var part in health.Parts)
        {
            w.WriteStartObject();
            w.WriteString("name", part.Name);
            w.WriteString("icon", IconName(part.IconKey));
            w.WriteString("key", part.StatusKey);
            w.WriteString("text", part.StatusText);
            w.WriteString("summary", part.Summary);
            w.WritePropertyName("checks");
            w.WriteStartArray();
            foreach (var check in part.Checks)
            {
                w.WriteStartObject();
                w.WriteString("state", check.StateKey);
                w.WriteString("title", check.Title);
                w.WriteString("detail", check.Detail);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteOverview(Utf8JsonWriter w, TelemetryFrame frame, int slot)
    {
        var settings = _context.Settings.Current;
        var cpuTemp = AlertThresholds(settings, "cpu-temp", Thresholds.CpuTemperature, 10);
        var gpuTemp = AlertThresholds(settings, "gpu-temp", Thresholds.GpuTemperature, 8);
        var f = F;

        string? hotSpot = Formatted(frame, MetricIds.GpuHotSpotTemperature(slot), ValueStyle.Temperature) is { } hs ? $"Hot spot {hs.Number}{hs.Unit}" : null;
        string? busiest = Formatted(frame, MetricIds.CpuLoadMaxThread, ValueStyle.Percent) is { } bt ? $"{(IsMac ? "Busiest core" : "Busiest thread")} {bt.Number}{bt.Unit}" : null;
        string? ccd = frame.TryGetValue(MetricIds.CpuCcdTemperature(1), out double ccd1)
            ? $"CCD1 {f.Temperature(ccd1).Number}° · CCD2 {(frame.TryGetValue(MetricIds.CpuCcdTemperature(2), out double ccd2) ? f.Temperature(ccd2).Number + "°" : "—")}"
            : null;
        string? memory = frame.TryGetValue(MetricIds.MemoryUsed, out double used) && frame.TryGetValue(MetricIds.MemoryTotal, out double total)
            ? $"{f.MemoryBytesAs(used, f.MemoryUnitIndex(total), 1).Number} of {f.MemoryBytes(total, 1)}"
            : null;
        double powerMax = 600;
        var powerThresholds = (Thresholds?)null;
        string? powerDetail = null;
        if (frame.TryGetValue(MetricIds.GpuPowerLimit(slot), out double limit) && limit > 0)
        {
            powerMax = limit;
            powerThresholds = Thresholds.ForLimit(limit);
            var l = f.Power(limit);
            powerDetail = $"of {l.Number} {l.Unit} limit";
        }

        var cards = new Dictionary<string, (string Title, string Icon, Def Def, string? Detail)>(StringComparer.Ordinal)
        {
            [DashboardCatalog.GpuLoad] = ("GPU", "gpu", new Def(MetricIds.GpuLoad(slot), "GPU utilization", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load), null),
            [DashboardCatalog.GpuTemperature] = ("GPU temperature", "temperature", new Def(MetricIds.GpuTemperature(slot), "GPU temperature", ValueStyle.Temperature, ColorKeys.Gpu, gpuTemp), hotSpot),
            [DashboardCatalog.CpuLoad] = ("CPU", "cpu", new Def(MetricIds.CpuLoad, "CPU utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load), busiest),
            [DashboardCatalog.CpuTemperature] = ("CPU temperature", "temperature", new Def(MetricIds.CpuTemperature, "CPU temperature", ValueStyle.Temperature, ColorKeys.Cpu, cpuTemp), ccd),
            [DashboardCatalog.Memory] = ("Memory", "memory", new Def(MetricIds.MemoryLoad, "Memory utilization", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory), memory),
            [DashboardCatalog.GpuPower] = ("GPU power", "power", new Def(MetricIds.GpuPower(slot), "GPU power draw", ValueStyle.Power, ColorKeys.Power, powerThresholds, powerMax), powerDetail),
        };

        w.WritePropertyName("overview");
        w.WriteStartObject();
        w.WritePropertyName("cards");
        w.WriteStartArray();
        var order = settings.Dashboard.Cards.Where(c => c.Visible && cards.ContainsKey(c.Id)).Select(c => c.Id).ToList();
        if (order.Count == 0)
        {
            order = [.. cards.Keys];
        }

        foreach (string id in order)
        {
            var (title, icon, def, detail) = cards[id];
            w.WriteStartObject();
            w.WriteString("card", id);
            w.WriteString("title", title);
            w.WriteString("icon", icon);
            w.WritePropertyName("metric");
            WriteMetric(w, frame, def, detail, always: true);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteGpu(Utf8JsonWriter w, TelemetryFrame frame, int slot)
    {
        var devices = _context.Monitor.Engine.Devices;
        string name = devices.Get(MetricIds.GpuDeviceId(slot))?.Name ?? _context.Monitor.Gpus().FirstOrDefault(g => g.Slot == slot).Name ?? "Graphics";
        double powerMax = IsMac ? 60 : 600;
        Thresholds? powerThresholds = null;
        string? powerDetail = null;
        if (frame.TryGetValue(MetricIds.GpuPowerLimit(slot), out double limit) && limit > 0)
        {
            powerMax = limit;
            powerThresholds = Thresholds.ForLimit(limit);
            var l = F.Power(limit);
            powerDetail = $"of {l.Number} {l.Unit} limit";
        }

        string? vramDetail = frame.TryGetValue(MetricIds.GpuMemoryUsed(slot), out double vramUsed) && frame.TryGetValue(MetricIds.GpuMemoryTotal(slot), out double vramTotal) && vramTotal > 0
            ? $"{F.MemoryBytesAs(vramUsed, F.MemoryUnitIndex(vramTotal), 1).Number} of {F.MemoryBytes(vramTotal, 1)}"
            : null;

        w.WritePropertyName("gpu");
        w.WriteStartObject();
        w.WriteString("name", PageViewModel.ShortGpuName(name));
        w.WritePropertyName("metrics");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.GpuLoad(slot), "Utilization", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.GpuTemperature(slot), "Temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.GpuTemperature), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.GpuPower(slot), IsMac ? "GPU power" : "Power draw", ValueStyle.Power, ColorKeys.Power, powerThresholds, powerMax), powerDetail);
        WriteMetric(w, frame, new Def(MetricIds.GpuMemoryLoad(slot), "VRAM", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory), vramDetail);
        WriteMetric(w, frame, new Def($"gpu/{slot.ToString(CultureInfo.InvariantCulture)}/load/renderer", "Renderer", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load), null);
        WriteMetric(w, frame, new Def($"gpu/{slot.ToString(CultureInfo.InvariantCulture)}/load/tiler", "Tiler", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuHotSpotTemperature(slot), "Hot spot", ValueStyle.Temperature, ColorKeys.Temperature, new Thresholds(90, 100, 60)), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuMemoryJunctionTemperature(slot), "Memory junction", ValueStyle.Temperature, ColorKeys.Temperature, new Thresholds(90, 100, 60)), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuCoreClock(slot), "Core clock", ValueStyle.ClockMegahertz, ColorKeys.Gpu), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuMemoryClock(slot), "Memory clock", ValueStyle.ClockMegahertz, ColorKeys.Memory), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuMemoryUsed(slot), "VRAM used", ValueStyle.MemoryBytes, ColorKeys.Memory), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuFanRpm(slot, 1), "Fan", ValueStyle.Fan, ColorKeys.Fan), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuFanPercent(slot, 1), "Fan duty", ValueStyle.Percent, ColorKeys.Fan), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuVoltage(slot), "Core voltage", ValueStyle.Millivolts, ColorKeys.Power), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuEncoderLoad(slot), "Video encoder", ValueStyle.Percent, ColorKeys.Gpu), null);
        WriteMetric(w, frame, new Def(MetricIds.GpuDecoderLoad(slot), "Video decoder", ValueStyle.Percent, ColorKeys.Gpu), null);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteCpu(Utf8JsonWriter w, TelemetryFrame frame)
    {
        var summary = _context.Monitor.Summary;
        string name = PageViewModel.ShortCpuName(_context.Monitor.Engine.Devices.Get(MetricIds.CpuDeviceId)?.Name ?? summary.CpuName);
        bool appleSilicon = IsMac && name.StartsWith("Apple", StringComparison.Ordinal);

        w.WritePropertyName("cpu");
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WritePropertyName("metrics");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.CpuLoad, "Utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.CpuTemperature, "Temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.CpuPower, IsMac ? "CPU power" : "Package power", ValueStyle.Power, ColorKeys.Power, null, IsMac ? 60 : 200), null);
        WriteMetric(w, frame, IsMac
            ? new Def(MetricIds.CpuClockAverage, "Average clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu)
            : new Def(MetricIds.CpuClockEffectiveAverage, "Effective clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu), null);
        WriteMetric(w, frame, new Def(MetricIds.CpuLoadMaxThread, IsMac ? "Busiest core" : "Busiest thread", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load), null);
        if (appleSilicon)
        {
            WriteMetric(w, frame, new Def(MetricIds.CpuLoadPerformanceCores, "P-core utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load), null);
            WriteMetric(w, frame, new Def(MetricIds.CpuLoadEfficiencyCores, "E-core utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load), null);
            WriteMetric(w, frame, new Def(MetricIds.CpuClockPerformanceCores, "P-core clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu), null);
            WriteMetric(w, frame, new Def(MetricIds.CpuClockEfficiencyCores, "E-core clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu), null);
            WriteMetric(w, frame, new Def(MetricIds.NeuralEnginePower, "Neural Engine power", ValueStyle.Power, ColorKeys.Power), null);
            WriteMetric(w, frame, new Def(MetricIds.SystemPower, "Whole Mac", ValueStyle.Power, ColorKeys.Power), null);
        }
        else
        {
            WriteMetric(w, frame, new Def(MetricIds.CpuCcdTemperature(1), "CCD1 temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature), null);
            WriteMetric(w, frame, new Def(MetricIds.CpuCcdTemperature(2), "CCD2 temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature), null);
            WriteMetric(w, frame, new Def(MetricIds.CpuVoltage, "Core voltage", ValueStyle.Volts, ColorKeys.Power), null);
        }

        w.WriteEndArray();

        // Each logical processor's load, for the per-core bars.
        w.WritePropertyName("threads");
        w.WriteStartArray();
        int threads = Math.Clamp(summary.LogicalProcessors, 0, 256);
        for (int i = 0; i < threads; i++)
        {
            if (!frame.TryGetValue(MetricIds.CpuThreadLoad(i), out double load))
            {
                break;
            }

            w.WriteNumberValue(Math.Round(Math.Clamp(load, 0, 100), 1));
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteMemory(Utf8JsonWriter w, TelemetryFrame frame)
    {
        w.WritePropertyName("memory");
        w.WriteStartObject();
        if (frame.TryGetValue(MetricIds.MemoryUsed, out double used) && frame.TryGetValue(MetricIds.MemoryAvailable, out double available))
        {
            double total = used + available;
            w.WriteNumber("used", used);
            w.WriteNumber("available", available);
            w.WriteString("usedText", F.MemoryBytes(used).ToString());
            w.WriteString("availableText", F.MemoryBytes(available).ToString());
            w.WriteString("caption", total > 0 ? $"{F.MemoryBytes(used, 1)} of {F.MemoryBytes(total, 1)}" : string.Empty);
        }

        w.WritePropertyName("metrics");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.MemoryLoad, "Utilization", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.MemoryUsed, IsMac ? "Memory used" : "In use", ValueStyle.MemoryBytes, ColorKeys.Memory), null);
        WriteMetric(w, frame, new Def(MetricIds.MemoryAvailable, "Available", ValueStyle.MemoryBytes, ColorKeys.Neutral), null);
        WriteMetric(w, frame, new Def(MetricIds.MemoryTotal, IsMac ? "Physical memory" : "Usable", ValueStyle.MemoryBytes, ColorKeys.Neutral), null);
        if (IsMac)
        {
            WriteMetric(w, frame, new Def(MetricIds.MemoryApp, "App memory", ValueStyle.MemoryBytes, ColorKeys.Memory), null);
            WriteMetric(w, frame, new Def(MetricIds.MemoryWired, "Wired memory", ValueStyle.MemoryBytes, ColorKeys.Cpu), null);
            WriteMetric(w, frame, new Def(MetricIds.MemoryCompressed, "Compressed", ValueStyle.MemoryBytes, ColorKeys.Storage), null);
            WriteMetric(w, frame, new Def(MetricIds.MemorySwapUsed, "Swap used", ValueStyle.MemoryBytes, ColorKeys.Memory), null);
        }
        else
        {
            WriteMetric(w, frame, new Def(MetricIds.MemoryCommitUsed, "Committed", ValueStyle.MemoryBytes, ColorKeys.Memory),
                frame.TryGetValue(MetricIds.MemoryCommitLimit, out double commitLimit) ? $"of {F.MemoryBytes(commitLimit, 1)}" : null);
        }

        WriteMetric(w, frame, new Def(MetricIds.MemoryCache, IsMac ? "Cached files" : "Cached", ValueStyle.MemoryBytes, ColorKeys.Neutral), null);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteStorage(Utf8JsonWriter w, TelemetryFrame frame)
    {
        w.WritePropertyName("storage");
        w.WriteStartObject();
        w.WritePropertyName("volumes");
        w.WriteStartArray();
        foreach (var (letter, name) in _info!.Volumes.Take(16))
        {
            if (!frame.TryGetValue(MetricIds.VolumeTotal(letter), out double total) || total <= 0
                || !frame.TryGetValue(MetricIds.VolumeFree(letter), out double free))
            {
                continue;
            }

            double used = Math.Max(0, total - free);
            double fraction = used / total;
            w.WriteStartObject();
            w.WriteString("id", letter);
            w.WriteString("name", IsMac ? name : $"{letter}:{(string.IsNullOrWhiteSpace(name) || name == letter ? string.Empty : " " + name)}");
            w.WriteString("used", F.Bytes(used).ToString());
            w.WriteString("free", F.Bytes(free).ToString());
            w.WriteString("total", F.Bytes(total, 0).ToString());
            w.WriteString("percent", F.Percent(fraction * 100).ToString());
            w.WriteNumber("f", Math.Round(fraction, 4));
            w.WriteString("l", Thresholds.DriveSpace.Evaluate(fraction * 100).ToString());
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WritePropertyName("metrics");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.DiskTotalRead, "Read", ValueStyle.DiskRate, ColorKeys.Read), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.DiskTotalWrite, "Write", ValueStyle.DiskRate, ColorKeys.Write), null, always: true);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteNetwork(Utf8JsonWriter w, TelemetryFrame frame)
    {
        w.WritePropertyName("network");
        w.WriteStartObject();
        w.WriteString("adapter", _context.Monitor.NetworkAdapters().FirstOrDefault(a => a.IsActive).Name ?? "No active connection");
        w.WritePropertyName("metrics");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.NetworkActiveDown, "Download", ValueStyle.NetworkRate, ColorKeys.NetworkDown), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.NetworkActiveUp, "Upload", ValueStyle.NetworkRate, ColorKeys.NetworkUp), null, always: true);
        WriteMetric(w, frame, new Def(MetricIds.NetworkActiveReceivedSession, "Received this session", ValueStyle.Bytes, ColorKeys.NetworkDown), null);
        WriteMetric(w, frame, new Def(MetricIds.NetworkActiveSentSession, "Sent this session", ValueStyle.Bytes, ColorKeys.NetworkUp), null);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteBattery(Utf8JsonWriter w, TelemetryFrame frame)
    {
        if (!frame.Contains(MetricIds.BatteryCharge))
        {
            return;
        }

        w.WritePropertyName("battery");
        w.WriteStartArray();
        WriteMetric(w, frame, new Def(MetricIds.BatteryCharge, "Charge", ValueStyle.Percent, ColorKeys.Storage), null);
        WriteMetric(w, frame, new Def(MetricIds.BatteryHealth, "Health", ValueStyle.Percent, ColorKeys.Storage), null);
        WriteMetric(w, frame, new Def(MetricIds.BatteryCycles, "Cycles", ValueStyle.Count, ColorKeys.Neutral), null);
        WriteMetric(w, frame, new Def(MetricIds.BatteryPower, "Power", ValueStyle.Power, ColorKeys.Power), null);
        w.WriteEndArray();
    }

    // ---- Metrics ----------------------------------------------------------------------------------------------------

    /// <summary>A reading as the phone draws it.</summary>
    private sealed record Def(string Id, string Label, ValueStyle Style, string Color, Thresholds? Thresholds = null, double Max = 100);

    /// <summary>
    /// {"id", "label", "v" (raw value), "n" (number), "u" (unit), "l" (level), "c" (color), "f" (0–1 of the gauge),
    /// "g" ([cool, warm, hot] band starts), "t" (temperature), "d" (secondary line)}. Readings this computer doesn't
    /// report are left out, unless <paramref name="always"/> (then "n" is "—").
    /// </summary>
    private void WriteMetric(Utf8JsonWriter w, TelemetryFrame frame, Def def, string? detail, bool always = false)
    {
        bool has = frame.TryGetSample(def.Id, out var sample);
        if (!has && !always)
        {
            return;
        }

        w.WriteStartObject();
        w.WriteString("id", def.Id);
        w.WriteString("label", def.Label);
        w.WriteString("c", def.Color);
        if (def.Style == ValueStyle.Temperature)
        {
            w.WriteBoolean("t", true);
        }

        if (def.Thresholds is { } thresholds)
        {
            var (cool, warm, hot) = GaugeTone.Stops(thresholds, def.Max);
            w.WritePropertyName("g");
            w.WriteStartArray();
            w.WriteNumberValue(Math.Round(cool, 4));
            w.WriteNumberValue(Math.Round(warm, 4));
            w.WriteNumberValue(Math.Round(hot, 4));
            w.WriteEndArray();
        }

        if (has)
        {
            double v = sample.Value;
            var formatted = ValueStyles.Format(def.Style, sample.Source.Descriptor.Kind, v, F);
            w.WriteNumber("v", Math.Round(v, 3));
            w.WriteString("n", formatted.Number);
            w.WriteString("u", formatted.Unit);
            w.WriteString("l", (def.Thresholds ?? Thresholds.None).Evaluate(v).ToString());
            w.WriteNumber("f", def.Max > 0 ? Math.Round(Math.Clamp(v / def.Max, 0, 1), 4) : 0);
        }
        else
        {
            w.WriteString("n", "—");
            w.WriteString("u", string.Empty);
        }

        if (detail is not null)
        {
            w.WriteString("d", detail);
        }

        w.WriteEndObject();
    }

    private FormattedValue? Formatted(TelemetryFrame frame, string id, ValueStyle style) =>
        frame.TryGetSample(id, out var sample) ? ValueStyles.Format(style, sample.Source.Descriptor.Kind, sample.Value, F) : null;

    private static Thresholds AlertThresholds(AppSettings settings, string ruleId, Thresholds fallback, double elevatedOffset)
    {
        var rule = settings.Alerts.Rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is { Enabled: true } ? new Thresholds(rule.Threshold - elevatedOffset, rule.Threshold, fallback.Cool) : fallback;
    }

    private static string IconName(string iconKey) => iconKey switch
    {
        "Icon.Gpu" => "gpu",
        "Icon.Cpu" => "cpu",
        "Icon.Memory" => "memory",
        "Icon.Storage" => "storage",
        _ => "health",
    };
}
