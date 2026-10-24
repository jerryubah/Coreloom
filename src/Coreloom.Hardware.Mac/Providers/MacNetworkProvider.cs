using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Providers;

/// <summary>
/// Network throughput per interface from the kernel's 64-bit interface byte counters, named the way System Settings
/// names them ("Wi-Fi", "Ethernet", "Thunderbolt Bridge"), plus the active connection: the interface macOS routes the
/// default traffic through. Interfaces are re-read when addresses change, so switching Wi-Fi, plugging in Ethernet or
/// connecting a VPN is picked up.
/// </summary>
public sealed class MacNetworkProvider : ITelemetryProvider
{
    public const int Priority = 50;
    private const string LogCategory = "Network";
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(15);
    private readonly Func<string?> _preferredAdapter;
    private readonly Dictionary<string, Adapter> _adapters = new(StringComparer.Ordinal);
    private IProviderContext? _context;
    private MetricHandle? _activeDown;
    private MetricHandle? _activeUp;
    private MetricHandle? _activeRx;
    private MetricHandle? _activeTx;
    private volatile bool _rescanRequested = true;
    private DateTime _lastRescan = DateTime.MinValue;
    private string? _primary;
    private string? _activeKey;
    private IReadOnlyList<(string Key, string Name, bool IsActive)> _snapshot = [];

    public MacNetworkProvider(Func<string?>? preferredAdapter = null) => _preferredAdapter = preferredAdapter ?? (() => null);

    public string Name => "macOS network";

    public MonitoringSection Section => MonitoringSection.Network;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public string? ActiveAdapterKey => _activeKey;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _context = context;
        MetricHandle Reg(string id, string name, MetricKind kind, bool track = true) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.Network, kind)
            {
                Group = "Active connection",
                TrackHistory = track,
            }, Provenance.Measured, Priority);

        _activeDown = Reg(MetricIds.NetworkActiveDown, "Download", MetricKind.Throughput);
        _activeUp = Reg(MetricIds.NetworkActiveUp, "Upload", MetricKind.Throughput);
        _activeRx = Reg(MetricIds.NetworkActiveReceivedSession, "Received this session", MetricKind.Data, track: false);
        _activeTx = Reg(MetricIds.NetworkActiveSentSession, "Sent this session", MetricKind.Data, track: false);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        Rescan();
        return ProviderInitResult.Running($"{_adapters.Count} interface(s){(_primary is null ? string.Empty : $", primary {_primary}")}");
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _rescanRequested = true;

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => _rescanRequested = true;

    private void Rescan()
    {
        var context = _context!;
        NetworkInterface[] interfaces;
        Dictionary<string, (string Name, string? Type)> known;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
            known = SystemConfiguration.Interfaces();
            _primary = SystemConfiguration.PrimaryInterface();
        }
        catch (Exception ex) when (ex is NetworkInformationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn(LogCategory, "Enumerating network interfaces failed.", ex);
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in interfaces)
        {
            string bsd = nic.Name;
            bool vpn = false;
            string name;
            string? scType = null;
            if (known.TryGetValue(bsd, out var sc))
            {
                name = sc.Name;
                scType = sc.Type;
            }
            else if ((bsd.StartsWith("utun", StringComparison.Ordinal) || bsd.StartsWith("ipsec", StringComparison.Ordinal) || bsd.StartsWith("ppp", StringComparison.Ordinal))
                && nic.OperationalStatus == OperationalStatus.Up && HasIPv4(nic))
            {
                vpn = true;
                name = $"VPN ({bsd})";
            }
            else
            {
                continue; // lo0, awdl0, llw0, anpi*, gif0, stf0 and the system's own tunnels
            }

            string key = AdapterKey(bsd);
            seen.Add(key);
            if (!_adapters.TryGetValue(key, out var adapter))
            {
                string deviceId = MetricIds.NetworkDeviceId(key);
                MetricHandle Reg(string id, string label, MetricKind kind, bool track) =>
                    context.Register(new MetricDescriptor(id, label, MetricCategory.Network, kind)
                    {
                        DeviceId = deviceId,
                        DeviceName = name,
                        Group = "Adapters",
                        TrackHistory = track,
                    }, Provenance.Measured, Priority);

                adapter = new Adapter(bsd, key)
                {
                    Down = Reg(MetricIds.NetworkDown(key), $"{name} download", MetricKind.Throughput, true),
                    Up = Reg(MetricIds.NetworkUp(key), $"{name} upload", MetricKind.Throughput, true),
                    Received = Reg(MetricIds.NetworkReceivedSession(key), $"{name} received", MetricKind.Data, false),
                    Sent = Reg(MetricIds.NetworkSentSession(key), $"{name} sent", MetricKind.Data, false),
                };
                _adapters[key] = adapter;
            }

            adapter.Interface = nic;
            adapter.Name = name;
            adapter.IsVirtual = vpn || scType is "Bridge" || bsd.StartsWith("bridge", StringComparison.Ordinal);
            adapter.IsUp = nic.OperationalStatus == OperationalStatus.Up || HasIPv4(nic);
            adapter.IsPrimary = bsd == _primary;
            context.UpsertDevice(Describe(nic, key, adapter, TypeName(scType, nic, vpn)));
        }

        foreach (string gone in _adapters.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _adapters.Remove(gone);
            context.RemoveDevice(MetricIds.NetworkDeviceId(gone));
        }

        _lastRescan = DateTime.UtcNow;
        _rescanRequested = false;
    }

    private static bool HasIPv4(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    internal static string TypeName(string? scType, NetworkInterface nic, bool vpn) => vpn ? "VPN" : scType switch
    {
        "IEEE80211" => "Wi-Fi",
        "Ethernet" => "Ethernet",
        "Bridge" => "Bridge",
        "Bond" => "Link aggregate",
        "VLAN" => "VLAN",
        "PPP" => "PPP",
        "WWAN" => "Mobile broadband",
        "Bluetooth" => "Bluetooth",
        _ => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet",
    };

    private static DeviceInfo Describe(NetworkInterface nic, string key, Adapter adapter, string type)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Interface", nic.Name),
            new("Type", type),
            new("Status", adapter.IsUp ? "Connected" : "Not connected"),
        };
        if (adapter.IsPrimary)
        {
            props.Add(new("Route", "Primary (default route)"));
        }

        if (adapter.IsUp && nic.Speed > 0)
        {
            props.Add(new("Link speed", FormatLinkSpeed(nic.Speed)));
        }

        try
        {
            if (nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork) is { } ipv4)
            {
                props.Add(new("IPv4 address", ipv4.Address.ToString()));
            }

            string mac = string.Join(':', nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            if (mac.Length > 0 && mac != "00:00:00:00:00:00")
            {
                props.Add(new("Hardware address", mac));
            }
        }
        catch (NetworkInformationException)
        {
        }

        return new DeviceInfo(MetricIds.NetworkDeviceId(key), MetricCategory.Network, adapter.Name)
        {
            Kind = type,
            Order = adapter.IsVirtual ? 100 : adapter.IsPrimary ? 0 : adapter.IsUp ? 10 : 50,
            Source = "macOS",
            Properties = props,
        };
    }

    public void Poll(IMetricWriter writer)
    {
        if (_rescanRequested || DateTime.UtcNow - _lastRescan >= RescanInterval)
        {
            Rescan();
        }

        long now = Environment.TickCount64;
        foreach (var adapter in _adapters.Values)
        {
            if (adapter.Interface is null)
            {
                continue;
            }

            long rx, tx;
            try
            {
                var stats = adapter.Interface.GetIPStatistics();
                rx = stats.BytesReceived;
                tx = stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (adapter.LastTimestamp is long last && now > last)
            {
                double seconds = (now - last) / 1000.0;
                long dRx = rx - adapter.LastReceived;
                long dTx = tx - adapter.LastSent;
                adapter.LastTimestamp = now;
                adapter.LastReceived = rx;
                adapter.LastSent = tx;
                if (dRx < 0 || dTx < 0)
                {
                    continue; // counters reset (the interface came back up)
                }

                adapter.DownRate = dRx / seconds;
                adapter.UpRate = dTx / seconds;
                adapter.SessionReceived += dRx;
                adapter.SessionSent += dTx;
                adapter.RecentBytes = (adapter.RecentBytes * 0.8) + dRx + dTx;
                writer.Write(adapter.Down!, adapter.DownRate);
                writer.Write(adapter.Up!, adapter.UpRate);
                writer.Write(adapter.Received!, adapter.SessionReceived);
                writer.Write(adapter.Sent!, adapter.SessionSent);
                continue;
            }

            adapter.LastTimestamp = now;
            adapter.LastReceived = rx;
            adapter.LastSent = tx;
        }

        var active = SelectActive();
        _activeKey = active?.Key;
        PublishSnapshot();
        if (active is not null && active.LastTimestamp is not null)
        {
            writer.Write(_activeDown!, active.DownRate);
            writer.Write(_activeUp!, active.UpRate);
            writer.Write(_activeRx!, active.SessionReceived);
            writer.Write(_activeTx!, active.SessionSent);
        }
    }

    private Adapter? SelectActive()
    {
        string? preferred = _preferredAdapter();
        if (!string.IsNullOrEmpty(preferred)
            && _adapters.Values.FirstOrDefault(a => a.Key == preferred || a.Bsd == preferred) is { } match)
        {
            return match;
        }

        return _adapters.Values
            .Where(a => a.IsUp)
            .OrderByDescending(a => a.IsPrimary)
            .ThenBy(a => a.IsVirtual)
            .ThenByDescending(a => a.RecentBytes)
            .FirstOrDefault();
    }

    public IReadOnlyList<(string Key, string Name, bool IsActive)> GetAdapters() => Volatile.Read(ref _snapshot);

    private void PublishSnapshot()
    {
        var next = _adapters.Values.OrderBy(a => a.IsVirtual).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => (a.Key, a.Name, a.Key == _activeKey)).ToArray();
        if (!Volatile.Read(ref _snapshot).SequenceEqual(next))
        {
            Volatile.Write(ref _snapshot, next);
        }
    }

    internal static string AdapterKey(string bsd) =>
        new string(bsd.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() is { Length: > 0 } key ? key : "adapter";

    internal static string FormatLinkSpeed(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000_000
            ? (bitsPerSecond / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " Gbps"
            : (bitsPerSecond / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mbps";

    public void Shutdown()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _adapters.Clear();
        _activeKey = null;
        Volatile.Write(ref _snapshot, Array.Empty<(string Key, string Name, bool IsActive)>());
    }

    public void Dispose() => Shutdown();

    private sealed class Adapter(string bsd, string key)
    {
        public string Bsd { get; } = bsd;

        public string Key { get; } = key;

        public string Name { get; set; } = bsd;

        public NetworkInterface? Interface { get; set; }

        public bool IsVirtual { get; set; }

        public bool IsUp { get; set; }

        public bool IsPrimary { get; set; }

        public MetricHandle? Down { get; init; }

        public MetricHandle? Up { get; init; }

        public MetricHandle? Received { get; init; }

        public MetricHandle? Sent { get; init; }

        public long? LastTimestamp { get; set; }

        public long LastReceived { get; set; }

        public long LastSent { get; set; }

        public double DownRate { get; set; }

        public double UpRate { get; set; }

        public double SessionReceived { get; set; }

        public double SessionSent { get; set; }

        public double RecentBytes { get; set; }
    }
}
