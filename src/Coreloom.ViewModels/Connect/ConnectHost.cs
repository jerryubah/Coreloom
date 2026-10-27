using System.Net.Sockets;
using Coreloom.Core.Connect;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Connect;

/// <summary>What the operating system needs for phones to reach Coreloom Connect.</summary>
public interface IConnectPlatform
{
    /// <summary>
    /// Lets phones on the local network reach the port (Windows: a firewall rule for private networks). Returns a note
    /// for Settings › Connect when phones may still be kept out (a network set to Public, say), else null.
    /// </summary>
    Task<string?> AllowAsync(int port, CancellationToken token);

    /// <summary>Takes the way in away again when Connect is turned off.</summary>
    Task RemoveAsync(CancellationToken token);

    /// <summary>True when there are network settings to open (to make a network Private).</summary>
    bool CanOpenNetworkSettings { get; }

    void OpenNetworkSettings();
}

/// <summary>No firewall to manage (macOS asks the person itself if its firewall is on).</summary>
public sealed class NoConnectPlatform : IConnectPlatform
{
    public static NoConnectPlatform Instance { get; } = new();

    public bool CanOpenNetworkSettings => false;

    public Task<string?> AllowAsync(int port, CancellationToken token) => Task.FromResult<string?>(null);

    public Task RemoveAsync(CancellationToken token) => Task.CompletedTask;

    public void OpenNetworkSettings()
    {
    }
}

/// <summary>Where Coreloom Connect keeps its state and how it reaches the operating system.</summary>
public sealed class ConnectEnvironment
{
    /// <summary>The settings folder: paired phones are kept in connect.json there.</summary>
    public required string DataDirectory { get; init; }

    public IConnectPlatform Platform { get; init; } = NoConnectPlatform.Instance;

    /// <summary>The phone app's files; normally those embedded in Coreloom.Core.</summary>
    public WebAssets? Assets { get; init; }

    /// <summary>This computer's addresses on its networks, best first (replaced in screenshots and tests).</summary>
    public Func<IReadOnlyList<LocalAddress>> FindAddresses { get; init; } = LocalAddresses.Find;
}

public enum ConnectStatus
{
    Off,
    Starting,
    On,
    Failed,
}

/// <summary>
/// Runs Coreloom Connect while it's turned on in Settings: the server, the paired phones, pairing, and the readings fed
/// to phones from each frame (built only while a phone is watching).
/// </summary>
public sealed class ConnectHost : IAsyncDisposable
{
    private const string LogCategory = "Connect";

    private readonly PageContext _context;
    private readonly ConnectEnvironment _environment;
    private readonly RemoteFeed _feed;
    private readonly SemaphoreSlim _apply = new(1, 1);
    private readonly Lazy<WebAssets> _assets;
    private readonly string _appVersion;
    private ConnectServer? _server;
    private int _runningPort;
    private bool _allowed;

    public ConnectHost(PageContext context, ConnectEnvironment environment, string appVersion)
    {
        _context = context;
        _environment = environment;
        _appVersion = appVersion;
        _feed = new RemoteFeed(context);
        _assets = new Lazy<WebAssets>(() => environment.Assets ?? WebAssets.Embedded());
        Store = new PairedDeviceStore(Path.Combine(environment.DataDirectory, "connect.json"));
        Pairing = new PairingWindow();
        Store.Changed += RaiseChanged;
        Pairing.Changed += RaiseChanged;
    }

    /// <summary>Raised on any thread when the status, the paired or connected phones or the pairing code change.</summary>
    public event Action? Changed;

    public PairedDeviceStore Store { get; }

    public PairingWindow Pairing { get; }

    public ConnectStatus Status { get; private set; }

    /// <summary>Why Connect couldn't start, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Something that may still keep phones out (the network is Public…), or null.</summary>
    public string? Note { get; private set; }

    public int Port => _runningPort;

    public IReadOnlyList<LocalAddress> Addresses { get; private set; } = [];

    public IReadOnlyList<string> ConnectedDevices => _server?.ConnectedDevices ?? [];

    public bool CanOpenNetworkSettings => _environment.Platform.CanOpenNetworkSettings;

    public void OpenNetworkSettings() => _environment.Platform.OpenNetworkSettings();

    /// <summary>Looks at the network again (the address in the QR code follows Wi-Fi changes).</summary>
    public void RefreshAddresses()
    {
        var addresses = FindAddresses();
        if (!addresses.SequenceEqual(Addresses)) // the addresses, or a network's name once the Network readings know it
        {
            Addresses = addresses;
            RaiseChanged();
        }
    }

    /// <summary>Starts, stops or restarts the server to match Settings › Connect.</summary>
    public async Task ApplyAsync()
    {
        var settings = _context.Settings.Current.Connect;
        await _apply.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!settings.Enabled)
            {
                bool wasOn = _server is not null || _allowed;
                SetStatus(ConnectStatus.Off, null, null); // first, so nothing reopens pairing while it stops
                await StopAsync().ConfigureAwait(false);
                if (_allowed)
                {
                    _allowed = false;
                    await _environment.Platform.RemoveAsync(CancellationToken.None).ConfigureAwait(false);
                }

                if (wasOn)
                {
                    Log.Info(LogCategory, "Turned off.");
                }

                return;
            }

            if (_server is not null && _runningPort == settings.Port)
            {
                return;
            }

            SetStatus(ConnectStatus.Starting, null, null);
            await StopAsync().ConfigureAwait(false);
            try
            {
                var server = ConnectServer.Start(new ConnectServerOptions
                {
                    Port = settings.Port,
                    ComputerName = string.IsNullOrWhiteSpace(_context.Monitor.Summary.ComputerName) ? Environment.MachineName : _context.Monitor.Summary.ComputerName,
                    Platform = _context.IsMac ? "macos" : "windows",
                    AppVersion = _appVersion,
                    Store = Store,
                    Pairing = Pairing,
                    Assets = _assets.Value,
                });
                server.HistoryProvider = _feed.History;
                server.ConnectedChanged += RaiseChanged;
                _server = server;
                _runningPort = server.Port;
            }
            catch (SocketException ex)
            {
                Log.Warn(LogCategory, $"Couldn't listen on port {settings.Port} ({ex.SocketErrorCode}).");
                SetStatus(ConnectStatus.Failed, ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {settings.Port} is in use by another app."
                    : $"Coreloom couldn't open port {settings.Port} ({ex.SocketErrorCode}).", null);
                return;
            }

            Addresses = FindAddresses();
            string? note = null;
            try
            {
                note = await _environment.Platform.AllowAsync(_runningPort, CancellationToken.None).ConfigureAwait(false);
                _allowed = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory, "Allowing phones through the firewall failed.", ex);
                note = "Coreloom couldn't add its firewall rule, so your phone may not reach it.";
            }

            SetStatus(ConnectStatus.On, null, note);
        }
        finally
        {
            _apply.Release();
        }
    }

    /// <summary>Feeds a frame to the phones watching (on the UI thread, after the health check has seen it).</summary>
    public void OnFrame(TelemetryFrame frame)
    {
        if (_server is { HasViewers: true } server)
        {
            server.Publish(_feed.Build(frame));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _apply.WaitAsync().ConfigureAwait(false);
        try
        {
            Status = ConnectStatus.Off;
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _apply.Release();
        }
    }

    private async Task StopAsync()
    {
        Pairing.Close();
        if (_server is { } server)
        {
            _server = null;
            server.ConnectedChanged -= RaiseChanged;
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SetStatus(ConnectStatus status, string? problem, string? note)
    {
        Status = status;
        Problem = problem;
        Note = note;
        RaiseChanged();
    }

    /// <summary>
    /// This computer's addresses, each named as the Network page names its adapter ("Wi-Fi" rather than macOS's "en0";
    /// Windows' adapter names are already the ones people know).
    /// </summary>
    private IReadOnlyList<LocalAddress> FindAddresses()
    {
        var addresses = _environment.FindAddresses();
        IReadOnlyList<(string Key, string Name, bool IsActive)> adapters;
        try
        {
            adapters = _context.Monitor.NetworkAdapters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return addresses;
        }

        return addresses.Select(a =>
        {
            string key = new string(a.Adapter.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return adapters.FirstOrDefault(n => n.Key == key) is { Name.Length: > 0 } match && match.Name != a.Adapter ? a with { Adapter = match.Name } : a;
        }).ToArray();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
