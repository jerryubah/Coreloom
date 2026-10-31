using Coreloom.Core.Apps;
using Coreloom.Core.Drivers;
using Coreloom.Core.Optimize;

namespace Coreloom.Core.Tests;

/// <summary>1.8.6: the Drivers page's grouping, versions and update sources, and the Applications page's installed-apps rules.</summary>
public class Release186Tests
{
    private static InstalledDriver Driver(string name, string? cls, string? provider, string? version, string? hardwareId = null, int problem = 0) =>
        new($"ID\\{name}", name, cls, provider, version, new DateTime(2026, 9, 1))
        {
            HardwareIds = hardwareId is null ? [] : [hardwareId, hardwareId + "&REV_01"],
            Problem = problem,
        };

    [Fact]
    public void Drivers_AreGroupedAndLabeled_AlikeOnesCounted_EndpointsLeftOut()
    {
        var drivers = new[]
        {
            Driver("NVIDIA GeForce RTX 5090", "Display", "NVIDIA", "32.0.15.8157"),
            Driver("Realtek USB Audio", "MEDIA", "Realtek", "6.3.9600.2393"),
            Driver("Speakers (Realtek USB Audio)", "AudioEndpoint", "Microsoft", "10.0.26100.1"),
            Driver("USB Root Hub (USB 3.0)", "USB", "(Standard USB HUBs)", "10.0.26100.1"),
            Driver("USB Root Hub (USB 3.0)", "USB", "(Standard USB HUBs)", "10.0.26100.1"),
            Driver("Intel(R) Wi-Fi 7 BE200", "Net", "Intel", "23.80.0.4"),
            Driver("AMD GPIO Controller", "System", "Advanced Micro Devices, Inc", "2.2.0.130", problem: 10),
            Driver("No driver here", "Unknown", null, null),
            Driver("Generic Monitor (Odyssey G8)", "Monitor", "Microsoft", "10.0.26100.1"),
        };
        var groups = DriverCatalog.Group(drivers);

        Assert.Equal(
            [DriverCategory.Graphics, DriverCategory.Displays, DriverCategory.Audio, DriverCategory.Network, DriverCategory.Usb, DriverCategory.System],
            groups.Select(g => g.Category));
        var hubs = groups.Single(g => g.Category == DriverCategory.Usb);
        Assert.Equal(2, hubs.Count);
        Assert.DoesNotContain(groups, g => g.Name.StartsWith("Speakers", StringComparison.Ordinal)); // an endpoint, not a driver of its own
        Assert.True(groups.Single(g => g.Category == DriverCategory.System).HasProblem);
        Assert.True(groups.Single(g => g.Category == DriverCategory.Displays).IsMicrosoft);
        Assert.False(groups.Single(g => g.Category == DriverCategory.Graphics).IsMicrosoft);

        Assert.Equal("Keyboards, mice and controllers", DriverCatalog.Label(DriverCatalog.Categorize("HIDClass")));
        Assert.Equal("Chipset and system", DriverCatalog.Label(DriverCategory.System));
        Assert.Equal(DriverCategory.Storage, DriverCatalog.Categorize("SCSIAdapter"));
        Assert.Equal(DriverCategory.Bluetooth, DriverCatalog.Categorize("bluetooth"));
        Assert.Equal(DriverCategory.Other, DriverCatalog.Categorize(null));
        Assert.All(Enum.GetValues<DriverCategory>(), c => Assert.StartsWith("Icon.", DriverCatalog.IconKey(c)));
        Assert.True(DriverCatalog.IsMicrosoft("Microsoft Windows"));
        Assert.False(DriverCatalog.IsMicrosoft("NVIDIA"));
    }

    [Fact]
    public void Versions_NvidiasNumbers_AndComparisons()
    {
        Assert.Equal("581.57", DriverCatalog.NvidiaVersion("32.0.15.8157"));
        Assert.Equal("536.99", DriverCatalog.NvidiaVersion("31.0.15.3699"));
        Assert.Equal("621.01", DriverCatalog.NvidiaVersion("32.0.16.2101"));
        Assert.Null(DriverCatalog.NvidiaVersion("6.3.9600"));
        Assert.Null(DriverCatalog.NvidiaVersion("abc"));
        Assert.True(DriverCatalog.CompareVersions("621.01", "581.57") > 0);
        Assert.True(DriverCatalog.CompareVersions("23.80.0.4", "23.100.0.1") < 0); // numbers, not text
        Assert.Equal(0, DriverCatalog.CompareVersions("1.2", "1.2.0"));
        Assert.Equal("1 update", DriverCatalog.Updates(1));
        Assert.Equal("3 updates", DriverCatalog.Updates(3));
    }

    [Fact]
    public void WindowsUpdateOffers_AreMatchedToTheirDevice_ByHardwareIdThenName()
    {
        var installed = new[]
        {
            Driver("Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel Corporation", "22.200.0.2", hardwareId: @"USB\VID_8087&PID_0036"),
            Driver("Realtek PCIe 2.5GbE Family Controller", "Net", "Realtek", "10.68.0.0"),
        };
        var bluetooth = DriverCatalog.FromWindowsUpdate(new WindowsUpdateDriver("u1", "Intel Corporation - Bluetooth - 22.240.0.2")
        {
            DriverClass = "Bluetooth",
            Provider = "Intel Corporation",
            Model = "Intel(R) Wireless Bluetooth(R)",
            HardwareId = @"usb\vid_8087&pid_0036",
            Date = new DateTime(2026, 8, 20),
            SizeBytes = 14_000_000,
        }, installed);
        Assert.Equal("u1", bluetooth.Id);
        Assert.Equal(DriverSource.WindowsUpdate, bluetooth.Source);
        Assert.Equal(DriverCategory.Bluetooth, bluetooth.Category);
        Assert.Equal("Intel(R) Wireless Bluetooth(R)", bluetooth.Name);
        Assert.Equal("22.200.0.2", bluetooth.CurrentVersion);
        Assert.Equal("22.240.0.2", bluetooth.NewVersion);
        Assert.Equal(14_000_000, bluetooth.SizeBytes);

        var network = DriverCatalog.FromWindowsUpdate(new WindowsUpdateDriver("u2", "Realtek - Net - 10.72.0.0") { Model = "Realtek PCIe 2.5GbE Family Controller" }, installed);
        Assert.Equal("10.68.0.0", network.CurrentVersion); // by name
        Assert.Equal("Realtek", network.Provider);

        var unknown = DriverCatalog.FromWindowsUpdate(new WindowsUpdateDriver("u3", "Advanced Micro Devices, Inc. - System - 2.0.0.11") { DriverClass = "System" }, installed);
        Assert.Null(unknown.CurrentVersion);
        Assert.Equal(DriverCategory.System, unknown.Category);
        Assert.Equal("System", unknown.Name);
        Assert.Equal("2.0.0.11", unknown.NewVersion);
    }

    [Fact]
    public void Nvidia_FindsTheCard_ReadsItsNewestDriver_AndOffersItWhenNewer()
    {
        const string list = """
            <?xml version="1.0" encoding="utf-8"?>
            <LookupValueSearch><LookupValues>
              <LookupValue ParentID="120"><Name>GeForce RTX 3080</Name><Value>929</Value></LookupValue>
              <LookupValue ParentID="131"><Name>GeForce RTX 5090</Name><Value>1066</Value></LookupValue>
              <LookupValue ParentID="129"><Name>GeForce RTX 4070 Laptop GPU</Name><Value>1007</Value></LookupValue>
              <LookupValue ParentID="x"><Name>Broken</Name><Value>1</Value></LookupValue>
            </LookupValues></LookupValueSearch>
            """;
        var products = NvidiaDrivers.ParseProducts(list);
        Assert.Equal(3, products.Count);
        Assert.Equal(new NvidiaProduct("GeForce RTX 5090", 1066, 131), NvidiaDrivers.Find(products, "NVIDIA GeForce RTX 5090"));
        Assert.Equal(1007, NvidiaDrivers.Find(products, "NVIDIA GeForce RTX 4070 Laptop GPU")!.FamilyId);
        Assert.Null(NvidiaDrivers.Find(products, "NVIDIA RTX A6000"));
        Assert.Empty(NvidiaDrivers.ParseProducts("not xml"));
        Assert.Contains("psid=131&pfid=1066&osID=57", NvidiaDrivers.LookupUrl(products[1]));
        Assert.Contains("dch=1", NvidiaDrivers.LookupUrl(products[1]));

        const string json = """
            {"Success":"1","IDS":[{"downloadInfo":{"Success":"1","ID":"282412","Name":"GeForce%20Game%20Ready%20Driver","Version":"621.01",
             "ReleaseDateTime":"Wed Oct 21, 2026","DetailsURL":"https://www.nvidia.com/en-us/drivers/details/282412/",
             "DownloadURL":"https://us.download.nvidia.com/Windows/621.01/621.01-desktop-win10-win11-64bit-international-dch-whql.exe",
             "DownloadURLFileSize":"1.04 GB"}}]}
            """;
        var release = NvidiaDrivers.ParseRelease(json)!;
        Assert.Equal("621.01", release.Version);
        Assert.Equal("GeForce Game Ready Driver", release.Name);
        Assert.Equal(new DateTime(2026, 10, 21), release.Date);
        Assert.Equal("1.04 GB", release.SizeText);
        Assert.StartsWith("https://us.download.nvidia.com/", release.DownloadUrl);
        Assert.Null(NvidiaDrivers.ParseRelease("""{"Success":"0","IDS":[]}"""));
        Assert.Null(NvidiaDrivers.ParseRelease("""{"IDS":[{"downloadInfo":{"Version":"1.0","DownloadURL":"http://insecure"}}]}"""));
        Assert.Null(NvidiaDrivers.ParseRelease("{"));

        var card = Driver("NVIDIA GeForce RTX 5090", "Display", "NVIDIA", "32.0.15.8157");
        var update = NvidiaDrivers.AsUpdate(release, card)!;
        Assert.Equal("nvidia", update.Id);
        Assert.Equal(DriverSource.Nvidia, update.Source);
        Assert.Equal("581.57", update.CurrentVersion);
        Assert.Equal("621.01", update.NewVersion);
        Assert.True(update.BlanksScreen);
        Assert.Null(NvidiaDrivers.AsUpdate(release, Driver("NVIDIA GeForce RTX 5090", "Display", "NVIDIA", "32.0.16.2101"))); // already 621.01

        Assert.True(NvidiaDrivers.Outcome(0, "621.01").Success);
        Assert.True(NvidiaDrivers.Outcome(1, "621.01").RestartNeeded);
        Assert.False(NvidiaDrivers.Outcome(-522190823, "621.01").Success);
        Assert.Equal("-s -noreboot", NvidiaDrivers.SilentArguments);
    }

    [Fact]
    public void DirectX_MissingParts_BecomeAnUpdateFromMicrosoft()
    {
        Assert.Null(DirectXRuntime.AsUpdate(DirectXRuntime.Missing(_ => true)));
        var none = DirectXRuntime.AsUpdate(DirectXRuntime.Missing(_ => false))!;
        Assert.Equal("Not installed", none.CurrentVersion);
        Assert.Equal(DriverSource.DirectX, none.Source);
        Assert.StartsWith("https://download.microsoft.com/", none.DownloadUrl);
        var some = DirectXRuntime.Missing(file => !file.StartsWith("xinput", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["XInput 1.3 (controllers)"], some);
        Assert.Equal("1 of 6 parts missing", DirectXRuntime.AsUpdate(some)!.CurrentVersion);
    }

    private static UninstallKey Key(string name, bool machine = true, params (string Name, object? Value)[] values) =>
        new(name, values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase), machine);

    [Fact]
    public void InstalledApps_AreTheOnesSettingsLists_WithTheirUninstallers()
    {
        var app = InstalledApps.FromUninstallKey(Key("Discord", false,
            ("DisplayName", "Discord"), ("Publisher", "Discord Inc."), ("DisplayVersion", "1.0.9200"), ("InstallDate", "20260912"),
            ("EstimatedSize", 340_000), ("UninstallString", @"""C:\Users\me\AppData\Local\Discord\Update.exe"" --uninstall"),
            ("DisplayIcon", @"C:\Users\me\AppData\Local\Discord\app.ico"), ("InstallLocation", @"C:\Users\me\AppData\Local\Discord")))!;
        Assert.Equal("hkcu:Discord", app.Id);
        Assert.Equal(AppKind.Desktop, app.Kind);
        Assert.Equal(new DateTime(2026, 9, 12), app.Installed);
        Assert.Equal(340_000L * 1024, app.SizeBytes);
        Assert.Equal(@"C:\Users\me\AppData\Local\Discord\app.ico", app.IconPath);
        Assert.False(app.ForEveryone);
        Assert.True(app.CanUninstall);

        // Windows Installer: uninstalled by its product code.
        const string code = "{23170F69-40C1-2702-2501-000001000000}";
        var msi = InstalledApps.FromUninstallKey(Key(code, true, ("DisplayName", "7-Zip 25.01 (x64 edition)"), ("WindowsInstaller", 1), ("UninstallString", "MsiExec.exe /I" + code)))!;
        Assert.Equal("MsiExec.exe /X" + code, msi.Uninstall);

        // Not listed: system components, updates, parts of another app, nameless or without an uninstaller.
        Assert.Null(InstalledApps.FromUninstallKey(Key("a", true, ("DisplayName", "Runtime part"), ("SystemComponent", 1), ("UninstallString", "x.exe"))));
        Assert.Null(InstalledApps.FromUninstallKey(Key("b", true, ("DisplayName", "Security Update for X"), ("ReleaseType", "Security Update"), ("UninstallString", "x.exe"))));
        Assert.Null(InstalledApps.FromUninstallKey(Key("c", true, ("DisplayName", "Plug-in"), ("ParentKeyName", "Host"), ("UninstallString", "x.exe"))));
        Assert.Null(InstalledApps.FromUninstallKey(Key("d", true, ("UninstallString", "x.exe"))));
        Assert.Null(InstalledApps.FromUninstallKey(Key("e", true, ("DisplayName", "No uninstaller"))));
    }

    [Fact]
    public void Commands_Icons_Dates_AndDuplicates()
    {
        Assert.Equal((@"C:\Program Files\App\unins000.exe", "/SILENT"), InstalledApps.SplitCommand(@"""C:\Program Files\App\unins000.exe"" /SILENT"));
        Assert.Equal((@"C:\Program Files\Some App\uninstall.exe", "--remove"), InstalledApps.SplitCommand(@"C:\Program Files\Some App\uninstall.exe --remove"));
        Assert.Equal(("MsiExec.exe", "/X{GUID}"), InstalledApps.SplitCommand("MsiExec.exe /X{GUID}"));
        Assert.Equal(("rundll32", "foo.dll,Uninstall"), InstalledApps.SplitCommand("rundll32 foo.dll,Uninstall"));

        Assert.Equal(@"C:\App\app.exe", InstalledApps.IconFile(@"""C:\App\app.exe"",0"));
        Assert.Equal(@"C:\App\app.exe", InstalledApps.IconFile(@"C:\App\app.exe,-101"));
        Assert.Equal(@"C:\App\app.ico", InstalledApps.IconFile(@"C:\App\app.ico"));
        Assert.Null(InstalledApps.IconFile("  "));

        Assert.Equal(new DateTime(2025, 1, 15), InstalledApps.ParseInstallDate("20250115"));
        Assert.Null(InstalledApps.ParseInstallDate("garbage"));
        Assert.Null(InstalledApps.ParseInstallDate(null));

        var a = new InstalledApp("hklm:x", "Steam", AppKind.Desktop) { Version = "2.10", ForEveryone = true };
        var b = new InstalledApp("hklm:y", "steam", AppKind.Desktop) { Version = "2.10", SizeBytes = 5 };
        var c = new InstalledApp("store:z", "Steam", AppKind.Store) { Version = "2.10" };
        var kept = InstalledApps.Dedupe([a, b, c]);
        Assert.Equal(2, kept.Count);
        Assert.Contains(b, kept); // the one with a size
        Assert.Equal("1 app", InstalledApps.Apps(1));
    }

    [Fact]
    public void StartupEntries_BelongToTheAppWhoseFolderHoldsTheirProgram()
    {
        var apps = new[]
        {
            new InstalledApp("hkcu:Spotify", "Spotify", AppKind.Desktop) { InstallLocation = @"C:\Users\me\AppData\Roaming\Spotify" },
            new InstalledApp("hklm:Steam", "Steam", AppKind.Desktop) { InstallLocation = @"C:\Program Files (x86)\Steam\" },
            new InstalledApp("hklm:Teams", "Microsoft Teams", AppKind.Desktop),
        };
        StartupApp Entry(string name, string? exe) => new(name, name, exe ?? name, StartupScope.User, true) { Executable = exe };

        Assert.Equal("hkcu:Spotify", InstalledApps.ForStartup(apps, Entry("Spotify", @"C:\Users\me\AppData\Roaming\Spotify\Spotify.exe"))!.Id);
        Assert.Equal("hklm:Steam", InstalledApps.ForStartup(apps, Entry("Steam", @"c:\program files (x86)\steam\steam.exe"))!.Id);
        Assert.Equal("hklm:Teams", InstalledApps.ForStartup(apps, Entry("Microsoft Teams", null))!.Id); // by name
        Assert.Null(InstalledApps.ForStartup(apps, Entry("SecurityHealth", @"C:\Windows\System32\SecurityHealthSystray.exe")));
        Assert.Equal("hklm:Teams", InstalledApps.ForStartup(apps, Entry("Teams", null))!.Id); // "Microsoft Teams" ends with it
    }
}
