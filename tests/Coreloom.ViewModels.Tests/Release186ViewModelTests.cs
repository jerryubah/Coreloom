using Coreloom.Core;
using Coreloom.Core.Apps;
using Coreloom.Core.Drivers;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

/// <summary>Drivers as a test PC has them, and what checking and installing answer.</summary>
internal sealed class FakeDrivers : IDriverService
{
    public List<InstalledDriver> Installed { get; } =
    [
        new(@"PCI\VEN_10DE", "NVIDIA GeForce RTX 5090", "Display", "NVIDIA", "32.0.15.8157", new DateTime(2026, 9, 30)),
        new(@"USB\VID_8087", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel Corporation", "22.200.0.2", new DateTime(2026, 3, 1)),
        new(@"HDAUDIO\1", "Realtek USB Audio", "MEDIA", "Realtek", "6.3.9600.2393", new DateTime(2025, 11, 4)),
        new(@"USB\ROOT_HUB30\1", "USB Root Hub (USB 3.0)", "USB", "Microsoft", "10.0.26100.1", new DateTime(2006, 6, 21)),
        new(@"USB\ROOT_HUB30\2", "USB Root Hub (USB 3.0)", "USB", "Microsoft", "10.0.26100.1", new DateTime(2006, 6, 21)),
        new(@"ACPI\1", "AMD GPIO Controller", "System", "Advanced Micro Devices, Inc", "2.2.0.130", new DateTime(2024, 1, 2)) { Problem = 10 },
    ];

    public List<DriverUpdate> Offers { get; } =
    [
        new("nvidia", "NVIDIA GeForce RTX 5090", DriverSource.Nvidia, DriverCategory.Graphics) { CurrentVersion = "581.57", NewVersion = "621.01", SizeText = "1.04 GB", DetailsUrl = "https://www.nvidia.com/en-us/drivers/details/282412/" },
        new("u1", "Intel(R) Wireless Bluetooth(R)", DriverSource.WindowsUpdate, DriverCategory.Bluetooth) { CurrentVersion = "22.200.0.2", NewVersion = "22.240.0.2", SizeBytes = 14_000_000 },
        new("directx", "DirectX End-User Runtime", DriverSource.DirectX, DriverCategory.Software) { CurrentVersion = "Not installed", NewVersion = "June 2010" },
    ];

    public List<string> Problems { get; } = [];

    public HashSet<string> Failing { get; } = [];

    public List<string> InstalledIds { get; } = [];

    public int Checks { get; private set; }

    public bool NvidiaAppInstalled { get; set; } = true;

    public List<string> Opened { get; } = [];

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledDriver>> InstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledDriver>>([.. Installed]);

    public Task<DriverScan> CheckAsync(IReadOnlyList<InstalledDriver> installed, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(new DriverScan([.. Offers], [.. Problems]));
    }

    public Task<DriverInstallResult> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        InstalledIds.Add(update.Id);
        return Task.FromResult(Failing.Contains(update.Id)
            ? new DriverInstallResult(false, "Windows Update couldn't install it.")
            : new DriverInstallResult(true, update.Source == DriverSource.Nvidia ? "NVIDIA 621.01 installed. Restart Windows to finish." : "Installed.", update.Source == DriverSource.Nvidia));
    }

    public bool OpenNvidiaApp()
    {
        Opened.Add("nvidia-app");
        return NvidiaAppInstalled;
    }

    public void OpenUrl(string url) => Opened.Add(url);
}

/// <summary>Installed apps on a test PC.</summary>
internal sealed class FakeApps : IAppService
{
    public List<InstalledApp> Apps { get; } =
    [
        new("hklm:Steam", "Steam", AppKind.Desktop) { Publisher = "Valve", Version = "2.10", SizeBytes = 900L << 20, InstallLocation = @"C:\Program Files (x86)\Steam", Uninstall = @"C:\Program Files (x86)\Steam\uninstall.exe", Installed = new DateTime(2024, 5, 1) },
        new("hkcu:Discord", "Discord", AppKind.Desktop) { Publisher = "Discord Inc.", SizeBytes = 330L << 20, Uninstall = "Update.exe --uninstall", Installed = new DateTime(2026, 9, 12) },
        new("hklm:Tool", "Old Tool", AppKind.Desktop) { Publisher = "Acme", InstallLocation = @"C:\Tools\Old", Uninstall = "unins000.exe", Installed = new DateTime(2020, 1, 1) },
        new("store:Spotify", "Spotify", AppKind.Store) { Publisher = "Spotify AB", InstallLocation = @"C:\Program Files\WindowsApps\Spotify", Uninstall = "SpotifyAB.SpotifyMusic_1.0_x64__zpdnekdrzrea0", Installed = new DateTime(2026, 6, 1) },
    ];

    public Dictionary<string, long> Measured { get; } = new() { ["hklm:Tool"] = 2L << 30, ["store:Spotify"] = 400L << 20 };

    public List<string> Uninstalled { get; } = [];

    public bool UninstallWorks { get; set; } = true;

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledApp>> InstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledApp>>([.. Apps]);

    public Task<IReadOnlyDictionary<string, long>> MeasureAsync(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(apps.Where(a => Measured.ContainsKey(a.Id)).ToDictionary(a => a.Id, a => Measured[a.Id]));

    public string? IconFor(InstalledApp app) => app.Kind == AppKind.Store ? @"C:\icons\spotify.png" : null;

    public Task<AppUninstallResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        Uninstalled.Add(app.Id);
        return Task.FromResult(UninstallWorks ? new AppUninstallResult(true, "Uninstalled.") : new AppUninstallResult(false, "It's still installed: its uninstaller was closed before it finished."));
    }

    public void OpenFolder(string path)
    {
    }
}

/// <summary>1.8.6: the Drivers and Applications pages (Windows only).</summary>
public class Release186ViewModelTests
{
    private static (PageContext Context, SettingsService Settings, FakeDrivers Drivers, FakeApps Apps, FakeOptimizer Optimizer) Context(HostPlatform platform = HostPlatform.Windows)
    {
        var (monitor, settings, _) = Fixture.Create();
        var drivers = new FakeDrivers();
        var apps = new FakeApps();
        var optimizer = new FakeOptimizer();
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Drivers = drivers, Apps = apps, Optimizer = optimizer },
            Platform = platform,
        };
        return (context, settings, drivers, apps, optimizer);
    }

    [Fact]
    public async Task Drivers_AreListedByGroup_WindowsOwnHiddenAtFirst_AndTheFirstVisitChecksForUpdates()
    {
        var (context, settings, fake, _, optimizer) = Context();
        var page = new DriversViewModel(context) { Now = () => new DateTime(2026, 10, 31, 19, 40, 0) };
        Assert.Equal("Check your drivers for updates", page.HeroTitle);
        Assert.Equal("Not checked yet", page.LastCheckedText);

        page.Activate(context.Monitor.Engine.LatestFrame);
        await WaitFor(() => fake.Checks == 1 && !page.IsChecking);
        Assert.Equal(["Graphics", "Audio", "Bluetooth", "Chipset and system"], page.Categories.Select(c => c.Label));
        Assert.Equal(6, page.DriverCount);
        Assert.Equal(4, page.MakerCount);
        Assert.Equal(1, page.ProblemCount);
        Assert.Equal("581.57", page.GraphicsDriverText);
        Assert.Equal("GeForce RTX 5090", page.GraphicsDriverCaption);
        Assert.True(page.HasNvidiaCard);
        Assert.Equal("581.57 (32.0.15.8157)", page.Categories[0].Rows[0].VersionText);
        Assert.Equal("2 of Windows' own drivers are hidden; they're updated with Windows.", page.HiddenText);

        page.ShowWindowsDrivers = true;
        var usb = page.Categories.Single(c => c.Label == "USB");
        Assert.Equal("×2", Assert.Single(usb.Rows).CountText);
        Assert.Null(page.HiddenText);

        page.Search = "realtek";
        Assert.Equal("Audio", Assert.Single(page.Categories).Label);
        Assert.Equal("Realtek USB Audio", Assert.Single(page.SearchSuggestions).Title);
        page.Search = "nothing like it";
        Assert.False(page.HasCategories);
        Assert.Equal("No drivers match your search.", page.EmptyText);
        page.Search = string.Empty;

        // The check: NVIDIA first, then Windows Update, then DirectX; every update picked.
        Assert.Equal(["nvidia", "u1", "directx"], page.Updates.Select(u => u.Update.Id));
        Assert.Equal("3 updates available", page.HeroTitle);
        Assert.Equal("Last checked today at " + new DateTime(2026, 10, 31, 19, 40, 0).ToString("t", System.Globalization.CultureInfo.CurrentCulture), page.LastCheckedText);
        Assert.True(page.SelectAll);
        Assert.Equal("Update 3 drivers", page.InstallText);
        var nvidia = page.Updates[0];
        Assert.Equal("581.57  →  621.01", nvidia.VersionText);
        Assert.Equal("From NVIDIA", nvidia.SourceText);
        Assert.True(nvidia.IsNvidia);
        Assert.Contains("1.04 GB", nvidia.DetailText);
        nvidia.OpenNotesCommand.Execute(null);
        Assert.Contains("https://www.nvidia.com/en-us/drivers/details/282412/", fake.Opened);
        Assert.Equal("From Windows Update", page.Updates[1].SourceText);
        Assert.Equal("DirectX", page.Updates[2].CategoryText);

        page.SelectAll = false;
        Assert.Equal(0, page.SelectedCount);
        Assert.False(page.InstallSelectedCommand.CanExecute(null));
        page.Updates[1].IsSelected = true;
        Assert.False(page.SelectAll);
        Assert.Equal("Update 1 driver", page.InstallText);
        settings.Dispose();
        optimizer.Dispose();
    }

    [Fact]
    public async Task OnlyThePickedUpdatesInstall_GraphicsLast_AndEachSaysHowItWent()
    {
        var (context, settings, fake, _, optimizer) = Context();
        fake.Failing.Add("directx");
        var page = new DriversViewModel(context);
        page.Activate(context.Monitor.Engine.LatestFrame);
        await WaitFor(() => fake.Checks == 1 && !page.IsChecking);

        page.Updates[1].IsSelected = false; // not the Bluetooth driver
        await page.InstallSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["directx", "nvidia"], fake.InstalledIds); // the graphics driver last: it blanks the screen
        var nvidia = page.Updates[0];
        Assert.True(nvidia.IsDone);
        Assert.False(nvidia.IsSelected);
        Assert.False(nvidia.CanSelect);
        Assert.Equal(ColorKeys.ToneCool, nvidia.StatusColorKey);
        Assert.Equal("Ready to install", page.Updates[1].StatusText);
        Assert.True(page.Updates[2].Failed);
        Assert.Equal(ColorKeys.ToneHot, page.Updates[2].StatusColorKey);
        Assert.Equal("Installed 1 of 2 updates; the others say what went wrong. Restart Windows to finish.", page.ResultText);
        Assert.Equal(2, page.PendingCount);
        Assert.Equal("2 updates available", page.HeroTitle);

        // Nothing new: up to date.
        fake.Offers.Clear();
        await page.CheckCommand.ExecuteAsync(null);
        Assert.False(page.HasUpdates);
        Assert.Equal("Your drivers are up to date", page.HeroTitle);
        Assert.Equal(1, page.Readiness);
        settings.Dispose();
        optimizer.Dispose();
    }

    [Fact]
    public async Task TheNvidiaApp_OpensOrNvidiasPagesDo_AndProblemsAreSaidPlainly()
    {
        var (context, settings, fake, _, optimizer) = Context();
        fake.Problems.Add("NVIDIA couldn't be reached to check for a newer graphics driver.");
        fake.Offers.RemoveAll(o => o.Id == "nvidia");
        var page = new DriversViewModel(context);
        page.Activate(context.Monitor.Engine.LatestFrame);
        await WaitFor(() => fake.Checks == 1 && !page.IsChecking);
        Assert.True(page.HasProblems);
        Assert.Contains("NVIDIA couldn't be reached", page.ProblemsText);

        page.OpenNvidiaAppCommand.Execute(null);
        Assert.Equal(["nvidia-app"], fake.Opened);
        fake.NvidiaAppInstalled = false;
        page.OpenNvidiaAppCommand.Execute(null);
        Assert.Equal(NvidiaDrivers.DriversPage, fake.Opened[^1]);
        settings.Dispose();
        optimizer.Dispose();
    }

    [Fact]
    public async Task Applications_ListsAppsBySize_MeasuresTheRest_AndFilters()
    {
        var (context, settings, _, fake, optimizer) = Context();
        var page = new ApplicationsViewModel(context);
        page.Activate(context.Monitor.Engine.LatestFrame);
        await WaitFor(() => page.IsLoaded && !page.IsMeasuring && page.Apps.Count > 0 && page.Apps[0].Name == "Old Tool");

        // Measured: the 2 GB tool first, then Steam, Spotify and Discord.
        Assert.Equal(["Old Tool", "Steam", "Spotify", "Discord"], page.Apps.Select(a => a.Name));
        Assert.Equal(1, page.Apps[0].SizeShare);
        Assert.Equal(4, page.AppCount);
        Assert.Equal(1, page.StoreCount);
        Assert.Equal(@"C:\icons\spotify.png", page.Apps.Single(a => a.Name == "Spotify").Icon);
        Assert.Equal("Microsoft Store", page.Apps.Single(a => a.Name == "Spotify").KindText);

        // Steam starts with Windows (its startup entry is matched by name); the Updater entry belongs to no app.
        var steam = page.Apps.Single(a => a.Name == "Steam");
        Assert.True(steam.HasStartup);
        Assert.True(steam.StartsWithWindows);
        Assert.Equal("1 of 2", page.StartupText);
        steam.StartsWithWindows = false;
        Assert.False(steam.StartsWithWindows);
        Assert.Equal("Won't start with Windows any more.", steam.Message);
        Assert.Equal("0 of 2", page.StartupText);

        page.Filter = AppFilter.Startup;
        Assert.Equal(["Steam", "Updater"], page.Apps.Select(a => a.Name).Order());
        Assert.Equal("Startup only", page.Apps.Single(a => a.Name == "Updater").KindText);
        Assert.False(page.Apps.Single(a => a.Name == "Updater").CanUninstall);
        page.Filter = AppFilter.Store;
        Assert.Equal("Spotify", Assert.Single(page.Apps).Name);
        page.Filter = AppFilter.All;

        page.Sort = AppSort.Name;
        Assert.Equal(["Discord", "Old Tool", "Spotify", "Steam"], page.Apps.Select(a => a.Name));
        page.Sort = AppSort.Installed;
        Assert.Equal("Discord", page.Apps[0].Name);
        page.Search = "valve";
        Assert.Equal("Steam", Assert.Single(page.Apps).Name);
        page.Search = "zzz";
        Assert.Equal("No apps match your search.", page.EmptyText);
        settings.Dispose();
        optimizer.Dispose();
    }

    [Fact]
    public async Task Uninstalling_AsksFirst_ThenRunsTheAppsOwnUninstaller()
    {
        var (context, settings, _, fake, optimizer) = Context();
        var page = new ApplicationsViewModel(context);
        page.Activate(context.Monitor.Engine.LatestFrame);
        await WaitFor(() => page.IsLoaded && !page.IsMeasuring);
        var discord = page.Apps.Single(a => a.Name == "Discord");

        discord.UninstallCommand.Execute(null);
        Assert.True(discord.IsConfirming);
        Assert.Contains("own uninstaller", discord.ConfirmText);
        Assert.Empty(fake.Uninstalled); // nothing yet
        discord.CancelUninstallCommand.Execute(null);
        Assert.False(discord.IsConfirming);

        discord.UninstallCommand.Execute(null);
        await discord.ConfirmUninstallCommand.ExecuteAsync(null);
        Assert.Equal(["hkcu:Discord"], fake.Uninstalled);
        Assert.True(discord.IsRemoved);
        Assert.False(discord.CanUninstall);
        Assert.Equal("Uninstalled.", discord.Message);
        Assert.Equal(3, page.AppCount);

        fake.UninstallWorks = false;
        var steam = page.Apps.Single(a => a.Name == "Steam");
        steam.UninstallCommand.Execute(null);
        await steam.ConfirmUninstallCommand.ExecuteAsync(null);
        Assert.False(steam.IsRemoved);
        Assert.StartsWith("It's still installed", steam.Message);
        Assert.Contains("Windows removes the app", page.Apps.Single(a => a.Name == "Spotify").ConfirmText);
        settings.Dispose();
        optimizer.Dispose();
    }

    [Fact]
    public void WithoutWindowsServices_ThePagesSaySo()
    {
        var (context, settings, _, _, optimizer) = Context(HostPlatform.MacOS);
        var bare = new PageContext { Monitor = context.Monitor, Settings = context.Settings, Timeline = context.Timeline, Platform = HostPlatform.MacOS };
        var drivers = new DriversViewModel(bare);
        var apps = new ApplicationsViewModel(bare);
        Assert.False(drivers.IsSupported);
        Assert.False(apps.IsSupported);
        Assert.False(drivers.CheckCommand.CanExecute(null));
        Assert.Contains("Windows' device tree", drivers.UnsupportedText);
        settings.Dispose();
        optimizer.Dispose();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the condition.");
    }
}
