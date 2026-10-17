using System.Runtime.Intrinsics.X86;
using Coreloom.Core.Hardware;

namespace Coreloom.Hardware.Native;

/// <summary>Runs the CPUID instruction (read-only, no driver or administrator rights needed).</summary>
internal static class CpuIdNative
{
    /// <summary>The processor's CPUID information, or <see cref="CpuIdInfo.Empty"/> on processors without CPUID (Arm).</summary>
    public static CpuIdInfo Read()
    {
        if (!X86Base.IsSupported)
        {
            return CpuIdInfo.Empty;
        }

        try
        {
            return CpuIdInfo.Read(static (leaf, subleaf) =>
            {
                var (eax, ebx, ecx, edx) = X86Base.CpuId(unchecked((int)leaf), unchecked((int)subleaf));
                return new CpuIdRegisters(unchecked((uint)eax), unchecked((uint)ebx), unchecked((uint)ecx), unchecked((uint)edx));
            });
        }
        catch (Exception)
        {
            return CpuIdInfo.Empty;
        }
    }
}
