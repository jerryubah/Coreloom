using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Coreloom.Core.Hardware;

/// <summary>The four registers one CPUID leaf returns.</summary>
public readonly record struct CpuIdRegisters(uint Eax, uint Ebx, uint Ecx, uint Edx);

/// <summary>Runs CPUID for a leaf and sub-leaf (zeros when the leaf isn't supported).</summary>
public delegate CpuIdRegisters CpuIdReader(uint leaf, uint subleaf);

/// <summary>Processor features worth showing that CPUID reports (what the chip supports, not what is switched on).</summary>
[Flags]
public enum ProcessorFeatures
{
    None = 0,

    /// <summary>Boosting above the base clock (AMD Core Performance Boost; Intel Turbo Boost).</summary>
    Boost = 1,

    /// <summary>Collaborative Processor Performance Control: Windows picks the fastest (preferred) cores.</summary>
    Cppc = 2,

    /// <summary>The chip changes its own clocks and voltages (hardware P-states).</summary>
    HardwarePStates = 4,

    /// <summary>Counters that show the clock the cores actually ran at (APERF/MPERF), behind "effective clock".</summary>
    EffectiveFrequency = 8,

    /// <summary>The time stamp counter ticks at a constant rate whatever the clock.</summary>
    InvariantTsc = 16,

    /// <summary>Energy counters for package and core power (RAPL).</summary>
    PowerReporting = 32,

    /// <summary>Two hardware threads per core (SMT / Hyper-Threading).</summary>
    Smt = 64,
}

/// <summary>What CPUID says about the processor: vendor, family/model/stepping, name, instruction sets and features.</summary>
public sealed record CpuIdInfo(string VendorId, int Family, int Model, int Stepping, string Brand, IReadOnlyList<string> InstructionSets, ProcessorFeatures Features)
{
    public static CpuIdInfo Empty { get; } = new(string.Empty, 0, 0, 0, string.Empty, [], ProcessorFeatures.None);

    public bool IsAmd => VendorId == "AuthenticAMD";

    public bool IsIntel => VendorId == "GenuineIntel";

    public bool IsKnown => VendorId.Length > 0 && Family > 0;

    /// <summary>"Family 17h · model 60h · stepping 1".</summary>
    public string Signature => CpuSignature.Format(Family, Model, Stepping);

    /// <summary>Reads the processor through <paramref name="cpuid"/>; <see cref="Empty"/> when it returns nothing.</summary>
    public static CpuIdInfo Read(CpuIdReader cpuid)
    {
        var leaf0 = cpuid(0, 0);
        if (leaf0.Ebx == 0 && leaf0.Ecx == 0 && leaf0.Edx == 0)
        {
            return Empty;
        }

        string vendor = Text(leaf0.Ebx, leaf0.Edx, leaf0.Ecx);
        uint maxLeaf = leaf0.Eax;
        var leaf1 = cpuid(1, 0);
        int baseFamily = (int)((leaf1.Eax >> 8) & 0xF);
        int baseModel = (int)((leaf1.Eax >> 4) & 0xF);
        int stepping = (int)(leaf1.Eax & 0xF);
        int extFamily = (int)((leaf1.Eax >> 20) & 0xFF);
        int extModel = (int)((leaf1.Eax >> 16) & 0xF);
        int family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
        int model = baseFamily == 0xF || (baseFamily == 0x6 && vendor == "GenuineIntel") ? (extModel << 4) + baseModel : baseModel;

        var leaf7 = maxLeaf >= 7 ? cpuid(7, 0) : default;
        var leaf6 = maxLeaf >= 6 ? cpuid(6, 0) : default;
        uint maxExtended = cpuid(0x80000000, 0).Eax;
        var ext1 = maxExtended >= 0x80000001 ? cpuid(0x80000001, 0) : default;
        var ext7 = maxExtended >= 0x80000007 ? cpuid(0x80000007, 0) : default;
        var ext8 = maxExtended >= 0x80000008 ? cpuid(0x80000008, 0) : default;

        string brand = string.Empty;
        if (maxExtended >= 0x80000004)
        {
            var builder = new StringBuilder(48);
            for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
            {
                var r = cpuid(leaf, 0);
                builder.Append(Text(r.Eax, r.Ebx, r.Ecx, r.Edx));
            }

            brand = Regex.Replace(builder.ToString().Trim('\0', ' '), @"\s+", " ");
        }

        var sets = new List<string>();
        void Add(bool present, string name)
        {
            if (present)
            {
                sets.Add(name);
            }
        }

        Add(Bit(leaf1.Edx, 26), "SSE2");
        Add(Bit(leaf1.Ecx, 0), "SSE3");
        Add(Bit(leaf1.Ecx, 9), "SSSE3");
        Add(Bit(leaf1.Ecx, 19), "SSE4.1");
        Add(Bit(leaf1.Ecx, 20), "SSE4.2");
        Add(Bit(ext1.Ecx, 6) && vendor == "AuthenticAMD", "SSE4a");
        Add(Bit(leaf1.Ecx, 28), "AVX");
        Add(Bit(leaf7.Ebx, 5), "AVX2");
        Add(Bit(leaf7.Ebx, 16), "AVX-512");
        Add(Bit(leaf1.Ecx, 12), "FMA3");
        Add(Bit(leaf1.Ecx, 25), "AES");
        Add(Bit(leaf7.Ebx, 29), "SHA");
        Add(Bit(leaf7.Ebx, 3) && Bit(leaf7.Ebx, 8), "BMI1/2");
        Add(Bit(leaf7.Ecx, 9), "VAES");

        var features = ProcessorFeatures.None;
        bool amd = vendor == "AuthenticAMD";
        if (amd ? Bit(ext7.Edx, 9) : Bit(leaf6.Eax, 1))
        {
            features |= ProcessorFeatures.Boost;
        }

        if (amd ? Bit(ext8.Ebx, 27) : Bit(leaf6.Eax, 7))
        {
            features |= ProcessorFeatures.Cppc;
        }

        if (amd && Bit(ext7.Edx, 7))
        {
            features |= ProcessorFeatures.HardwarePStates;
        }

        if (Bit(leaf6.Ecx, 0) || (amd && Bit(ext7.Edx, 10)))
        {
            features |= ProcessorFeatures.EffectiveFrequency;
        }

        if (Bit(ext7.Edx, 8))
        {
            features |= ProcessorFeatures.InvariantTsc;
        }

        if (amd && Bit(ext7.Edx, 14))
        {
            features |= ProcessorFeatures.PowerReporting;
        }

        if (Bit(leaf1.Edx, 28) && ((leaf1.Ebx >> 16) & 0xFF) > 1 && amd && maxExtended >= 0x8000001E && ((cpuid(0x8000001E, 0).Ebx >> 8) & 0xFF) >= 1)
        {
            features |= ProcessorFeatures.Smt;
        }

        return new CpuIdInfo(vendor, family, model, stepping, brand, sets, features);
    }

    /// <summary>The features in words, for the Specifications card ("Core Performance Boost, CPPC preferred cores...").</summary>
    public static IReadOnlyList<string> Describe(ProcessorFeatures features, bool amd)
    {
        var words = new List<string>();
        if (features.HasFlag(ProcessorFeatures.Boost))
        {
            words.Add(amd ? "Core Performance Boost" : "Turbo Boost");
        }

        if (features.HasFlag(ProcessorFeatures.Cppc))
        {
            words.Add(amd ? "CPPC preferred cores" : "Hardware P-states (Speed Shift)");
        }

        if (features.HasFlag(ProcessorFeatures.Smt))
        {
            words.Add("SMT");
        }

        if (features.HasFlag(ProcessorFeatures.EffectiveFrequency))
        {
            words.Add("Effective-clock counters");
        }

        if (features.HasFlag(ProcessorFeatures.PowerReporting))
        {
            words.Add("RAPL power reporting");
        }

        return words;
    }

    private static bool Bit(uint value, int bit) => ((value >> bit) & 1) != 0;

    private static string Text(params uint[] registers)
    {
        var bytes = new byte[registers.Length * 4];
        for (int i = 0; i < registers.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), registers[i]);
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }
}

/// <summary>Formats and reads back "Family 17h · model 60h · stepping 1".</summary>
public static partial class CpuSignature
{
    public static string Format(int family, int model, int stepping) =>
        string.Create(CultureInfo.InvariantCulture, $"Family {family:X}h · model {model:X2}h · stepping {stepping}");

    public static bool TryParse(string? text, out int family, out int model, out int stepping)
    {
        family = model = stepping = 0;
        if (text is null || Pattern().Match(text) is not { Success: true } m)
        {
            return false;
        }

        family = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        model = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        stepping = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return true;
    }

    [GeneratedRegex(@"Family\s+([0-9A-Fa-f]+)h\W+model\s+([0-9A-Fa-f]+)h\W+stepping\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
