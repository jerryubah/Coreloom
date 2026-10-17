using Coreloom.Core.Hardware;

namespace Coreloom.Core.Tests;

public class MemoryModuleNamesTests
{
    private static MemoryModuleInfo Module(string locator, string? bank) => new(locator, bank, 16L << 30, "DDR5", 6000, 6000, null, null);

    [Fact]
    public void ModulesReportingTheSameSlot_GetTheirChannel()
    {
        var labels = MemoryModuleNames.Labels([Module("DIMM 1", "P0 CHANNEL A"), Module("DIMM 1", "P0 CHANNEL B")]);

        Assert.Equal(["DIMM 1 · Channel A", "DIMM 1 · Channel B"], labels);
    }

    [Fact]
    public void DistinctSlots_StayAsTheyAre_AndTrueDuplicatesAreNumbered()
    {
        Assert.Equal(["DIMM_A2", "DIMM_B2"], MemoryModuleNames.Labels([Module("DIMM_A2", "BANK 0"), Module("DIMM_B2", "BANK 0")]));
        Assert.Equal(["DIMM 0 · BANK 0 #1", "DIMM 0 · BANK 0 #2", "Module"], MemoryModuleNames.Labels([Module("DIMM 0", "BANK 0"), Module("DIMM 0", "BANK 0"), Module(" ", null)]));
    }
}
