using System.Diagnostics;
using Coreloom.Hardware.Gaming;

namespace Coreloom.Hardware.Tests;

/// <summary>VRAM management's reads (1.8.4) on the build machine: the GPU Process Memory counters' instance names and values.</summary>
public class GpuProcessMemoryTests
{
    [Fact]
    public void InstanceNamesAreRead()
    {
        Assert.Equal((1234, 0x0000C3A1L), GpuProcessMemory.Parse("pid_1234_luid_0x00000000_0x0000C3A1_phys_0"));
        Assert.Equal((7, (1L << 32) | 0x10), GpuProcessMemory.Parse("pid_7_luid_0x00000001_0x00000010_phys_0"));
        Assert.Null(GpuProcessMemory.Parse("_Total"));
        Assert.Null(GpuProcessMemory.Parse("pid_x_luid_0x0_0x0_phys_0"));
    }

    [Fact]
    public void Counters_OpenAndRead()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var counters = GpuProcessMemory.Open(out string? problem);
        Assert.SkipWhen(counters is null, $"GPU Process Memory counters aren't available here: {problem}");
        var read = counters!.Read([Environment.ProcessId, Process.GetCurrentProcess().Id]);
        Console.WriteLine($"This process: {(read is { } r ? $"{r.Dedicated} dedicated, {r.Shared} shared" : "no reading")}");
        if (read is { } value)
        {
            Assert.True(value.Dedicated >= 0 && value.Shared >= 0);
        }
    }
}
