using System.Buffers.Binary;
using System.Diagnostics;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Sensors;

/// <summary>One IOReport delta: power from the energy counters and clocks from the frequency residencies.</summary>
public sealed record IOReportSample(
    double CpuPower,
    double GpuPower,
    double MemoryPower,
    double NeuralEnginePower,
    double GpuClockMhz,
    double GpuActiveFraction,
    IReadOnlyList<ClusterReading> Clusters);

/// <summary>
/// Samples IOReport's "Energy Model", "CPU Stats" and "GPU Stats" groups on Apple silicon. Each sample is the change
/// since the previous one, so power is energy over the elapsed time and a cluster's clock is the time-weighted average
/// of the frequencies it ran at (from the residency in each performance state and the frequency table in the power
/// manager's device-tree entry). This is how powermetrics and Activity Monitor's energy figures are made; it needs no
/// administrator rights.
/// </summary>
internal sealed class IOReportSampler : IDisposable
{
    private readonly nint _subscription;
    private readonly nint _channels;
    private readonly nint _subscribed;
    private readonly IReadOnlyList<double> _efficiencyMhz;
    private readonly IReadOnlyList<double> _performanceMhz;
    private readonly IReadOnlyList<double> _gpuMhz;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private nint _previous;
    private TimeSpan _previousAt;

    private IOReportSampler(nint subscription, nint channels, nint subscribed, IReadOnlyList<double> e, IReadOnlyList<double> p, IReadOnlyList<double> gpu)
    {
        _subscription = subscription;
        _channels = channels;
        _subscribed = subscribed;
        _efficiencyMhz = e;
        _performanceMhz = p;
        _gpuMhz = gpu;
        _previous = IOReport.IOReportCreateSamples(subscription, subscribed, 0);
        _previousAt = _clock.Elapsed;
    }

    public string Description { get; private set; } = string.Empty;

    public static IOReportSampler? Open()
    {
        var (e, p, gpu) = FrequencyTables.Read();
        nint channels = 0;
        foreach (var (group, subgroup) in new[] { ("Energy Model", (string?)null), ("CPU Stats", "CPU Complex Performance States"), ("GPU Stats", "GPU Performance States") })
        {
            nint g = CF.String(group);
            nint s = subgroup is null ? 0 : CF.String(subgroup);
            nint found = IOReport.IOReportCopyChannelsInGroup(g, s, 0, 0, 0);
            CF.CFRelease(g);
            if (s != 0)
            {
                CF.CFRelease(s);
            }

            if (found == 0)
            {
                continue;
            }

            // A group this Mac doesn't have can come back without a channel list, and merging that raises an
            // Objective-C exception that would end the process: only groups with channels are used.
            nint list = CF.Get(found, "IOReportChannels");
            if (!CF.IsArray(list) || CF.CFArrayGetCount(list) == 0)
            {
                CF.CFRelease(found);
                continue;
            }

            if (channels == 0)
            {
                channels = found;
            }
            else
            {
                IOReport.IOReportMergeChannels(channels, found, 0);
                CF.CFRelease(found);
            }
        }

        if (channels == 0)
        {
            return null;
        }

        nint mutableChannels = IOReport.CFDictionaryCreateMutableCopy(0, CF.CFDictionaryGetCount(channels), channels);
        CF.CFRelease(channels);
        if (mutableChannels == 0)
        {
            return null;
        }

        nint subscription = IOReport.IOReportCreateSubscription(0, mutableChannels, out nint subscribed, 0, 0);
        if (subscription == 0 || subscribed == 0)
        {
            CF.CFRelease(mutableChannels);
            return null;
        }

        var sampler = new IOReportSampler(subscription, mutableChannels, subscribed, e, p, gpu);
        int count = sampler.ChannelCount();
        if (count == 0)
        {
            sampler.Dispose();
            return null;
        }

        sampler.Description = $"({count} channels; frequency tables {e.Count}E/{p.Count}P/{gpu.Count} GPU)";
        return sampler;
    }

    private int ChannelCount()
    {
        nint array = CF.Get(_previous, "IOReportChannels");
        return CF.IsArray(array) ? (int)CF.CFArrayGetCount(array) : 0;
    }

    /// <summary>The change since the last call, or null on the first call or when nothing could be read.</summary>
    public IOReportSample? Sample()
    {
        nint current = IOReport.IOReportCreateSamples(_subscription, _subscribed, 0);
        var now = _clock.Elapsed;
        if (current == 0)
        {
            return null;
        }

        nint previous = _previous;
        double seconds = (now - _previousAt).TotalSeconds;
        _previous = current;
        _previousAt = now;
        if (previous == 0 || seconds <= 0.01)
        {
            if (previous != 0)
            {
                CF.CFRelease(previous);
            }

            return null;
        }

        nint delta = IOReport.IOReportCreateSamplesDelta(previous, current, 0);
        CF.CFRelease(previous);
        if (delta == 0)
        {
            return null;
        }

        try
        {
            return Parse(delta, seconds);
        }
        finally
        {
            CF.CFRelease(delta);
        }
    }

    private IOReportSample Parse(nint delta, double seconds)
    {
        double cpu = double.NaN, gpu = double.NaN, dram = double.NaN, ane = double.NaN;
        double gpuMhz = double.NaN, gpuActive = double.NaN;
        var clusters = new List<ClusterReading>();
        nint array = CF.Get(delta, "IOReportChannels");
        if (!CF.IsArray(array))
        {
            return new IOReportSample(cpu, gpu, dram, ane, gpuMhz, gpuActive, clusters);
        }

        int count = (int)CF.CFArrayGetCount(array);
        for (int i = 0; i < count; i++)
        {
            nint channel = CF.CFArrayGetValueAtIndex(array, i);
            string group = CF.ToStringValue(IOReport.IOReportChannelGetGroup(channel)) ?? string.Empty;
            string name = CF.ToStringValue(IOReport.IOReportChannelGetChannelName(channel)) ?? string.Empty;
            if (group == "Energy Model")
            {
                string unit = CF.ToStringValue(IOReport.IOReportChannelGetUnitLabel(channel)) ?? string.Empty;
                double watts = IOReportMath.Watts(IOReport.IOReportSimpleGetIntegerValue(channel, 0), unit, seconds);
                switch (IOReportMath.EnergyChannel(name))
                {
                    case EnergyKind.Cpu:
                        cpu = Add(cpu, watts);
                        break;
                    case EnergyKind.Gpu:
                        gpu = Add(gpu, watts);
                        break;
                    case EnergyKind.Memory:
                        dram = Add(dram, watts);
                        break;
                    case EnergyKind.NeuralEngine:
                        ane = Add(ane, watts);
                        break;
                }

                continue;
            }

            string subgroup = CF.ToStringValue(IOReport.IOReportChannelGetSubGroup(channel)) ?? string.Empty;
            if (group == "CPU Stats" && subgroup == "CPU Complex Performance States")
            {
                bool? performance = IOReportMath.ClusterKind(name);
                if (performance is bool p)
                {
                    var (mhz, active) = IOReportMath.Frequency(States(channel), p ? _performanceMhz : _efficiencyMhz);
                    if (double.IsFinite(mhz))
                    {
                        clusters.Add(new ClusterReading(name, p, mhz, active));
                    }
                }
            }
            else if (group == "GPU Stats" && subgroup == "GPU Performance States" && name == "GPUPH")
            {
                (gpuMhz, gpuActive) = IOReportMath.Frequency(States(channel), _gpuMhz);
            }
        }

        return new IOReportSample(cpu, gpu, dram, ane, gpuMhz, gpuActive, clusters);
    }

    private static double Add(double total, double watts) =>
        !double.IsFinite(watts) ? total : double.IsNaN(total) ? watts : total + watts;

    private static List<(string State, long Residency)> States(nint channel)
    {
        int count = IOReport.IOReportStateGetCount(channel);
        var states = new List<(string, long)>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            states.Add((CF.ToStringValue(IOReport.IOReportStateGetNameForIndex(channel, i)) ?? string.Empty, IOReport.IOReportStateGetResidency(channel, i)));
        }

        return states;
    }

    public void Dispose()
    {
        if (_previous != 0)
        {
            CF.CFRelease(_previous);
            _previous = 0;
        }

        CF.CFRelease(_subscribed);
        CF.CFRelease(_channels);
        CF.CFRelease(_subscription);
    }
}

public enum EnergyKind
{
    Other,
    Cpu,
    Gpu,
    Memory,
    NeuralEngine,
}

/// <summary>The arithmetic behind <see cref="IOReportSampler"/>, kept apart so it can be tested anywhere.</summary>
internal static class IOReportMath
{
    /// <summary>Energy in the channel's unit ("mJ", "uJ", "nJ") over <paramref name="seconds"/>, in watts.</summary>
    public static double Watts(long energy, string unit, double seconds)
    {
        if (seconds <= 0 || energy < 0)
        {
            return double.NaN;
        }

        double joules = unit.Trim() switch
        {
            "mJ" => energy / 1e3,
            "uJ" or "µJ" => energy / 1e6,
            "nJ" => energy / 1e9,
            "J" => energy,
            _ => double.NaN,
        };
        return joules / seconds;
    }

    /// <summary>
    /// Which part an "Energy Model" channel measures. "CPU Energy" on most chips ("DIE_0_CPU Energy" on Ultra), "GPU
    /// Energy", "ANE…" and "DRAM…"; per-cluster and SRAM channels are parts of those totals and are skipped.
    /// </summary>
    public static EnergyKind EnergyChannel(string name)
    {
        if (name.EndsWith("CPU Energy", StringComparison.Ordinal))
        {
            return EnergyKind.Cpu;
        }

        if (name == "GPU Energy" || (name.StartsWith("DIE_", StringComparison.Ordinal) && name.EndsWith("GPU Energy", StringComparison.Ordinal)))
        {
            return EnergyKind.Gpu;
        }

        if (name.StartsWith("ANE", StringComparison.Ordinal))
        {
            return EnergyKind.NeuralEngine;
        }

        if (name.StartsWith("DRAM", StringComparison.Ordinal))
        {
            return EnergyKind.Memory;
        }

        return EnergyKind.Other;
    }

    /// <summary>True for a performance cluster ("PCPU", "PCPU1", "PACC0"), false for efficiency ("ECPU"), null otherwise.</summary>
    public static bool? ClusterKind(string channel)
    {
        if (channel.StartsWith("PCPU", StringComparison.Ordinal) || channel.StartsWith("PACC", StringComparison.Ordinal))
        {
            return true;
        }

        if (channel.StartsWith("ECPU", StringComparison.Ordinal) || channel.StartsWith("EACC", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// The average frequency while active (time-weighted over the active states) and the share of time active. States
    /// before the first active one ("IDLE", "OFF", "DOWN") are idle; the active states line up with the frequency table.
    /// </summary>
    public static (double Mhz, double ActiveFraction) Frequency(IReadOnlyList<(string State, long Residency)> states, IReadOnlyList<double> tableMhz)
    {
        if (states.Count == 0 || tableMhz.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        int offset = 0;
        while (offset < states.Count && IsIdle(states[offset].State))
        {
            offset++;
        }

        double total = 0, active = 0, weighted = 0;
        for (int i = 0; i < states.Count; i++)
        {
            double residency = Math.Max(0, states[i].Residency);
            total += residency;
            if (i >= offset)
            {
                int index = i - offset;
                if (index < tableMhz.Count)
                {
                    active += residency;
                    weighted += residency * tableMhz[index];
                }
            }
        }

        if (total <= 0)
        {
            return (double.NaN, double.NaN);
        }

        double fraction = active / total;
        double mhz = active > 0 ? weighted / active : tableMhz[0];
        return (mhz, Math.Clamp(fraction, 0, 1));
    }

    private static bool IsIdle(string state) =>
        state is "IDLE" or "OFF" or "DOWN" || state.StartsWith("IDLE", StringComparison.Ordinal);
}

/// <summary>
/// The power manager's frequency tables (the device tree's "pmgr" node): "voltage-states1-sram" for the efficiency
/// cores, "voltage-states5-sram" for the performance cores and "voltage-states9" for the GPU. Each entry is a 32-bit
/// frequency and a 32-bit voltage.
/// </summary>
internal static class FrequencyTables
{
    public static (IReadOnlyList<double> Efficiency, IReadOnlyList<double> Performance, IReadOnlyList<double> Gpu) Read()
    {
        foreach (var (_, properties) in IOKit.Services("AppleARMIODevice"))
        {
            if (CF.Text(properties, "name") is not "pmgr")
            {
                continue;
            }

            return (Table(properties, "voltage-states1-sram"), Table(properties, "voltage-states5-sram"), Table(properties, "voltage-states9"));
        }

        return ([], [], []);
    }

    private static IReadOnlyList<double> Table(Dictionary<string, object?> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is byte[] data ? ParseMhz(data) : [];

    /// <summary>Frequencies in MHz from (frequency, voltage) pairs; zero entries are dropped. Hz on older chips, kHz on newer ones.</summary>
    public static IReadOnlyList<double> ParseMhz(ReadOnlySpan<byte> data)
    {
        var result = new List<double>();
        for (int i = 0; i + 8 <= data.Length; i += 8)
        {
            uint frequency = BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
            if (frequency == 0)
            {
                continue;
            }

            result.Add(frequency > 100_000_000 ? frequency / 1e6 : frequency / 1e3);
        }

        return result;
    }
}
