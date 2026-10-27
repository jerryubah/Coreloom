using System.Text.Json;
using System.Text.Json.Serialization;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Connect;

/// <summary>A phone (or tablet, or another browser) paired with this computer.</summary>
public sealed class PairedDevice
{
    public string Id { get; set; } = string.Empty;

    /// <summary>What the phone calls itself ("iPhone", "Pixel 9"…), shown in Settings › Connect.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The device key (32 bytes, base64url). Only this computer and that phone have it.</summary>
    public string Key { get; set; } = string.Empty;

    public DateTimeOffset Paired { get; set; }

    public DateTimeOffset? LastSeen { get; set; }

    /// <summary>"qr", "code" or "home" (a Home Screen copy set up from an already paired phone).</summary>
    public string Via { get; set; } = string.Empty;
}

/// <summary>
/// The phones paired with this computer and this computer's own Connect id, in <c>connect.json</c> next to the
/// settings (readable only by the user on macOS). Thread-safe: the server and Settings › Connect both use it.
/// </summary>
public sealed class PairedDeviceStore
{
    public const int MaximumDevices = 20;
    private const string LogCategory = "Connect";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private State _state;

    public PairedDeviceStore(string? filePath)
    {
        _filePath = filePath;
        _state = Load(filePath);
        if (string.IsNullOrEmpty(_state.PcId))
        {
            _state.PcId = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(16));
            Save();
        }
    }

    /// <summary>Raised (on the caller's thread) when a device is added, removed or renamed.</summary>
    public event Action? Changed;

    /// <summary>This computer's id among the phone's paired computers.</summary>
    public string PcId
    {
        get
        {
            lock (_gate)
            {
                return _state.PcId;
            }
        }
    }

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _state.Devices.Select(Copy).ToArray();
            }
        }
    }

    public PairedDevice? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_gate)
        {
            var device = _state.Devices.FirstOrDefault(d => d.Id == id);
            return device is null ? null : Copy(device);
        }
    }

    /// <summary>Adds a newly paired device with a fresh id and key. The oldest is forgotten past the limit.</summary>
    public PairedDevice Add(string name, string via, DateTimeOffset now)
    {
        var device = new PairedDevice
        {
            Id = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(16)),
            Name = CleanName(name),
            Key = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(ConnectCrypto.KeySize)),
            Paired = now,
            LastSeen = now,
            Via = via,
        };

        lock (_gate)
        {
            _state.Devices.Add(device);
            while (_state.Devices.Count > MaximumDevices)
            {
                var oldest = _state.Devices.OrderBy(d => d.LastSeen ?? d.Paired).First();
                _state.Devices.Remove(oldest);
            }

            SaveLocked();
        }

        Log.Info(LogCategory, $"Paired \"{device.Name}\" ({via}).");
        Changed?.Invoke();
        return Copy(device);
    }

    public bool Remove(string id)
    {
        PairedDevice? removed;
        lock (_gate)
        {
            removed = _state.Devices.FirstOrDefault(d => d.Id == id);
            if (removed is null)
            {
                return false;
            }

            _state.Devices.Remove(removed);
            SaveLocked();
        }

        Log.Info(LogCategory, $"Removed \"{removed.Name}\".");
        Changed?.Invoke();
        return true;
    }

    public void RemoveAll()
    {
        lock (_gate)
        {
            if (_state.Devices.Count == 0)
            {
                return;
            }

            _state.Devices.Clear();
            SaveLocked();
        }

        Log.Info(LogCategory, "Removed every paired device.");
        Changed?.Invoke();
    }

    /// <summary>Notes when a device was last connected (saved at most every few minutes).</summary>
    public void Touch(string id, DateTimeOffset now)
    {
        lock (_gate)
        {
            var device = _state.Devices.FirstOrDefault(d => d.Id == id);
            if (device is null)
            {
                return;
            }

            bool save = device.LastSeen is not { } last || now - last > TimeSpan.FromMinutes(5);
            device.LastSeen = now;
            if (save)
            {
                SaveLocked();
            }
        }
    }

    /// <summary>Writes pending last-seen times (on quitting).</summary>
    public void Save()
    {
        lock (_gate)
        {
            SaveLocked();
        }
    }

    internal static string CleanName(string? name)
    {
        string cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return "Phone";
        }

        return cleaned.Length > 40 ? cleaned[..40].TrimEnd() : cleaned;
    }

    private static PairedDevice Copy(PairedDevice d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Key = d.Key,
        Paired = d.Paired,
        LastSeen = d.LastSeen,
        Via = d.Via,
    };

    private static State Load(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return new State();
        }

        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path), JsonOptions) ?? new State();
            state.Devices = state.Devices
                .Where(d => !string.IsNullOrEmpty(d.Id) && ConnectCrypto.FromBase64Url(d.Key) is { Length: ConnectCrypto.KeySize })
                .Take(MaximumDevices)
                .ToList();
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn(LogCategory, "connect.json couldn't be read; paired phones will need to pair again.", ex);
            return new State();
        }
    }

    private void SaveLocked()
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, JsonOptions));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogCategory, "connect.json couldn't be saved.", ex);
        }
    }

    private sealed class State
    {
        public string PcId { get; set; } = string.Empty;

        public List<PairedDevice> Devices { get; set; } = [];
    }
}
