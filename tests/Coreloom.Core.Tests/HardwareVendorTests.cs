using Coreloom.Core.Hardware;

namespace Coreloom.Core.Tests;

public class HardwareVendorTests
{
    [Theory]
    [InlineData("NVIDIA", HardwareVendor.Nvidia)]
    [InlineData("NVIDIA Corporation", HardwareVendor.Nvidia)]
    [InlineData("AMD", HardwareVendor.Amd)]
    [InlineData("AuthenticAMD", HardwareVendor.Amd)]
    [InlineData("Advanced Micro Devices, Inc.", HardwareVendor.Amd)]
    [InlineData("GenuineIntel", HardwareVendor.Intel)]
    [InlineData("Intel", HardwareVendor.Intel)]
    [InlineData("Apple", HardwareVendor.Apple)]
    [InlineData("Apple Inc.", HardwareVendor.Apple)]
    [InlineData("Microsoft", HardwareVendor.Unknown)]
    [InlineData("", HardwareVendor.Unknown)]
    [InlineData(null, HardwareVendor.Unknown)]
    public void VendorStrings(string? vendor, HardwareVendor expected) =>
        Assert.Equal(expected, HardwareVendors.FromVendorString(vendor));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", HardwareVendor.Nvidia)]
    [InlineData("GeForce GTX 1080 Ti", HardwareVendor.Nvidia)]
    [InlineData("NVIDIA RTX A6000", HardwareVendor.Nvidia)]
    [InlineData("Quadro P2200", HardwareVendor.Nvidia)]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", HardwareVendor.Amd)]
    [InlineData("AMD Radeon RX 7900 XTX", HardwareVendor.Amd)]
    [InlineData("AMD Radeon(TM) Graphics", HardwareVendor.Amd)]
    [InlineData("Ryzen Threadripper PRO 7995WX", HardwareVendor.Amd)]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K", HardwareVendor.Intel)]
    [InlineData("Intel(R) Core(TM) i9-14900K", HardwareVendor.Intel)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", HardwareVendor.Intel)]
    [InlineData("Intel(R) UHD Graphics 770", HardwareVendor.Intel)]
    [InlineData("Apple M4 Pro", HardwareVendor.Apple)]
    [InlineData("Apple M1 (Virtual)", HardwareVendor.Apple)]
    [InlineData("Apple Paravirtual device", HardwareVendor.Apple)]
    [InlineData("AMD Radeon Pro 5500M", HardwareVendor.Amd)]
    [InlineData("Intel Iris Plus Graphics", HardwareVendor.Intel)]
    [InlineData("Microsoft Basic Render Driver", HardwareVendor.Unknown)]
    [InlineData("", HardwareVendor.Unknown)]
    public void DeviceNames(string name, HardwareVendor expected) =>
        Assert.Equal(expected, HardwareVendors.FromDeviceName(name));

    [Fact]
    public void VendorString_WinsOverTheName()
    {
        // A provider that says "Intel" is trusted even when the name has no recognisable keyword.
        Assert.Equal(HardwareVendor.Intel, HardwareVendors.Detect("GenuineIntel", "Processor"));
        Assert.Equal(HardwareVendor.Amd, HardwareVendors.Detect(null, "AMD Ryzen 9 9950X3D"));
        Assert.Equal(HardwareVendor.Unknown, HardwareVendors.Detect(null, null));
    }

    [Fact]
    public void Keys_RoundTrip_AndUnknownIsEmpty()
    {
        foreach (var vendor in new[] { HardwareVendor.Nvidia, HardwareVendor.Amd, HardwareVendor.Intel, HardwareVendor.Apple })
        {
            Assert.Equal(vendor, HardwareVendors.FromKey(HardwareVendors.Key(vendor)));
            Assert.False(string.IsNullOrEmpty(HardwareVendors.DisplayName(vendor)));
        }

        Assert.Equal(string.Empty, HardwareVendors.Key(HardwareVendor.Unknown));
        Assert.Equal(HardwareVendor.Unknown, HardwareVendors.FromKey("matrox"));
        Assert.Equal(HardwareVendor.Nvidia, HardwareVendors.FromKey(" NVIDIA "));
    }
}
