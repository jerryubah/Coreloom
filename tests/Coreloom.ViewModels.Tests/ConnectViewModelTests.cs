using System.Net;
using System.Text.Json;
using Coreloom.Core.Connect;
using Coreloom.ViewModels.Connect;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public sealed class ConnectViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-connect-vm-" + Guid.NewGuid().ToString("N"));
    private readonly ConfirmingShell _shell = new();
    private ConnectHost? _host;

    public void Dispose()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private async Task<(ConnectViewModel ViewModel, ConnectHost Host, SettingsService Settings)> CreateAsync(bool enabled, bool withNetwork = true)
    {
        var (monitor, settings, _) = Fixture.Create(_dir);
        settings.Current.Connect.Enabled = enabled;
        settings.Current.Connect.Port = 0; // any free port
        var context = Fixture.Context(monitor, settings);
        _host = new ConnectHost(context, new ConnectEnvironment
        {
            DataDirectory = _dir,
            Assets = new WebAssets(new Dictionary<string, byte[]> { ["index.html"] = "<!doctype html>"u8.ToArray() }),
            FindAddresses = () => withNetwork
                ? [new LocalAddress(IPAddress.Parse("192.168.1.24"), "Wi-Fi", true), new LocalAddress(IPAddress.Parse("100.84.12.7"), "VPN", false)]
                : [],
        }, "1.7.0");
        await _host.ApplyAsync();
        return (new ConnectViewModel(_host, settings, _shell, isMac: false), _host, settings);
    }

    [Fact]
    public async Task OffSaysSoAndShowsNoCode()
    {
        var (vm, host, _) = await CreateAsync(enabled: false);
        vm.SetVisible(true);

        Assert.Equal(ConnectStatus.Off, host.Status);
        Assert.False(vm.IsEnabled);
        Assert.Equal("Connect is off", vm.HeadlineText);
        Assert.Contains("this PC's readings", vm.DetailText);
        Assert.Equal(string.Empty, vm.StatusTone);
        Assert.False(vm.IsPairingOpen);
        Assert.Equal(string.Empty, vm.QrPath);
        Assert.False(host.Pairing.IsOpen);
    }

    [Fact]
    public async Task OnShowsTheQrCodeAndCodeOnlyWhileTheSectionIsShowing()
    {
        var (vm, host, _) = await CreateAsync(enabled: true);
        Assert.Equal(ConnectStatus.On, host.Status);
        Assert.False(host.Pairing.IsOpen); // not until Settings › Connect is on screen
        Assert.False(vm.IsPairingOpen);

        vm.SetVisible(true);
        Assert.True(host.Pairing.IsOpen);
        Assert.True(vm.IsPairingOpen);
        Assert.Equal($"http://192.168.1.24:{host.Port}", vm.Url);
        Assert.Equal($"192.168.1.24:{host.Port}", vm.DisplayAddress);
        Assert.Equal("Ready for your phone", vm.HeadlineText);
        Assert.Equal($"Phones on Wi-Fi reach this PC at 192.168.1.24:{host.Port}.", vm.DetailText);
        Assert.Equal("Accent", vm.StatusTone);
        Assert.Matches("^[0-9]{3} [0-9]{3}$", vm.PairingCode);
        Assert.Equal(host.Pairing.Current!.Code, vm.PairingCode.Replace(" ", string.Empty));
        Assert.Matches("^New code in [45]:[0-5][0-9]$", vm.RenewsText);
        Assert.Equal($"{vm.Url}/#p={host.Pairing.Current.LinkSecret}", vm.QrText);
        Assert.StartsWith("M", vm.QrPath);
        Assert.Equal(QrCode.Encode(vm.QrText).Size + 4, vm.QrSize);
        Assert.Equal([$"100.84.12.7:{host.Port} (VPN)"], vm.OtherAddresses);

        string code = vm.PairingCode;
        vm.RenewCommand.Execute(null);
        Assert.NotEqual(code + host.Pairing.Current!.LinkSecret, vm.PairingCode + vm.QrText);

        vm.SetVisible(false);
        Assert.False(host.Pairing.IsOpen);
        Assert.False(vm.IsPairingOpen);
    }

    [Fact]
    public async Task NamesTheNetworkAsTheNetworkPageDoes()
    {
        var (monitor, settings, _) = Fixture.Create(_dir);
        settings.Current.Connect.Enabled = true;
        settings.Current.Connect.Port = 0;
        // The Network readings come in after Connect starts with the app.
        IReadOnlyList<(string Key, string Name, bool IsActive)> known = [];
        var mac = new MonitorContext
        {
            Engine = monitor.Engine,
            Summary = monitor.Summary,
            Processes = monitor.Processes,
            PrimaryGpuSlot = monitor.PrimaryGpuSlot,
            Gpus = monitor.Gpus,
            NetworkAdapters = () => known,
            LibreHardwareMonitorIncluded = false,
        };
        _host = new ConnectHost(Fixture.Context(mac, settings), new ConnectEnvironment
        {
            DataDirectory = _dir,
            Assets = new WebAssets(new Dictionary<string, byte[]> { ["index.html"] = "<!doctype html>"u8.ToArray() }),
            FindAddresses = () => [new LocalAddress(IPAddress.Parse("192.168.1.30"), "en0", true), new LocalAddress(IPAddress.Parse("10.0.0.2"), "utun3", false)],
        }, "1.7.0");
        await _host.ApplyAsync();
        var vm = new ConnectViewModel(_host, settings, _shell, isMac: true);
        Assert.Equal("en0", vm.AdapterText);

        known = [("en0", "Wi-Fi", true), ("en5", "Thunderbolt Bridge", false)];
        vm.SetVisible(true);
        Assert.Equal("Wi-Fi", vm.AdapterText);
        Assert.Equal($"Phones on Wi-Fi reach this Mac at 192.168.1.30:{_host.Port}.", vm.DetailText);
        Assert.Equal([$"10.0.0.2:{_host.Port} (utun3)"], vm.OtherAddresses);
    }

    [Fact]
    public async Task WithoutANetworkItAsksForOne()
    {
        var (vm, _, _) = await CreateAsync(enabled: true, withNetwork: false);
        vm.SetVisible(true);
        Assert.True(vm.NeedsNetwork);
        Assert.False(vm.IsPairingOpen);
        Assert.Equal("Waiting for a network", vm.HeadlineText);
    }

    [Fact]
    public async Task TheSwitchTurnsConnectOnAndOff()
    {
        var (vm, host, settings) = await CreateAsync(enabled: false);
        vm.IsEnabled = true;
        Assert.True(settings.Current.Connect.Enabled);
        await host.ApplyAsync(); // what the shell does when the setting changes
        vm.SetVisible(true);
        Assert.True(vm.IsOn);
        Assert.True(vm.IsPairingOpen);

        vm.IsEnabled = false;
        await host.ApplyAsync();
        vm.Tick();
        Assert.Equal(ConnectStatus.Off, host.Status);
        Assert.False(host.Pairing.IsOpen);
    }

    [Fact]
    public async Task ListsPairedPhonesAndRemovesThem()
    {
        var (vm, host, _) = await CreateAsync(enabled: true);
        var now = DateTimeOffset.UtcNow;
        var iphone = host.Store.Add("Jerry's iPhone", "qr", now.AddDays(-1));
        host.Store.Add("iPad", "code", now.AddDays(-3));
        host.Store.Touch(iphone.Id, now.AddMinutes(-4));
        vm.SetVisible(true);

        Assert.True(vm.HasDevices);
        Assert.Equal("2 paired phones", vm.DevicesTitle);
        Assert.Equal(["Jerry's iPhone", "iPad"], vm.Devices.Select(d => d.Name));
        Assert.Equal("Last connected 4 min ago", vm.Devices[0].StatusText);
        Assert.False(vm.Devices[0].IsConnected);

        _shell.Answer = false;
        vm.Devices[1].RemoveCommand.Execute(null);
        Assert.Equal(2, host.Store.Devices.Count);
        Assert.Contains("Remove iPad?", _shell.Questions);

        _shell.Answer = true;
        vm.Devices[1].RemoveCommand.Execute(null);
        Assert.Single(host.Store.Devices);
        Assert.Equal("1 paired phone", vm.DevicesTitle);

        vm.RemoveAllCommand.Execute(null);
        Assert.Empty(host.Store.Devices);
        Assert.False(vm.HasDevices);
        Assert.False(vm.RemoveAllCommand.CanExecute(null));
    }

    [Fact]
    public void SaysWhenTheyWereLastSeen()
    {
        var when = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", ConnectViewModel.Ago(TimeSpan.FromSeconds(20), when));
        Assert.Equal("12 min ago", ConnectViewModel.Ago(TimeSpan.FromMinutes(12.5), when));
        Assert.Equal("an hour ago", ConnectViewModel.Ago(TimeSpan.FromMinutes(70), when));
        Assert.Equal("5 hours ago", ConnectViewModel.Ago(TimeSpan.FromHours(5), when));
        Assert.Equal(when.ToLocalTime().ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture), ConnectViewModel.Ago(TimeSpan.FromDays(3), when));
    }

    [Fact]
    public void ThePhonesReadingsAreWhatTheAppShows()
    {
        var (monitor, settings, _) = Fixture.Create(_dir);
        var feed = new RemoteFeed(Fixture.Context(monitor, settings));
        using var json = JsonDocument.Parse(feed.Build(monitor.Engine.LatestFrame));
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal("windows", root.GetProperty("pc").GetProperty("platform").GetString());
        Assert.Equal("GeForce RTX 5090", root.GetProperty("gpu").GetProperty("name").GetString());
        var gpuMetrics = root.GetProperty("gpu").GetProperty("metrics").EnumerateArray().ToList();
        var temperature = gpuMetrics.Single(m => m.GetProperty("id").GetString() == Coreloom.Core.Telemetry.MetricIds.GpuTemperature(0));
        Assert.Equal(38, temperature.GetProperty("v").GetDouble(), 3);
        Assert.Equal("38", temperature.GetProperty("n").GetString());
        Assert.Equal("°C", temperature.GetProperty("u").GetString());
        Assert.Equal("24.0 GB", root.GetProperty("memory").GetProperty("usedText").GetString()!.Replace("GiB", "GB"));
        Assert.True(root.GetProperty("health").TryGetProperty("key", out _));
        Assert.NotEmpty(root.GetProperty("overview").GetProperty("cards").EnumerateArray());

        settings.Dispose();
    }

    private sealed class ConfirmingShell : IShellService
    {
        public bool Answer { get; set; }

        public List<string> Questions { get; } = [];

        public void OpenUrl(string url)
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void OpenFile(string path)
        {
        }

        public void CopyToClipboard(string text)
        {
        }

        public string? PickSaveFile(string suggestedName, string filter) => null;

        public void ShowMessage(string title, string message)
        {
        }

        public bool Confirm(string title, string message)
        {
            Questions.Add(title);
            return Answer;
        }
    }
}
