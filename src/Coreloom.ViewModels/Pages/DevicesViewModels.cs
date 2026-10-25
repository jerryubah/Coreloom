using Coreloom.Core;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Devices;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>A card for one paired Bluetooth device: what it is, whether it's connected, its battery and Connect/Disconnect.</summary>
public sealed class BluetoothDeviceViewModel : ObservableObject
{
    private readonly Func<BluetoothDeviceViewModel, Task> _toggle;
    private readonly Func<DateTime> _now;
    private BluetoothDeviceInfo _info;
    private bool _isBusy;
    private string? _message;
    private bool _messageIsError;
    private bool _isMatch = true;

    public BluetoothDeviceViewModel(BluetoothDeviceInfo info, Func<BluetoothDeviceViewModel, Task> toggle, Func<DateTime> now)
    {
        _info = info;
        _toggle = toggle;
        _now = now;
        ActionCommand = new AsyncRelayCommand(() => _toggle(this), () => !IsBusy);
    }

    public BluetoothDeviceInfo Info => _info;

    public string Id => _info.Id;

    public string Name => _info.Name;

    public string KindText => DeviceKinds.Text(_info.Kind);

    public string IconKey => DeviceKinds.IconKey(_info.Kind);

    public string TransportText => _info.Transport == BluetoothTransport.LowEnergy ? "Bluetooth LE" : "Bluetooth";

    public string AddressText => BluetoothAddresses.Format(_info.Address);

    public bool IsConnected => _info.IsConnected;

    /// <summary>"Connected", "Last connected yesterday", "Not connected".</summary>
    public string StatusText => _info.IsConnected ? "Connected"
        : _info.LastConnected is { } last ? $"Last connected {OptimizeText.Ago(last, _now())}"
        : "Not connected";

    /// <summary>"Connected", "Disconnected" or "Busy" (templates pick the dot's color from it).</summary>
    public string StatusKey => _isBusy ? "Busy" : _info.IsConnected ? "Connected" : "Disconnected";

    public bool HasBattery => _info.Battery is not null;

    /// <summary>Battery in percent (NaN without a reading), for the number that counts smoothly.</summary>
    public double Battery => _info.Battery ?? double.NaN;

    public double BatteryFraction => (_info.Battery ?? 0) / 100.0;

    public string BatteryColorKey => _info.Battery is int level ? ColorKeys.ForLevel(BatteryLevels.Level(level)) : ColorKeys.Neutral;

    public Func<double, string> BatteryFormatter { get; } = v => Math.Round(v).ToString("0", CultureInfo.CurrentCulture);

    public bool IsLowBattery => _info.IsConnected && _info.Battery is int level && BatteryLevels.IsLow(level);

    public IAsyncRelayCommand ActionCommand { get; }

    /// <summary>"Connect", "Disconnect", "Connecting…", "Disconnecting…".</summary>
    public string ActionText => _isBusy ? (_info.IsConnected ? "Disconnecting…" : "Connecting…") : _info.IsConnected ? "Disconnect" : "Connect";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(StatusKey));
                ActionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>How the last Connect or Disconnect went (kept while it matters).</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool MessageIsError
    {
        get => _messageIsError;
        private set => SetProperty(ref _messageIsError, value);
    }

    /// <summary>Matches the search box.</summary>
    public bool IsMatch
    {
        get => _isMatch;
        set => SetProperty(ref _isMatch, value);
    }

    public void ShowMessage(string? text, bool isError)
    {
        Message = string.IsNullOrWhiteSpace(text) ? null : text;
        MessageIsError = isError;
        if (!isError && Message is { } shown)
        {
            _ = ClearLaterAsync(shown);
        }
    }

    /// <summary>Takes a fresh reading; only what changed is announced.</summary>
    public void Update(BluetoothDeviceInfo info)
    {
        if (info == _info)
        {
            OnPropertyChanged(nameof(StatusText)); // "last connected 3 minutes ago" moves on
            return;
        }

        bool reconnected = info.IsConnected != _info.IsConnected;
        _info = info;
        OnPropertyChanged(string.Empty);
        if (reconnected && _messageIsError)
        {
            Message = null; // "couldn't reach it" no longer applies
        }
    }

    public bool Matches(string search) =>
        search.Length == 0
        || Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || KindText.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    private async Task ClearLaterAsync(string shown)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (Message == shown)
        {
            Message = null;
        }
    }
}

/// <summary>A device plugged in by wire, with "Safely remove" for drives.</summary>
public sealed class WiredDeviceViewModel : ObservableObject
{
    private WiredDevice _device;
    private bool _isBusy;
    private string? _message;
    private bool _messageIsError;

    public WiredDeviceViewModel(WiredDevice device, Func<WiredDeviceViewModel, Task> eject)
    {
        _device = device;
        EjectCommand = new AsyncRelayCommand(() => eject(this), () => !IsBusy && CanEject);
    }

    public WiredDevice Device => _device;

    public string Id => _device.Id;

    public string Name => _device.Name;

    public string KindText => DeviceKinds.Text(_device.Kind);

    public string IconKey => DeviceKinds.IconKey(_device.Kind);

    /// <summary>"Keyboard · Logitech · USB".</summary>
    public string Detail => string.Join(" · ", new[] { KindText, _device.Manufacturer, _device.Connection }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool HasProblem => _device.Problem != 0;

    public string? ProblemText => _device.ProblemText;

    public bool CanEject => _device.CanEject;

    public IAsyncRelayCommand EjectCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                EjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool MessageIsError
    {
        get => _messageIsError;
        private set => SetProperty(ref _messageIsError, value);
    }

    public void ShowMessage(string? text, bool isError)
    {
        Message = string.IsNullOrWhiteSpace(text) ? null : text;
        MessageIsError = isError;
    }

    public void Update(WiredDevice device)
    {
        if (device != _device)
        {
            _device = device;
            OnPropertyChanged(string.Empty);
            EjectCommand.NotifyCanExecuteChanged();
        }
    }

    public bool Matches(string search) =>
        search.Length == 0
        || Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || KindText.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>A group of wired devices ("Keyboards and mice", "Displays"…).</summary>
public sealed class DeviceGroupViewModel : ObservableObject
{
    public DeviceGroupViewModel(DeviceGroup group)
    {
        Group = group;
    }

    public DeviceGroup Group { get; }

    public string Title => DeviceKinds.GroupText(Group);

    public string IconKey => DeviceKinds.GroupIconKey(Group);

    public ObservableCollection<WiredDeviceViewModel> Devices { get; } = [];

    public string CountText => Devices.Count.ToString(CultureInfo.CurrentCulture);

    internal void CountChanged() => OnPropertyChanged(nameof(CountText));
}

/// <summary>
/// The Devices page: the Bluetooth adapter and every paired Bluetooth device as a card with Connect or Disconnect, and the
/// devices plugged in by wire, grouped by type. Readings refresh every few seconds while the page is open and right away
/// when Windows reports a device connecting or disconnecting.
/// </summary>
public sealed class DevicesViewModel : PageViewModel
{
    private static readonly TimeSpan BluetoothInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WiredInterval = TimeSpan.FromSeconds(15);

    private readonly IDeviceService _service;
    private readonly SynchronizationContext? _sync;
    private readonly List<BluetoothDeviceViewModel> _bluetooth = [];
    private readonly List<WiredDeviceViewModel> _wired = [];
    private readonly Dictionary<DeviceGroup, DeviceGroupViewModel> _groups = [];
    private DateTime _lastBluetooth = DateTime.MinValue;
    private DateTime _lastWired = DateTime.MinValue;
    private bool _bluetoothBusy;
    private bool _bluetoothAgain;
    private bool _wiredBusy;
    private bool _changePending;
    private BluetoothRadioInfo _radio = BluetoothRadioInfo.None;
    private bool _isRadioBusy;
    private string? _radioMessage;
    private bool _isLoaded;
    private string _search = string.Empty;      // trimmed: what the list is filtered by
    private string _searchText = string.Empty;  // as typed (a trailing space stays while typing the next word)

    public DevicesViewModel(PageContext context)
        : base("devices", "Devices", "Icon.Devices", context)
    {
        _service = context.Tools.Devices;
        _sync = SynchronizationContext.Current;
        _service.Changed += OnServiceChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PairCommand = new RelayCommand(_service.OpenBluetoothSettings);
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty);
        AllowBluetoothCommand = new AsyncRelayCommand(AllowBluetoothAsync);
    }

    /// <summary>For tests: the clock "last connected" texts use.</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    public bool IsSupported => _service.IsSupported;

    public string UnsupportedText => IsMac
        ? "Device information comes from macOS's Bluetooth and I/O services, which aren't available here."
        : "Device information comes from Windows' Bluetooth and Plug and Play services, which aren't available here.";

    /// <summary>The paired Bluetooth devices that match the search, connected first.</summary>
    public ObservableCollection<BluetoothDeviceViewModel> BluetoothDevices { get; } = [];

    /// <summary>The wired devices that match the search, by group.</summary>
    public ObservableCollection<DeviceGroupViewModel> WiredGroups { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand PairCommand { get; }

    /// <summary>macOS: asks for Bluetooth access (only when clicked), then lists the devices.</summary>
    public IAsyncRelayCommand AllowBluetoothCommand { get; }

    /// <summary>macOS hasn't asked yet whether Coreloom may use Bluetooth: the page offers to ask.</summary>
    public bool NeedsBluetoothPermission => _radio.NeedsPermission;

    public IRelayCommand ClearSearchCommand { get; }

    /// <summary>The first readings are in (until then the page shows "Looking for devices…").</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public bool HasRadio => _radio.IsPresent;

    /// <summary>The Bluetooth switch. Turning it changes Windows' Bluetooth setting.</summary>
    public bool IsRadioOn
    {
        get => _radio.IsOn;
        set
        {
            if (value != _radio.IsOn && !_isRadioBusy)
            {
                _ = SetRadioAsync(value);
            }
        }
    }

    public bool CanToggleRadio => _radio.IsPresent && _radio.CanToggle && !_isRadioBusy;

    public bool IsRadioBusy
    {
        get => _isRadioBusy;
        private set
        {
            if (SetProperty(ref _isRadioBusy, value))
            {
                OnPropertyChanged(nameof(CanToggleRadio));
            }
        }
    }

    /// <summary>"Bluetooth is on", "Bluetooth is off", "No Bluetooth adapter".</summary>
    public string RadioText => _radio.NeedsPermission ? "Bluetooth access needed"
        : !_radio.IsPresent ? "No Bluetooth adapter" : _radio.IsOn ? "Bluetooth is on" : "Bluetooth is off";

    /// <summary>Why the switch didn't work, when it didn't.</summary>
    public string? RadioMessage
    {
        get => _radioMessage;
        private set => SetProperty(ref _radioMessage, value);
    }

    public int ConnectedCount => _bluetooth.Count(d => d.IsConnected);

    public int PairedCount => _bluetooth.Count;

    public int WiredCount => _wired.Count;

    /// <summary>"2 connected · 6 paired".</summary>
    public string BluetoothSummary => _radio.NeedsPermission
            ? "Coreloom lists your Bluetooth accessories and their batteries once you allow it. macOS asks once."
        : !_radio.IsPresent && _bluetooth.Count == 0
            ? IsMac ? "This Mac has no Bluetooth, or macOS isn't sharing it with Coreloom (System Settings › Privacy & Security › Bluetooth)." : "This PC has no Bluetooth adapter, or it's turned off in Device Manager."
        : !_radio.IsOn && _radio.IsPresent ? "Turn Bluetooth on to connect your devices."
        : _bluetooth.Count == 0 ? "No Bluetooth devices are paired yet."
        : $"{ConnectedCount.ToString(CultureInfo.CurrentCulture)} connected · {PairedCount.ToString(CultureInfo.CurrentCulture)} paired";

    public string WiredSummary => _wired.Count == 0 ? (IsMac ? "Nothing plugged in by USB, Thunderbolt or display cable." : "Nothing plugged in by USB or display cable.")
        : _wired.Count == 1 ? "1 device plugged in" : $"{_wired.Count.ToString(CultureInfo.CurrentCulture)} devices plugged in";

    /// <summary>"Galaxy Buds2 Pro: 12% battery left", when a connected device runs low.</summary>
    public string? LowBatteryText => _bluetooth.Where(d => d.IsLowBattery).Select(d => $"{d.Name}: {d.Info.Battery!.Value.ToString(CultureInfo.CurrentCulture)}% battery left").FirstOrDefault();

    public bool HasBluetoothDevices => BluetoothDevices.Count > 0;

    public bool HasWiredDevices => WiredGroups.Count > 0;

    /// <summary>What to say where no Bluetooth card shows.</summary>
    public string BluetoothEmptyText => _search.Length > 0 && _bluetooth.Count > 0 ? "No Bluetooth devices match your search." : "Pair a device to see it here.";

    public string WiredEmptyText => _search.Length > 0 && _wired.Count > 0 ? "No wired devices match your search." : "Plug in a USB device or a display to see it here.";

    /// <summary>The best few matches for the search, listed under the search box while typing.</summary>
    public IReadOnlyList<SearchSuggestion> SearchSuggestions =>
        Infrastructure.SearchSuggestions.Rank(
            _bluetooth.Cast<object>().Concat(_wired),
            _search,
            item => item is BluetoothDeviceViewModel b ? b.Name : ((WiredDeviceViewModel)item).Name,
            item => item is BluetoothDeviceViewModel b ? b.Matches(_search) : ((WiredDeviceViewModel)item).Matches(_search),
            item => item switch
            {
                BluetoothDeviceViewModel b => new SearchSuggestion(b.Name, $"Bluetooth · {b.KindText} · {(b.IsConnected ? "Connected" : "Not connected")}", b.IconKey),
                WiredDeviceViewModel w => new SearchSuggestion(w.Name, w.Detail, w.IconKey),
                _ => new SearchSuggestion(item.ToString() ?? string.Empty, string.Empty, "Icon.Devices"),
            });

    public string Search
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty) && _searchText.Trim() != _search)
            {
                _search = _searchText.Trim();
                ApplyBluetooth();
                ApplyWired();
            }
        }
    }

    /// <summary>Refreshes everything now.</summary>
    public async Task RefreshAsync()
    {
        await Task.WhenAll(RefreshBluetoothAsync(), RefreshWiredAsync());
        IsLoaded = true;
    }

    protected override void OnActivated()
    {
        if (IsSupported)
        {
            _ = RefreshAsync();
        }
        else
        {
            IsLoaded = true;
        }
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (!IsSupported)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastBluetooth >= BluetoothInterval)
        {
            _ = RefreshBluetoothAsync();
        }

        if (now - _lastWired >= WiredInterval)
        {
            _ = RefreshWiredAsync();
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        if (_sync is null)
        {
            OnChanged();
        }
        else
        {
            _sync.Post(_ => OnChanged(), null);
        }
    }

    /// <summary>A device connected or disconnected: refresh shortly (several reports in a row become one refresh).</summary>
    private async void OnChanged()
    {
        if (!IsActive || _changePending)
        {
            return;
        }

        _changePending = true;
        try
        {
            await Task.Delay(350);
            await RefreshBluetoothAsync();
        }
        finally
        {
            _changePending = false;
        }
    }

    private async Task AllowBluetoothAsync()
    {
        try
        {
            await _service.RequestBluetoothAccessAsync(CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            // the radio card says what it can
        }

        await RefreshBluetoothAsync();
    }

    private async Task RefreshBluetoothAsync()
    {
        if (_bluetoothBusy)
        {
            _bluetoothAgain = true; // once the running refresh is done
            return;
        }

        _bluetoothBusy = true;
        try
        {
            do
            {
                _bluetoothAgain = false;
                _lastBluetooth = DateTime.UtcNow;
                try
                {
                    var radio = _service.RadioAsync(CancellationToken.None);
                    var devices = _service.BluetoothDevicesAsync(CancellationToken.None);
                    ApplyRadio(await radio);
                    Apply(await devices);
                }
                catch (Exception e) when (IsExpected(e))
                {
                    // shown as before; the next refresh tries again
                }
            }
            while (_bluetoothAgain);
        }
        finally
        {
            _bluetoothBusy = false;
        }
    }

    private async Task RefreshWiredAsync()
    {
        if (_wiredBusy)
        {
            return;
        }

        _wiredBusy = true;
        _lastWired = DateTime.UtcNow;
        try
        {
            Apply(await _service.WiredDevicesAsync(CancellationToken.None));
        }
        catch (Exception e) when (IsExpected(e))
        {
            // shown as before
        }
        finally
        {
            _wiredBusy = false;
        }
    }

    private static bool IsExpected(Exception e) =>
        e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException or ArgumentException;

    private void ApplyRadio(BluetoothRadioInfo radio)
    {
        if (radio == _radio)
        {
            return;
        }

        _radio = radio;
        RadioChanged();
    }

    private void RadioChanged()
    {
        OnPropertyChanged(nameof(HasRadio));
        OnPropertyChanged(nameof(NeedsBluetoothPermission));
        OnPropertyChanged(nameof(IsRadioOn));
        OnPropertyChanged(nameof(CanToggleRadio));
        OnPropertyChanged(nameof(RadioText));
        OnPropertyChanged(nameof(BluetoothSummary));
    }

    private void Apply(IReadOnlyList<BluetoothDeviceInfo> devices)
    {
        var byId = _bluetooth.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var next = new List<BluetoothDeviceViewModel>(devices.Count);
        foreach (var info in devices)
        {
            if (byId.Remove(info.Id, out var existing))
            {
                existing.Update(info);
                next.Add(existing);
            }
            else
            {
                next.Add(new BluetoothDeviceViewModel(info, ToggleAsync, Now));
            }
        }

        // A device being connected or disconnected stays on screen even if Windows briefly stops listing it.
        next.AddRange(byId.Values.Where(d => d.IsBusy));
        _bluetooth.Clear();
        _bluetooth.AddRange(next
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase));
        ApplyBluetooth();
    }

    private void ApplyBluetooth()
    {
        foreach (var device in _bluetooth)
        {
            device.IsMatch = device.Matches(_search);
        }

        Sync(BluetoothDevices, _bluetooth.Where(d => d.IsMatch).ToList());
        OnPropertyChanged(nameof(ConnectedCount));
        OnPropertyChanged(nameof(PairedCount));
        OnPropertyChanged(nameof(BluetoothSummary));
        OnPropertyChanged(nameof(LowBatteryText));
        OnPropertyChanged(nameof(HasBluetoothDevices));
        OnPropertyChanged(nameof(BluetoothEmptyText));
        OnPropertyChanged(nameof(SearchSuggestions));
    }

    private void Apply(IReadOnlyList<WiredDevice> devices)
    {
        var byId = _wired.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var next = new List<WiredDeviceViewModel>(devices.Count);
        foreach (var device in devices)
        {
            if (byId.Remove(device.Id, out var existing))
            {
                existing.Update(device);
                next.Add(existing);
            }
            else
            {
                next.Add(new WiredDeviceViewModel(device, EjectAsync));
            }
        }

        _wired.Clear();
        _wired.AddRange(next
            .OrderBy(d => d.Device.Group)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase));
        ApplyWired();
    }

    private void ApplyWired()
    {
        var visible = _wired.Where(d => d.Matches(_search)).ToList();
        var groups = new List<DeviceGroupViewModel>();
        foreach (var byGroup in visible.GroupBy(d => d.Device.Group).OrderBy(g => g.Key))
        {
            if (!_groups.TryGetValue(byGroup.Key, out var group))
            {
                group = new DeviceGroupViewModel(byGroup.Key);
                _groups[byGroup.Key] = group;
            }

            Sync(group.Devices, byGroup.ToList());
            group.CountChanged();
            groups.Add(group);
        }

        Sync(WiredGroups, groups);
        OnPropertyChanged(nameof(WiredCount));
        OnPropertyChanged(nameof(WiredSummary));
        OnPropertyChanged(nameof(HasWiredDevices));
        OnPropertyChanged(nameof(WiredEmptyText));
        OnPropertyChanged(nameof(SearchSuggestions));
    }

    private async Task ToggleAsync(BluetoothDeviceViewModel device)
    {
        bool connect = !device.IsConnected;
        device.IsBusy = true;
        device.ShowMessage(null, false);
        try
        {
            var result = connect
                ? await _service.ConnectAsync(device.Info, CancellationToken.None)
                : await _service.DisconnectAsync(device.Info, CancellationToken.None);
            device.ShowMessage(result.Message, !result.Succeeded);
        }
        catch (Exception e) when (IsExpected(e))
        {
            device.ShowMessage($"{Platform.Name()} reported a problem: {e.Message}", true);
        }
        finally
        {
            device.IsBusy = false;
        }

        await RefreshBluetoothAsync();
    }

    private async Task SetRadioAsync(bool on)
    {
        IsRadioBusy = true;
        RadioMessage = null;
        try
        {
            if (!await _service.SetRadioAsync(on, CancellationToken.None))
            {
                RadioMessage = $"{Platform.Name()} didn't let Coreloom turn Bluetooth {(on ? "on" : "off")}. Use the switch in Bluetooth settings instead.";
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            RadioMessage = $"Bluetooth couldn't be switched: {e.Message}";
        }
        finally
        {
            IsRadioBusy = false;
        }

        await RefreshBluetoothAsync();
        RadioChanged(); // the switch shows the real state, also when the change didn't happen
    }

    private async Task EjectAsync(WiredDeviceViewModel device)
    {
        device.IsBusy = true;
        try
        {
            var result = await _service.EjectAsync(device.Device, CancellationToken.None);
            device.ShowMessage(result.Message, !result.Succeeded);
        }
        catch (Exception e) when (IsExpected(e))
        {
            device.ShowMessage($"{Platform.Name()} reported a problem: {e.Message}", true);
        }
        finally
        {
            device.IsBusy = false;
        }

        await RefreshWiredAsync();
    }

    /// <summary>Makes <paramref name="target"/> equal <paramref name="items"/> with moves and inserts, so cards aren't rebuilt.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        where T : class
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int at = target.IndexOf(item);
            if (at == i)
            {
                continue;
            }

            if (at >= 0)
            {
                target.Move(at, i);
            }
            else
            {
                target.Insert(i, item);
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
