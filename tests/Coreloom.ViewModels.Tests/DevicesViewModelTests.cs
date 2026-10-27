using Coreloom.Core.Devices;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

internal sealed class FakeDevices : IDeviceService
{
    public List<BluetoothDeviceInfo> Bluetooth { get; } =
    [
        new("headphones", "WH-1000XM5", 0xA0E9DB123456, BluetoothTransport.Classic, DeviceKind.Headphones, false) { LastConnected = new DateTime(2026, 10, 13, 9, 0, 0) },
        new("mouse", "MX Master 3S", 0xC1C2C3C4C5C6, BluetoothTransport.LowEnergy, DeviceKind.Mouse, true) { Battery = 15 },
        new("pad", "Xbox Wireless Controller", 0x0C3526112233, BluetoothTransport.Classic, DeviceKind.Controller, true) { Battery = 80 },
    ];

    public List<WiredDevice> Wired { get; } =
    [
        new("k", "Corsair K70", DeviceKind.Keyboard, "USB") { Manufacturer = "Corsair" },
        new("d", "DELL U2723QE", DeviceKind.Display, "Display cable") { Problem = 28 },
        new("s", "SanDisk Ultra", DeviceKind.Storage, "USB") { EjectInstanceId = @"USB\VID_0781&PID_5581\4C53" },
    ];

    public BluetoothRadioInfo Radio { get; set; } = new(true, true, true);

    public bool ConnectWorks { get; set; } = true;

    public bool RadioSwitchWorks { get; set; } = true;

    public int BluetoothQueries { get; private set; }

    public bool IsSupported => true;

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken) => Task.FromResult(Radio);

    public Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken)
    {
        if (RadioSwitchWorks)
        {
            Radio = Radio with { IsOn = on };
        }

        return Task.FromResult(RadioSwitchWorks);
    }

    public Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken)
    {
        BluetoothQueries++;
        return Task.FromResult(BluetoothDeviceList.Merge(Bluetooth));
    }

    public Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken)
    {
        if (!ConnectWorks)
        {
            return Task.FromResult(new DeviceActionResult(false, $"Couldn't reach {device.Name}."));
        }

        Set(device.Id, true);
        return Task.FromResult(new DeviceActionResult(true, $"{device.Name} is connected."));
    }

    public Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken)
    {
        Set(device.Id, false);
        return Task.FromResult(new DeviceActionResult(true, $"{device.Name} is disconnected."));
    }

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WiredDevice>>(Wired.ToList());

    public Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken)
    {
        Wired.RemoveAll(d => d.Id == device.Id);
        return Task.FromResult(new DeviceActionResult(true, "You can unplug it now."));
    }

    public void OpenBluetoothSettings()
    {
    }

    public int AccessRequests { get; private set; }

    /// <summary>macOS's prompt, answered "Allow": the radio and devices appear.</summary>
    public Task<bool> RequestBluetoothAccessAsync(CancellationToken cancellationToken)
    {
        AccessRequests++;
        Radio = new BluetoothRadioInfo(true, true, true);
        return Task.FromResult(true);
    }

    private void Set(string id, bool connected)
    {
        int i = Bluetooth.FindIndex(d => d.Id == id);
        Bluetooth[i] = Bluetooth[i] with { IsConnected = connected };
    }
}

public class DevicesViewModelTests
{
    private static (DevicesViewModel Page, FakeDevices Fake, SettingsService Settings) Create()
    {
        var (monitor, settings, _) = Fixture.Create();
        var fake = new FakeDevices();
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Devices = fake },
        };
        var page = new DevicesViewModel(context) { Now = () => new DateTime(2026, 10, 14, 9, 0, 0) };
        page.Activate(monitor.Engine.LatestFrame);
        return (page, fake, settings);
    }

    [Fact]
    public void Opening_ListsBluetoothConnectedFirst_AndWiredByGroup()
    {
        var (page, _, settings) = Create();
        Assert.True(page.IsLoaded);
        Assert.Equal("Devices", page.Title);
        Assert.Equal("Bluetooth is on", page.RadioText);
        Assert.Equal("2 connected · 3 paired", page.BluetoothSummary);
        Assert.Equal<string>(new[] { "MX Master 3S", "Xbox Wireless Controller", "WH-1000XM5" }, page.BluetoothDevices.Select(d => d.Name).ToArray());

        var headphones = page.BluetoothDevices[2];
        Assert.Equal("Headphones", headphones.KindText);
        Assert.Equal("Icon.Headphones", headphones.IconKey);
        Assert.Equal("Last connected yesterday", headphones.StatusText);
        Assert.Equal("Connect", headphones.ActionText);
        Assert.Equal("A0:E9:DB:12:34:56", headphones.AddressText);
        Assert.False(headphones.HasBattery);

        var mouse = page.BluetoothDevices[0];
        Assert.Equal("Disconnect", mouse.ActionText);
        Assert.Equal("Bluetooth LE", mouse.TransportText);
        Assert.Equal(15, mouse.Battery, 6);
        Assert.Equal(ColorKeys.ToneWarm, mouse.BatteryColorKey);
        Assert.Equal("MX Master 3S: 15% battery left", page.LowBatteryText);

        Assert.Equal("3 devices plugged in", page.WiredSummary);
        Assert.Equal<string>(new[] { "Keyboards and mice", "Displays", "Storage" }, page.WiredGroups.Select(g => g.Title).ToArray());
        var dell = page.WiredGroups[1].Devices.Single();
        Assert.True(dell.HasProblem);
        Assert.Equal("Driver not installed", dell.ProblemText);
        Assert.Equal("Keyboard · Corsair · USB", page.WiredGroups[0].Devices[0].Detail);
        settings.Dispose();
    }

    [Fact]
    public async Task Bluetooth_IsAskedForOnlyWhenTheButtonIsClicked()
    {
        var (monitor, settings, _) = Fixture.Create();
        var fake = new FakeDevices { Radio = BluetoothRadioInfo.PermissionNeeded };
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settings,
            Timeline = new TimelineViewModel(monitor.Engine.History),
            Tools = new ToolServices { Devices = fake },
        };
        var page = new DevicesViewModel(context);
        page.Activate(monitor.Engine.LatestFrame);
        await page.RefreshAsync();

        Assert.True(page.NeedsBluetoothPermission);
        Assert.Equal("Bluetooth access needed", page.RadioText);
        Assert.Contains("once you allow it", page.BluetoothSummary);
        Assert.Equal(0, fake.AccessRequests);

        await page.AllowBluetoothCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.AccessRequests);
        Assert.False(page.NeedsBluetoothPermission);
        Assert.Equal("Bluetooth is on", page.RadioText);
    }

    [Fact]
    public async Task Connect_ReconnectsAndMovesTheCardUp()
    {
        var (page, _, settings) = Create();
        var headphones = page.BluetoothDevices.Single(d => d.Name == "WH-1000XM5");

        await headphones.ActionCommand.ExecuteAsync(null);

        Assert.True(headphones.IsConnected);
        Assert.Equal("Connected", headphones.StatusText);
        Assert.Equal("Disconnect", headphones.ActionText);
        Assert.Equal("WH-1000XM5 is connected.", headphones.Message);
        Assert.False(headphones.MessageIsError);
        Assert.Equal("3 connected · 3 paired", page.BluetoothSummary);
        Assert.Same(headphones, page.BluetoothDevices.Single(d => d.Name == "WH-1000XM5")); // the same card, not a new one

        await headphones.ActionCommand.ExecuteAsync(null);
        Assert.False(headphones.IsConnected);
        Assert.Equal("Connect", headphones.ActionText);
        settings.Dispose();
    }

    [Fact]
    public async Task FailedConnect_ExplainsWhy()
    {
        var (page, fake, settings) = Create();
        fake.ConnectWorks = false;
        var headphones = page.BluetoothDevices.Single(d => d.Name == "WH-1000XM5");

        await headphones.ActionCommand.ExecuteAsync(null);

        Assert.False(headphones.IsConnected);
        Assert.True(headphones.MessageIsError);
        Assert.Equal("Couldn't reach WH-1000XM5.", headphones.Message);
        Assert.False(headphones.IsBusy);
        settings.Dispose();
    }

    [Fact]
    public void SearchSuggestions_ListBestMatchesFromBothLists()
    {
        var (page, _, settings) = Create();
        Assert.Empty(page.SearchSuggestions);

        page.Search = "xbox ";
        Assert.Equal("xbox ", page.Search); // the trailing space stays while typing the next word
        var suggestion = Assert.Single(page.SearchSuggestions);
        Assert.Equal("Xbox Wireless Controller", suggestion.Title);
        Assert.StartsWith("Bluetooth · ", suggestion.Detail);
        Assert.Equal("Icon.Controller", suggestion.IconKey);

        page.Search = "dell";
        Assert.Equal("DELL U2723QE", Assert.Single(page.SearchSuggestions).Title);
        settings.Dispose();
    }

    [Fact]
    public async Task Search_FiltersBothLists()
    {
        var (page, _, settings) = Create();
        page.Search = "controller";
        Assert.Equal("Xbox Wireless Controller", Assert.Single(page.BluetoothDevices).Name);
        Assert.False(page.HasWiredDevices);
        Assert.Equal("No wired devices match your search.", page.WiredEmptyText);

        page.Search = "display";
        Assert.False(page.HasBluetoothDevices);
        Assert.Equal("DELL U2723QE", Assert.Single(Assert.Single(page.WiredGroups).Devices).Name);

        page.ClearSearchCommand.Execute(null);
        Assert.Equal(3, page.BluetoothDevices.Count);
        Assert.Equal(3, page.WiredGroups.Count);
        await Task.CompletedTask;
        settings.Dispose();
    }

    [Fact]
    public async Task RadioSwitch_AndEject()
    {
        var (page, fake, settings) = Create();
        page.IsRadioOn = false;
        await Task.Yield();
        Assert.False(page.IsRadioOn);
        Assert.Equal("Turn Bluetooth on to connect your devices.", page.BluetoothSummary);

        fake.RadioSwitchWorks = false;
        page.IsRadioOn = true;
        await Task.Yield();
        Assert.False(page.IsRadioOn); // the switch goes back
        Assert.NotNull(page.RadioMessage);

        var drive = page.WiredGroups.Single(g => g.Group == DeviceGroup.Storage).Devices.Single();
        Assert.True(drive.CanEject);
        await drive.EjectCommand.ExecuteAsync(null);
        Assert.Equal("You can unplug it now.", drive.Message);
        Assert.DoesNotContain(page.WiredGroups, g => g.Group == DeviceGroup.Storage);
        settings.Dispose();
    }

    [Fact]
    public void ChangesFromWindows_RefreshTheList()
    {
        var (page, fake, settings) = Create();
        int before = fake.BluetoothQueries;
        fake.Bluetooth.RemoveAt(2);
        fake.RaiseChanged();
        SpinWait.SpinUntil(() => fake.BluetoothQueries > before && page.BluetoothDevices.Count == 2, TimeSpan.FromSeconds(5));
        Assert.Equal(2, page.BluetoothDevices.Count);
        settings.Dispose();
    }

    [Fact]
    public async Task Messages_LeaveAfterFiveSeconds_AndALowBatteryWarnsOncePerCharge()
    {
        using var timer = new ManualDismissals();
        var (page, fake, settings) = Create();
        Assert.Equal("MX Master 3S: 15% battery left", page.LowBatteryText);
        timer.Elapse();
        Assert.Null(page.LowBatteryText);
        await page.RefreshAsync();
        Assert.Null(page.LowBatteryText); // still low: not again

        int i = fake.Bluetooth.FindIndex(d => d.Id == "mouse");
        fake.Bluetooth[i] = fake.Bluetooth[i] with { Battery = 90 };
        await page.RefreshAsync();
        fake.Bluetooth[i] = fake.Bluetooth[i] with { Battery = 12 };
        await page.RefreshAsync();
        Assert.Equal("MX Master 3S: 12% battery left", page.LowBatteryText); // charged, then low again: warns again

        fake.RadioSwitchWorks = false;
        page.IsRadioOn = false;
        await Task.Yield();
        Assert.NotNull(page.RadioMessage);
        timer.Elapse();
        Assert.Null(page.RadioMessage);
        Assert.Null(page.LowBatteryText);

        fake.ConnectWorks = false;
        var headphones = page.BluetoothDevices.Single(d => d.Id == "headphones");
        await headphones.ActionCommand.ExecuteAsync(null);
        Assert.Equal("Couldn't reach WH-1000XM5.", headphones.Message);
        Assert.True(headphones.MessageIsError);
        timer.Elapse();
        Assert.Null(headphones.Message);
        settings.Dispose();
    }

}
