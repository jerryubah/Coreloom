using System.Text.Json;
using Coreloom.Core.Connect;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Connect;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ConnectDemo;

/// <summary>
/// Coreloom Connect on sample readings: <c>dotnet run --project tools/Coreloom.ConnectDemo -- [--mac] [--port 47101]
/// [--state DIR] [--ticket FILE] [--fast] [--version 1.8.0] [--update 1.8.1]</c>; a PC also runs a game at about 141 FPS for
/// the phone's overlay. Pairing stays open; the QR link and the code are printed and, with
/// --ticket, written to a JSON file whenever they change (for automated tests).
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool mac = args.Contains("--mac");
        int port = Option(args, "--port") is { } p ? int.Parse(p, System.Globalization.CultureInfo.InvariantCulture) : ConnectSettings.DefaultPort;
        string state = Option(args, "--state") ?? Path.Combine(Path.GetTempPath(), "coreloom-connect-demo");
        string? ticketFile = Option(args, "--ticket");
        int interval = args.Contains("--fast") ? 250 : 1000;
        Directory.CreateDirectory(state);

        var clock = new DemoClock();
        var provider = new DemoProvider(mac);
        var engine = new TelemetryEngine([provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(4096), SystemCapabilities.Unknown, clock: clock);
        engine.InitializeAll();
        provider.AddDevices(engine.Devices);
        for (int i = 0; i < 150; i++)
        {
            clock.Advance(1000);
            engine.PollOnce();
        }

        var settings = new AppSettings().Normalize();
        if (mac)
        {
            settings = MacDefaults.Apply(settings, appleSilicon: true);
        }

        settings.Connect.Enabled = true;
        settings.Connect.Port = port;
        var settingsService = new SettingsService(new SettingsStore(Path.Combine(state, "settings.json")), settings);
        var monitor = new MonitorContext
        {
            Engine = engine,
            Summary = mac
                ? new SystemSummary("Jerry's MacBook Pro", "macOS Sequoia", "15.7", "MacBook Pro (16-inch, 2024)", null, "Apple M4 Max", 16, 16, 64L << 30, [])
                : new SystemSummary("JERRY-PC", "Windows 11 Pro", "25H2", "ASRock X870E Taichi", "AMI 3.25", "AMD Ryzen 9 9950X3D 16-Core Processor", 16, 32, 64L << 30, []),
            Processes = new NoProcesses(),
            PrimaryGpuSlot = () => 0,
            Gpus = () => [(0, mac ? "Apple M4 Max" : "NVIDIA GeForce RTX 5090", mac)],
            NetworkAdapters = () => mac ? [("en0", "Wi-Fi", true)] : [("eth0", "Ethernet", true)],
            LibreHardwareMonitorIncluded = !mac,
        };
        var context = new PageContext
        {
            Monitor = monitor,
            Settings = settingsService,
            Timeline = new TimelineViewModel(engine.History),
            Platform = mac ? Coreloom.Core.HostPlatform.MacOS : Coreloom.Core.HostPlatform.Windows,
        };

        string version = Option(args, "--version") ?? "1.8.0";
        var host = new ConnectHost(context, new ConnectEnvironment { DataDirectory = state, FrameRates = mac ? null : new DemoFrameRates() }, version);
        if (Option(args, "--update") is { } update)
        {
            host.UpdateNotice = () => new RemoteUpdate(update, Ready: true);
        }

        await host.ApplyAsync();
        if (host.Status != ConnectStatus.On)
        {
            Console.Error.WriteLine($"Couldn't start: {host.Problem}");
            return 1;
        }

        host.Pairing.Open();
        string? lastTicket = null;
        void ShowTicket()
        {
            var ticket = host.Pairing.Current;
            var address = host.Addresses.FirstOrDefault()?.Url(host.Port) ?? $"http://127.0.0.1:{host.Port}";
            if (ticket is null)
            {
                return;
            }

            string json = JsonSerializer.Serialize(new { url = address, link = $"{address}/#p={ticket.LinkSecret}", secret = ticket.LinkSecret, code = ticket.Code, port = host.Port });
            if (json == lastTicket)
            {
                return;
            }

            lastTicket = json;
            Console.WriteLine($"Pair: {address}/#p={ticket.LinkSecret}  or code {ticket.Code}");
            if (ticketFile is not null)
            {
                File.WriteAllText(ticketFile + ".tmp", json);
                File.Move(ticketFile + ".tmp", ticketFile, overwrite: true);
            }
        }

        host.Changed += ShowTicket;
        Console.WriteLine($"Coreloom Connect demo ({(mac ? "Mac" : "PC")}) on port {host.Port}. Ctrl+C to stop.");
        ShowTicket();

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(interval, stop.Token);
                clock.Advance(1000);
                engine.PollOnce();
                var frame = engine.LatestFrame;
                context.Health.Update(frame);
                host.OnFrame(frame);
                if (!host.Pairing.IsOpen)
                {
                    host.Pairing.Open();
                }

                _ = host.Pairing.Current; // renews an expired code
                ShowTicket();
            }
        }
        catch (OperationCanceledException)
        {
        }

        await host.DisposeAsync();
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private sealed class NoProcesses : IProcessSampler
    {
        public IReadOnlyList<ProcessUsage> Sample() => [];
    }

    private sealed class DemoClock : IMonotonicClock
    {
        private readonly DateTime _start = DateTime.Now.AddMinutes(-3);

        public long NowMilliseconds { get; private set; } = 1_000_000;

        public DateTime LocalNow => _start.AddMilliseconds(NowMilliseconds - 1_000_000);

        public void Advance(long milliseconds) => NowMilliseconds += milliseconds;
    }
}
