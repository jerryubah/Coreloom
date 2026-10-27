using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Connect;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Connect;

/// <summary>A paired phone in Settings › Connect.</summary>
public sealed class PairedDeviceViewModel : ObservableObject
{
    private bool _isConnected;
    private string _statusText = string.Empty;

    public PairedDeviceViewModel(PairedDevice device, Action<PairedDeviceViewModel> remove)
    {
        Id = device.Id;
        Name = device.Name;
        IconKey = "Icon.Phone";
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Id { get; }

    public string Name { get; }

    public string IconKey { get; }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    /// <summary>"Connected now", "Last connected 5 min ago", "Paired today"…</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public IRelayCommand RemoveCommand { get; }
}

/// <summary>
/// Settings › Connect: turn Coreloom Connect on, show the QR code and pairing code (pairing is open only while this
/// section is on screen), the address phones use, and the paired phones with a way to remove each.
/// </summary>
public sealed class ConnectViewModel : ObservableObject
{
    private readonly ConnectHost _host;
    private readonly SettingsService _settings;
    private readonly IShellService _shell;
    private readonly SynchronizationContext? _sync;
    private readonly Func<DateTimeOffset> _now;
    private bool _visible;
    private bool _refreshQueued;
    private Timer? _clock;
    private int _ticks;
    private PairingTicket? _ticket;
    private string _qrPath = string.Empty;
    private double _qrSize = 1;
    private string _renewsText = string.Empty;
    private string _url = string.Empty;
    private string? _noteShown;
    private string? _noteSeen;
    private readonly Dismissal _noteDismissal = new();
    private string? _problemShown;
    private string? _problemSeen;
    private readonly Dismissal _problemDismissal = new();

    public ConnectViewModel(ConnectHost host, SettingsService settings, IShellService shell, bool isMac, Func<DateTimeOffset>? now = null)
    {
        _host = host;
        _settings = settings;
        _shell = shell;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _sync = SynchronizationContext.Current;
        IsMac = isMac;
        RenewCommand = new RelayCommand(() => _host.Pairing.Renew());
        CopyAddressCommand = new RelayCommand(() => _shell.CopyToClipboard(Url), () => Url.Length > 0);
        OpenNetworkSettingsCommand = new RelayCommand(_host.OpenNetworkSettings);
        RemoveAllCommand = new RelayCommand(RemoveAll, () => Devices.Count > 0);
        _host.Changed += QueueRefresh;
        Refresh();
    }

    public bool IsMac { get; }

    public string Computer => IsMac ? "Mac" : "PC";

    /// <summary>The switch: phones on this network may connect.</summary>
    public bool IsEnabled
    {
        get => _settings.Current.Connect.Enabled;
        set
        {
            if (_settings.Current.Connect.Enabled != value)
            {
                _settings.Update("Connect", s => s.Connect.Enabled = value);
                OnPropertyChanged();
            }
        }
    }

    public bool IsOn => _host.Status == ConnectStatus.On;

    public bool IsStarting => _host.Status == ConnectStatus.Starting;

    /// <summary>The status card's headline: "Connect is off", "Ready for your phone", "2 phones connected"…</summary>
    public string HeadlineText => _host.Status switch
    {
        ConnectStatus.On => ConnectedCount switch
        {
            0 => HasAddress ? "Ready for your phone" : "Waiting for a network",
            1 => "1 phone connected",
            int n => $"{n} phones connected",
        },
        ConnectStatus.Starting => "Starting Connect…",
        ConnectStatus.Failed => "Connect couldn't start",
        _ => "Connect is off",
    };

    /// <summary>The line under the headline.</summary>
    public string DetailText => _host.Status switch
    {
        ConnectStatus.On when HasAddress => $"Phones on {(AdapterText.Length > 0 ? AdapterText : "this network")} reach this {Computer} at {DisplayAddress}.",
        ConnectStatus.On => $"Connect this {Computer} to Wi-Fi or Ethernet, then pair your phone here.",
        ConnectStatus.Starting => "Opening a port on this network…",
        ConnectStatus.Failed => (Problem ?? string.Empty) == _problemSeen ? "Turn Connect off and on to try again." : Problem ?? "Something stopped Connect from starting.",
        _ => $"Turn it on to see this {Computer}'s readings live on your phone, over your own network.",
    };

    /// <summary>"Success" (a phone is connected), "Accent" (on), "Critical" (couldn't start) or "" (off).</summary>
    public string StatusTone => _host.Status switch
    {
        ConnectStatus.On => ConnectedCount > 0 ? "Success" : "Accent",
        ConnectStatus.Failed => "Critical",
        _ => string.Empty,
    };

    /// <summary>Kept short for places with little room (the Windows status line, tests).</summary>
    public string StatusText => _host.Status switch
    {
        ConnectStatus.On => ConnectedCount switch
        {
            0 => "On, no phone connected",
            1 => "On, 1 phone connected",
            int n => $"On, {n} phones connected",
        },
        ConnectStatus.Starting => "Starting…",
        ConnectStatus.Failed => "Couldn't start",
        _ => "Off",
    };

    /// <summary>On, but this computer has no address on a local network yet (no Wi-Fi or Ethernet).</summary>
    public bool NeedsNetwork => _host.Status == ConnectStatus.On && !HasAddress;

    public bool HasProblem => _host.Problem is not null;

    public string? Problem => _host.Problem;

    /// <summary>A warning about the network (a firewall that may block phones); shown for five seconds each time it changes.</summary>
    public bool HasNote => _host.Note is not null && IsOn && _host.Note != _noteSeen;

    public string? Note => _host.Note;

    public bool CanOpenNetworkSettings => _host.CanOpenNetworkSettings && HasNote;

    /// <summary>The address to type on the phone: "http://192.168.1.20:47101".</summary>
    public string Url
    {
        get => _url;
        private set
        {
            if (SetProperty(ref _url, value))
            {
                OnPropertyChanged(nameof(DisplayAddress));
                OnPropertyChanged(nameof(HasAddress));
                CopyAddressCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The address without "http://", as shown under the code.</summary>
    public string DisplayAddress => Url.StartsWith("http://", StringComparison.Ordinal) ? Url[7..] : Url;

    public bool HasAddress => Url.Length > 0;

    /// <summary>"Wi-Fi" or "Ethernet": the network the address is on.</summary>
    public string AdapterText => _host.Addresses.FirstOrDefault() is { } a ? a.Adapter : string.Empty;

    /// <summary>Other addresses this computer has (a second network, a VPN).</summary>
    public IReadOnlyList<string> OtherAddresses => _host.Addresses.Skip(1).Select(a => $"{a.Address}:{_host.Port.ToString(CultureInfo.InvariantCulture)} ({a.Adapter})").ToArray();

    public bool HasOtherAddresses => _host.Addresses.Count > 1;

    public bool IsPairingOpen => _ticket is not null && IsOn && HasAddress;

    /// <summary>The QR code (its link: the address, then the one-time secret after #, which a browser never sends).</summary>
    public string QrText => _ticket is null || !HasAddress ? string.Empty : $"{Url}/#p={_ticket.LinkSecret}";

    /// <summary>The QR code as path data in a <see cref="QrSize"/>-square box (quiet zone included).</summary>
    public string QrPath
    {
        get => _qrPath;
        private set => SetProperty(ref _qrPath, value);
    }

    public double QrSize
    {
        get => _qrSize;
        private set => SetProperty(ref _qrSize, value);
    }

    /// <summary>"482 913".</summary>
    public string PairingCode => _ticket is null ? "— — —" : $"{_ticket.Code[..3]} {_ticket.Code[3..]}";

    /// <summary>"New code in 4:32".</summary>
    public string RenewsText
    {
        get => _renewsText;
        private set => SetProperty(ref _renewsText, value);
    }

    public bool IsPaused => _host.Pairing.IsPaused;

    public ObservableCollection<PairedDeviceViewModel> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public int ConnectedCount => Devices.Count(d => d.IsConnected);

    public string DevicesTitle => Devices.Count switch
    {
        0 => "No paired phones yet",
        1 => "1 paired phone",
        int n => $"{n} paired phones",
    };

    public IRelayCommand RenewCommand { get; }

    public IRelayCommand CopyAddressCommand { get; }

    public IRelayCommand OpenNetworkSettingsCommand { get; }

    public IRelayCommand RemoveAllCommand { get; }

    /// <summary>The section is on screen (pairing opens) or not (it closes).</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        _clock?.Dispose();
        _clock = null;
        if (visible && _sync is { } sync)
        {
            // The countdown ticks every second, whatever the sampling interval.
            _clock = new Timer(_ => sync.Post(_ => Tick(), null), null, 1000, 1000);
        }

        if (visible)
        {
            _host.RefreshAddresses();
            if (_host.Status == ConnectStatus.On)
            {
                _host.Pairing.Open();
            }
        }
        else
        {
            _host.Pairing.Close();
        }

        Refresh();
    }

    /// <summary>Called about once a second while the section is shown: the countdown, and a new code when it expires.</summary>
    public void Tick()
    {
        if (!_visible)
        {
            return;
        }

        if (_host.Status == ConnectStatus.On && !_host.Pairing.IsOpen)
        {
            _host.Pairing.Open();
        }

        if (++_ticks % 5 == 0)
        {
            _host.RefreshAddresses(); // Wi-Fi changed while the page is open
        }

        var ticket = _host.Pairing.Current; // renews an expired code
        if (!ReferenceEquals(ticket, _ticket))
        {
            Refresh();
            return;
        }

        UpdateCountdown();
        UpdateDeviceTimes();
    }

    private void QueueRefresh()
    {
        if (_sync is null)
        {
            Refresh();
            return;
        }

        lock (this)
        {
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
        }

        _sync.Post(_ =>
        {
            lock (this)
            {
                _refreshQueued = false;
            }

            Refresh();
        }, null);
    }

    /// <summary>Warnings and why Connect couldn't start leave after five seconds (the headline still says it couldn't start).</summary>
    private void DismissLater()
    {
        string? note = IsOn ? _host.Note : null;
        if (note != _noteShown)
        {
            _noteShown = note;
            _noteSeen = null;
            _noteDismissal.Update(note is not null, () =>
            {
                _noteSeen = note;
                Refresh();
            });
        }

        string? problem = _host.Status == ConnectStatus.Failed ? _host.Problem ?? string.Empty : null;
        if (problem != _problemShown)
        {
            _problemShown = problem;
            _problemSeen = null;
            _problemDismissal.Update(problem is not null, () =>
            {
                _problemSeen = problem;
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        DismissLater();
        var primary = _host.Addresses.FirstOrDefault();
        Url = primary is null || _host.Status != ConnectStatus.On ? string.Empty : primary.Url(_host.Port);

        if (_visible && _host.Status == ConnectStatus.On && !_host.Pairing.IsOpen)
        {
            _host.Pairing.Open();
        }

        _ticket = _visible ? _host.Pairing.Current : null;
        if (QrText.Length > 0)
        {
            var qr = QrCode.Encode(QrText, QrErrorCorrection.Medium);
            QrPath = qr.ToSvgPath(border: 2);
            QrSize = qr.Size + 4;
        }
        else
        {
            QrPath = string.Empty;
        }

        SyncDevices();
        UpdateCountdown();
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(NeedsNetwork));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(CanOpenNetworkSettings));
        OnPropertyChanged(nameof(AdapterText));
        OnPropertyChanged(nameof(OtherAddresses));
        OnPropertyChanged(nameof(HasOtherAddresses));
        OnPropertyChanged(nameof(IsPairingOpen));
        OnPropertyChanged(nameof(QrText));
        OnPropertyChanged(nameof(PairingCode));
        OnPropertyChanged(nameof(IsPaused));
    }

    private void UpdateCountdown()
    {
        if (_ticket is null)
        {
            RenewsText = string.Empty;
            return;
        }

        var left = _ticket.Expires - _now();
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        RenewsText = $"New code in {(int)left.TotalMinutes}:{left.Seconds.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private void SyncDevices()
    {
        var devices = _host.Store.Devices.OrderByDescending(d => d.LastSeen ?? d.Paired).ToList();
        if (!devices.Select(d => d.Id).SequenceEqual(Devices.Select(d => d.Id)))
        {
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(new PairedDeviceViewModel(device, Remove));
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(DevicesTitle));
            RemoveAllCommand.NotifyCanExecuteChanged();
        }

        var connected = _host.ConnectedDevices.ToHashSet(StringComparer.Ordinal);
        foreach (var row in Devices)
        {
            row.IsConnected = connected.Contains(row.Id);
        }

        UpdateDeviceTimes();
        OnPropertyChanged(nameof(ConnectedCount));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(StatusText));
    }

    private void UpdateDeviceTimes()
    {
        var now = _now();
        var byId = _host.Store.Devices.ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var row in Devices)
        {
            if (row.IsConnected)
            {
                row.StatusText = "Connected now";
            }
            else if (byId.TryGetValue(row.Id, out var device))
            {
                row.StatusText = device.LastSeen is { } seen ? $"Last connected {Ago(now - seen, seen)}" : "Paired, not connected yet";
            }
        }
    }

    internal static string Ago(TimeSpan span, DateTimeOffset when)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        if (span < TimeSpan.FromDays(1))
        {
            int hours = (int)span.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        return when.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }

    private void Remove(PairedDeviceViewModel device)
    {
        if (_shell.Confirm($"Remove {device.Name}?", $"{device.Name} will stop seeing this {Computer}'s readings. To use it again, pair it again."))
        {
            _host.Store.Remove(device.Id);
        }
    }

    private void RemoveAll()
    {
        if (_shell.Confirm("Remove every paired phone?", $"Every phone will stop seeing this {Computer}'s readings until it's paired again."))
        {
            _host.Store.RemoveAll();
        }
    }
}
