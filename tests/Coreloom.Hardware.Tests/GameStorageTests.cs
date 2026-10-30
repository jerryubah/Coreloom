using System.Diagnostics;
using System.Text;
using Coreloom.Hardware.Gaming;
using Coreloom.Hardware.Gpu;

namespace Coreloom.Hardware.Tests;

/// <summary>
/// 1.8.4's reads for the DirectStorage check and VRAM management, on the build machine: BypassIO on the system drive,
/// the drive's kind, Direct3D 12's shader model (on Windows' software adapter, which every PC has) and the GPU Process
/// Memory counters.
/// </summary>
public class GameStorageTests
{
    [Fact]
    public void BypassIoAnswers_AreReadFromWindowsOwnLayout()
    {
        var output = new byte[GameStorageProbe.OutputSize];
        BitConverter.GetBytes(GameStorageProbe.OperationQuery).CopyTo(output, 0);
        Assert.True(GameStorageProbe.ParseQuery(output).Supported); // OpStatus 0 = STATUS_SUCCESS

        BitConverter.GetBytes(unchecked((int)0xC00000BB)).CopyTo(output, 24); // STATUS_NOT_SUPPORTED
        Encoding.Unicode.GetBytes("wcifs.sys").CopyTo(output, 30);
        Encoding.Unicode.GetBytes("A filter driver attached to the volume doesn't support BypassIO.").CopyTo(output, 96);
        var blocked = GameStorageProbe.ParseQuery(output);
        Assert.False(blocked.Supported);
        Assert.Equal("wcifs.sys", blocked.Driver);
        Assert.StartsWith("A filter driver attached", blocked.Reason);
        Assert.Equal(352, GameStorageProbe.OutputSize);
        Assert.Equal(0x00090448u, GameStorageProbe.FsctlManageBypassIo);
    }

    [Fact]
    public void TheSystemDrive_SaysWhatItIs_AndWhetherBypassIoWorks()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var (bus, ssd) = GameStorageProbe.DriveKind(root);
        var bypass = GameStorageProbe.BypassIo(root);
        // The runner's drive is a virtual disk; either answer is fine, as long as asking works and reads back cleanly.
        Console.WriteLine($"{root}: {bus}, SSD {ssd?.ToString() ?? "unknown"}, BypassIO {(bypass is null ? "can't be asked" : bypass.Supported ? "supported" : $"not supported ({bypass.Driver}: {bypass.Reason})")}");
        Assert.False(string.IsNullOrEmpty(bus));
        if (bypass is { Supported: false })
        {
            Assert.DoesNotContain('\0', bypass.Driver ?? string.Empty);
            Assert.DoesNotContain('\0', bypass.Reason ?? string.Empty);
        }
    }

    [Fact]
    public void Direct3D12_ReportsAShaderModel()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var found = Direct3D.HighestShaderModel(includeSoftware: true);
        Assert.SkipWhen(found is null, "No adapter at all.");
        Console.WriteLine($"{found!.Value.Adapter.Name}: shader model {(found.Value.ShaderModel is int m ? $"{m >> 4}.{m & 0xF}" : "none (no Direct3D 12)")}");
        Assert.SkipWhen(found.Value.ShaderModel is null, "Direct3D 12 isn't available on this machine.");
        Assert.InRange(found.Value.ShaderModel!.Value, 0x51, 0x69);
    }

    [Fact]
    public void GpuProcessMemory_InstanceNamesAreRead()
    {
        Assert.Equal((1234, 0x0000C3A1L), GpuProcessMemory.Parse("pid_1234_luid_0x00000000_0x0000C3A1_phys_0"));
        Assert.Equal((7, (1L << 32) | 0x10), GpuProcessMemory.Parse("pid_7_luid_0x00000001_0x00000010_phys_0"));
        Assert.Null(GpuProcessMemory.Parse("_Total"));
        Assert.Null(GpuProcessMemory.Parse("pid_x_luid_0x0_0x0_phys_0"));
    }

    [Fact]
    public void GpuProcessMemory_Counters_OpenAndRead()
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
