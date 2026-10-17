using System.Text;
using Coreloom.Core.Hardware;

namespace Coreloom.Core.Tests;

public class CpuIdTests
{
    /// <summary>A CPUID stand-in: vendor, signature (leaf 1 EAX), brand string and a few feature bits.</summary>
    private static CpuIdReader Fake(string vendor, uint signature, string brand, uint leaf1Ecx = 0, uint leaf1Edx = 0, uint leaf7Ebx = 0, uint ext7Edx = 0, uint ext8Ebx = 0)
    {
        byte[] v = Encoding.ASCII.GetBytes(vendor);
        byte[] b = new byte[48];
        Encoding.ASCII.GetBytes(brand).CopyTo(b, 0);
        uint Word(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
        return (leaf, _) => leaf switch
        {
            0 => new CpuIdRegisters(0x10, Word(v, 0), Word(v, 8), Word(v, 4)),
            1 => new CpuIdRegisters(signature, 0x00100800, leaf1Ecx, leaf1Edx),
            7 => new CpuIdRegisters(0, leaf7Ebx, 0, 0),
            0x80000000 => new CpuIdRegisters(0x80000008, 0, 0, 0),
            >= 0x80000002 and <= 0x80000004 => new CpuIdRegisters(
                Word(b, (int)(leaf - 0x80000002) * 16), Word(b, ((int)(leaf - 0x80000002) * 16) + 4),
                Word(b, ((int)(leaf - 0x80000002) * 16) + 8), Word(b, ((int)(leaf - 0x80000002) * 16) + 12)),
            0x80000007 => new CpuIdRegisters(0, 0, 0, ext7Edx),
            0x80000008 => new CpuIdRegisters(0, ext8Ebx, 0, 0),
            _ => default,
        };
    }

    [Fact]
    public void ReadsAnAmdProcessor_FamilyModelSteppingNameAndFeatures()
    {
        // Ryzen 5 4600H: family 17h (Fh + 8h), model 60h, stepping 1.
        var info = CpuIdInfo.Read(Fake("AuthenticAMD", 0x00860F01, "AMD Ryzen 5 4600H with Radeon Graphics      ",
            leaf1Ecx: (1u << 20) | (1u << 28) | (1u << 12) | (1u << 25), leaf1Edx: 1u << 26, leaf7Ebx: (1u << 5) | (1u << 29),
            ext7Edx: (1u << 9) | (1u << 10) | (1u << 14) | (1u << 8), ext8Ebx: 1u << 27));

        Assert.True(info.IsAmd);
        Assert.Equal(0x17, info.Family);
        Assert.Equal(0x60, info.Model);
        Assert.Equal(1, info.Stepping);
        Assert.Equal("AMD Ryzen 5 4600H with Radeon Graphics", info.Brand);
        Assert.Equal("Family 17h · model 60h · stepping 1", info.Signature);
        Assert.Equal(["SSE2", "SSE4.2", "AVX", "AVX2", "FMA3", "AES", "SHA"], info.InstructionSets);
        Assert.True(info.Features.HasFlag(ProcessorFeatures.Boost));
        Assert.True(info.Features.HasFlag(ProcessorFeatures.Cppc));
        Assert.True(info.Features.HasFlag(ProcessorFeatures.EffectiveFrequency));
        Assert.True(info.Features.HasFlag(ProcessorFeatures.PowerReporting));
        Assert.Equal(["Core Performance Boost", "CPPC preferred cores", "Effective-clock counters", "RAPL power reporting"], CpuIdInfo.Describe(info.Features, amd: true));
    }

    [Fact]
    public void ReadsAnIntelProcessor_WithItsExtendedModel()
    {
        // Core i7-12700K: family 6, model 97h, stepping 2.
        var info = CpuIdInfo.Read(Fake("GenuineIntel", 0x00090672, "12th Gen Intel(R) Core(TM) i7-12700K"));

        Assert.True(info.IsIntel);
        Assert.Equal(6, info.Family);
        Assert.Equal(0x97, info.Model);
        Assert.Equal(2, info.Stepping);
    }

    [Fact]
    public void NoCpuid_GivesEmpty()
    {
        var info = CpuIdInfo.Read((_, _) => default);

        Assert.Same(CpuIdInfo.Empty, info);
        Assert.False(info.IsKnown);
    }

    [Theory]
    [InlineData("Family 17h · model 60h · stepping 1", 0x17, 0x60, 1)]
    [InlineData("Family 1Ah, model 44h, stepping 0", 0x1A, 0x44, 0)]
    public void Signature_ReadsBack(string text, int family, int model, int stepping)
    {
        Assert.True(CpuSignature.TryParse(text, out int f, out int m, out int s));
        Assert.Equal((family, model, stepping), (f, m, s));
        Assert.Equal(text.Replace(",", " ·", StringComparison.Ordinal), CpuSignature.Format(f, m, s));
    }

    [Fact]
    public void Signature_RejectsOtherText() => Assert.False(CpuSignature.TryParse("8 cores", out _, out _, out _));
}

public class AmdProcessorTests
{
    [Theory]
    [InlineData("AMD Ryzen 5 4600H with Radeon Graphics", 0x17, 0x60, "4600H", "Zen 2", "Renoir", ProcessorSegment.Laptop, 0)]
    [InlineData("AMD Ryzen 5 4500 6-Core Processor", 0x17, 0x60, "4500", "Zen 2", "Renoir", ProcessorSegment.Desktop, 65)]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", 0x17, 0x71, "3600", "Zen 2", "Matisse", ProcessorSegment.Desktop, 65)]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", 0x19, 0x21, "5800X3D", "Zen 3", "Vermeer", ProcessorSegment.Desktop, 105)]
    [InlineData("AMD Ryzen 7 5700G with Radeon Graphics", 0x19, 0x50, "5700G", "Zen 3", "Cezanne", ProcessorSegment.DesktopWithGraphics, 65)]
    [InlineData("AMD Ryzen 7 6800H with Radeon Graphics", 0x19, 0x44, "6800H", "Zen 3+", "Rembrandt", ProcessorSegment.Laptop, 0)]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", 0x19, 0x61, "7950X", "Zen 4", "Raphael", ProcessorSegment.Desktop, 170)]
    [InlineData("AMD Ryzen 9 7945HX with Radeon Graphics", 0x19, 0x61, "7945HX", "Zen 4", "Dragon Range", ProcessorSegment.Laptop, 0)]
    [InlineData("AMD Ryzen 7 8845HS w/ Radeon 780M Graphics", 0x19, 0x75, "8845HS", "Zen 4", "Hawk Point", ProcessorSegment.Laptop, 0)]
    [InlineData("AMD Ryzen 7 8700G w/ Radeon 780M Graphics", 0x19, 0x75, "8700G", "Zen 4", "Phoenix", ProcessorSegment.DesktopWithGraphics, 65)]
    [InlineData("AMD Ryzen Z1 Extreme", 0x19, 0x74, "Z1 Extreme", "Zen 4", "Phoenix", ProcessorSegment.Handheld, 0)]
    [InlineData("AMD Ryzen AI 9 HX 370 w/ Radeon 890M", 0x1A, 0x24, "HX 370", "Zen 5", "Strix Point", ProcessorSegment.Laptop, 0)]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", 0x1A, 0x44, "9800X3D", "Zen 5", "Granite Ridge", ProcessorSegment.Desktop, 120)]
    [InlineData("AMD Ryzen Threadripper 3970X 32-Core Processor", 0x17, 0x31, "3970X", "Zen 2", "Castle Peak", ProcessorSegment.Workstation, 0)]
    [InlineData("AMD EPYC 7763 64-Core Processor", 0x19, 0x01, "7763", "Zen 3", "Milan", ProcessorSegment.Server, 0)]
    public void Identifies_ArchitectureCodenameTypeAndStockTdp(string brand, int family, int model, string number, string architecture, string codename, ProcessorSegment segment, int tdp)
    {
        var profile = AmdProcessors.Create(brand, family, model);

        Assert.Equal(number, profile.ModelNumber);
        Assert.Equal(architecture, profile.Architecture);
        Assert.Equal(codename, profile.Codename);
        Assert.Equal(segment, profile.Segment);
        Assert.Equal(tdp == 0 ? null : tdp, profile.TdpWatts);
    }

    [Fact]
    public void WithoutCpuid_TheNameStillGivesTheTypeAndStockLimit()
    {
        var profile = AmdProcessors.Create("AMD Ryzen 7 7800X3D 8-Core Processor");

        Assert.Null(profile.Architecture);
        Assert.Equal(ProcessorSegment.Desktop, profile.Segment);
        Assert.True(profile.HasVCache);
        Assert.Equal(162, profile.StockPptWatts);
    }

    [Fact]
    public void AnUnknownModel_FallsBackToTheFamily()
    {
        var profile = AmdProcessors.Create("AMD Ryzen 5 9999 Processor", 0x1A, 0x50);

        Assert.Equal("Zen 5", profile.Architecture);
        Assert.Null(profile.Codename);
        Assert.Null(profile.TdpWatts);
    }

    [Theory]
    [InlineData(65, 88)]
    [InlineData(95, 128)]
    [InlineData(105, 142)]
    [InlineData(120, 162)]
    [InlineData(170, 230)]
    public void StockPpt_IsAmdsDefault(int tdp, int ppt) => Assert.Equal(ppt, AmdProcessors.StockPpt(tdp));

    [Fact]
    public void ArchitectureText_JoinsArchitectureAndCodename()
    {
        Assert.Equal("Zen 2 · Renoir", AmdProcessors.Create("AMD Ryzen 5 4600H", 0x17, 0x60).ArchitectureText);
        Assert.Null(AmdProcessors.Create("AMD Ryzen 5 4600H").ArchitectureText);
    }
}

public class AmdTuningTests
{
    private static readonly AmdProcessorProfile Desktop65 = AmdProcessors.Create("AMD Ryzen 5 5600X 6-Core Processor", 0x19, 0x21);
    private static readonly AmdProcessorProfile Laptop = AmdProcessors.Create("AMD Ryzen 5 4600H with Radeon Graphics", 0x17, 0x60);

    [Fact]
    public void PowerClearlyAboveTheStockLimit_SeveralTimes_MeansPboIsLikelyOn()
    {
        var result = AmdTuning.Assess([40, 96, 120, 118, 121, 60], Desktop65);

        Assert.Equal(PowerLimitVerdict.AboveStock, result.Verdict);
        Assert.Equal(121, result.PeakWatts);
        Assert.Equal(88, result.StockWatts);
    }

    [Fact]
    public void OneSpikeAboveTheLimit_IsNotEnough()
    {
        var result = AmdTuning.Assess([40, 130, 50, 52], Desktop65);

        Assert.Equal(PowerLimitVerdict.NotEnoughLoad, result.Verdict);
    }

    [Fact]
    public void PowerHeldNearTheLimit_MeansStockLimits()
    {
        var result = AmdTuning.Assess([20, 76, 78, 80, 79, 77, 75, 30], Desktop65);

        Assert.Equal(PowerLimitVerdict.HeldAtStock, result.Verdict);
    }

    [Fact]
    public void ThePptSensor_CountsAsNearOnlyCloseToTheLimit()
    {
        double[] watts = [76, 78, 80, 79, 77, 75];

        Assert.Equal(PowerLimitVerdict.HeldAtStock, AmdTuning.Assess(watts, Desktop65).Verdict);
        Assert.Equal(PowerLimitVerdict.NotEnoughLoad, AmdTuning.Assess(watts, Desktop65, fromPptSensor: true).Verdict);
    }

    [Fact]
    public void NoReadings_AndLaptops_AreSaidSo()
    {
        Assert.Equal(PowerLimitVerdict.NoReadings, AmdTuning.Assess([], Desktop65).Verdict);
        Assert.Equal(PowerLimitVerdict.NotApplicable, AmdTuning.Assess([45, 54, 54, 54], Laptop).Verdict);
    }

    [Fact]
    public void NoControlCanBeChanged_AndEachExplainsWhy()
    {
        foreach (var profile in new[] { Desktop65, Laptop, AmdProcessors.Create("AMD Ryzen 7 5800X3D", 0x19, 0x21) })
        {
            var controls = AmdTuning.Controls(profile, AmdTuning.Assess([], profile), sensorsAccessible: true);

            Assert.Equal(["Power limits (PPT · TDC · EDC)", "Precision Boost Overdrive", "Curve Optimizer", "Eco mode", "Manual clocks and voltages"], controls.Select(c => c.Name));
            Assert.All(controls, c => Assert.False(c.CanChange));
            Assert.All(controls, c => Assert.False(string.IsNullOrWhiteSpace(c.Explanation)));
        }
    }

    [Fact]
    public void Desktop_ShowsTheStockLimitAndTheInference()
    {
        var controls = AmdTuning.Controls(Desktop65, AmdTuning.Assess([120, 121, 119], Desktop65), sensorsAccessible: true);

        Assert.Equal("Stock PPT 88 W", controls[0].Status);
        Assert.Equal(TuningState.ReadOnly, controls[0].State);
        Assert.Contains("65 W processor", controls[0].Explanation);
        Assert.Equal("Likely on", controls[1].Status);
        Assert.Equal(TuningState.Detected, controls[1].State);
        Assert.Contains("121 W", controls[1].Explanation);
        Assert.Equal(TuningState.Unavailable, controls[2].State);
        Assert.Contains("Ryzen Master", controls[2].Explanation);
    }

    [Fact]
    public void Laptop_SaysTheMakerSetsTheLimits()
    {
        var controls = AmdTuning.Controls(Laptop, AmdTuning.Assess([45], Laptop), sensorsAccessible: false);

        Assert.Equal("Set by the maker", controls[0].Status);
        Assert.Equal("Not offered", controls[1].Status);
        Assert.Equal("Use power modes", controls[3].Status);
    }

    [Fact]
    public void Zen2Desktop_HasNoCurveOptimizer_AndOlderX3dIsLocked()
    {
        var zen2 = AmdProcessors.Create("AMD Ryzen 5 3600 6-Core Processor", 0x17, 0x71);
        var x3d = AmdProcessors.Create("AMD Ryzen 7 5800X3D 8-Core Processor", 0x19, 0x21);
        var zen5x3d = AmdProcessors.Create("AMD Ryzen 7 9800X3D 8-Core Processor", 0x1A, 0x44);

        Assert.Equal("Not on this processor", AmdTuning.Controls(zen2, default, true)[2].Status);
        Assert.Contains("locks the multiplier", AmdTuning.Controls(x3d, default, true)[4].Explanation);
        Assert.DoesNotContain("locks the multiplier", AmdTuning.Controls(zen5x3d, default, true)[4].Explanation);
    }

    [Fact]
    public void WithoutPowerReadings_SaysWhatIsNeeded()
    {
        var controls = AmdTuning.Controls(Desktop65, AmdTuning.Assess([], Desktop65), sensorsAccessible: false);

        Assert.Contains("administrator", controls[1].Explanation);
    }
}
