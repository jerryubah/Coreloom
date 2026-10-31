using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Coreloom.App.Interop;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Drivers;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Services;

/// <summary>
/// The Drivers page on Windows (since 1.8.6). Installed drivers come from the device tree (configuration manager);
/// updates from three places: Windows Update's driver offers (the Windows Update Agent, as Settings › Windows Update ›
/// Optional updates lists them), NVIDIA's own driver lookup for an NVIDIA card, and the DirectX runtime's files in
/// System32. Installing runs Windows Update's installer, NVIDIA's package silently (-s -noreboot), or Microsoft's DirectX
/// web installer quietly (/Q) — only for the updates the user picked. Coreloom runs as administrator, so none of them
/// asks again.
/// </summary>
internal sealed class WindowsDriverService : IDriverService
{
    private const string LogCategory = "Drivers";

    /// <summary>Windows Update's offers from the last search, by update id (installing needs the same objects).</summary>
    private readonly Dictionary<string, object> _offers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _downloads;
    private readonly string _userAgent;
    private readonly object _gate = new();
    private HttpClient? _http;
    private object? _session;

    public WindowsDriverService(string localDirectory, string userAgent)
    {
        _downloads = Path.Combine(localDirectory, "Drivers");
        _userAgent = userAgent;
    }

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledDriver>> InstalledAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<InstalledDriver>>(DeviceNative.InstalledDrivers, cancellationToken);

    public async Task<DriverScan> CheckAsync(IReadOnlyList<InstalledDriver> installed, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var updates = new List<DriverUpdate>();
        var problems = new List<string>();

        // DirectX first: it's instant.
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var missing = DirectXRuntime.Missing(file => File.Exists(Path.Combine(system, file)));
        if (DirectXRuntime.AsUpdate(missing) is { } directx)
        {
            updates.Add(directx);
        }

        // NVIDIA, for an NVIDIA card.
        var card = installed.FirstOrDefault(d => d.Category == DriverCategory.Graphics
            && ((d.Provider?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false) || d.DeviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)));
        Task<DriverUpdate?> nvidia = card is null ? Task.FromResult<DriverUpdate?>(null) : NvidiaAsync(card, problems, cancellationToken);

        // Windows Update (the slow one).
        progress?.Report("Asking Windows Update for your devices' drivers…");
        try
        {
            var offers = await Task.Run(() => SearchWindowsUpdate(), cancellationToken);
            foreach (var offer in offers)
            {
                var update = DriverCatalog.FromWindowsUpdate(offer, installed);
                if (card is not null && update.Category == DriverCategory.Graphics && (offer.Provider?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue; // NVIDIA's own lookup offers the newer driver for the card
                }

                updates.Add(update);
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Warn(LogCategory, "Windows Update couldn't be asked for drivers.", e);
            problems.Add(WindowsUpdateProblem(e));
        }

        progress?.Report("Asking NVIDIA for the newest graphics driver…");
        if (await nvidia is { } newer)
        {
            updates.Insert(0, newer);
        }

        return new DriverScan(updates, problems);
    }

    public async Task<DriverInstallResult> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        switch (update.Source)
        {
            case DriverSource.Nvidia:
                return await InstallPackageAsync(update, NvidiaDrivers.SilentArguments, "nvidia-" + update.NewVersion + ".exe", progress, cancellationToken,
                    code => NvidiaDrivers.Outcome(code, update.NewVersion ?? string.Empty));
            case DriverSource.DirectX:
                return await InstallPackageAsync(update, DirectXRuntime.QuietArguments, "dxwebsetup.exe", progress, cancellationToken,
                    code => code == 0
                        ? new DriverInstallResult(true, "The DirectX runtime's files are installed.")
                        : new DriverInstallResult(false, $"Microsoft's installer stopped (code {code}). Check your internet connection and try again."));
            default:
                return await Task.Run(() => InstallWindowsUpdate(update, progress), cancellationToken);
        }
    }

    public bool OpenNvidiaApp()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string app in new[]
        {
            Path.Combine(programs, "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe"),
            Path.Combine(programs, "NVIDIA Corporation", "NVIDIA GeForce Experience", "NVIDIA GeForce Experience.exe"),
        })
        {
            if (File.Exists(app) && ShellLaunch.Start(app))
            {
                Log.Info(LogCategory, $"Opened {Path.GetFileNameWithoutExtension(app)}.");
                return true;
            }
        }

        return false;
    }

    public void OpenUrl(string url) => new ShellService().OpenUrl(url);

    // ---- NVIDIA ---------------------------------------------------------------------------------------

    private async Task<DriverUpdate?> NvidiaAsync(InstalledDriver card, List<string> problems, CancellationToken cancellationToken)
    {
        try
        {
            var http = Http();
            string list = await http.GetStringAsync(NvidiaDrivers.ProductListUrl, cancellationToken);
            var product = NvidiaDrivers.Find(NvidiaDrivers.ParseProducts(list), card.DeviceName);
            if (product is null)
            {
                Log.Info(LogCategory, $"{card.DeviceName} isn't in NVIDIA's driver lookup.");
                lock (problems)
                {
                    problems.Add($"NVIDIA's driver lookup doesn't list the {card.DeviceName}; the NVIDIA app can check it.");
                }

                return null;
            }

            string json = await http.GetStringAsync(NvidiaDrivers.LookupUrl(product), cancellationToken);
            var release = NvidiaDrivers.ParseRelease(json);
            if (release is null)
            {
                lock (problems)
                {
                    problems.Add("NVIDIA's driver lookup didn't answer with a driver; the NVIDIA app can check it.");
                }

                return null;
            }

            Log.Info(LogCategory, $"NVIDIA's newest driver for the {product.Name}: {release.Version} (installed: {DriverCatalog.NvidiaVersion(card.Version) ?? card.Version}).");
            return NvidiaDrivers.AsUpdate(release, card);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warn(LogCategory, "NVIDIA's driver lookup couldn't be reached.", e);
            lock (problems)
            {
                problems.Add("NVIDIA couldn't be reached to check for a newer graphics driver.");
            }

            return null;
        }
    }

    private async Task<DriverInstallResult> InstallPackageAsync(DriverUpdate update, string arguments, string fileName, IProgress<string>? progress,
        CancellationToken cancellationToken, Func<int, DriverInstallResult> outcome)
    {
        if (update.DownloadUrl is not { } url || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new DriverInstallResult(false, "There's no download for it.");
        }

        Directory.CreateDirectory(_downloads);
        string file = Path.Combine(_downloads, string.Concat(fileName.Split(Path.GetInvalidFileNameChars())));
        try
        {
            progress?.Report("Downloading…");
            using (var response = await Http().GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(file);
                var buffer = new byte[1 << 20];
                long done = 0;
                int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    if (total is > 0 && (int)(done * 100 / total.Value) is int percent && percent != lastPercent && percent % 5 == 0)
                    {
                        lastPercent = percent;
                        progress?.Report($"Downloading… {percent} %");
                    }
                }
            }

            Log.Info(LogCategory, $"Downloaded {update.Name} {update.NewVersion} ({new FileInfo(file).Length / (1024 * 1024)} MB).");
            progress?.Report(update.BlanksScreen ? "Installing… the screen may go dark for a moment" : "Installing…");
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, WorkingDirectory = _downloads })
                ?? throw new InvalidOperationException("The installer didn't start.");
            await process.WaitForExitAsync(cancellationToken);
            var result = outcome(process.ExitCode);
            Log.Info(LogCategory, $"{update.Name} {update.NewVersion}: installer exited with {process.ExitCode}.");
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Warn(LogCategory, $"Installing {update.Name} {update.NewVersion} failed.", e);
            return new DriverInstallResult(false, e is HttpRequestException or TaskCanceledException
                ? "The download stopped. Check your internet connection and try again."
                : "Its installer couldn't run: " + e.Message);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // removed with the folder next time
            }
        }
    }

    private HttpClient Http()
    {
        lock (_gate)
        {
            if (_http is null)
            {
                _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                _http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            }

            return _http;
        }
    }

    // ---- Windows Update -----------------------------------------------------------------------------

    private dynamic Session()
    {
        lock (_gate)
        {
            _session ??= Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;
            return _session;
        }
    }

    /// <summary>Driver updates Windows Update offers this PC that aren't installed or hidden.</summary>
    private List<WindowsUpdateDriver> SearchWindowsUpdate()
    {
        dynamic session = Session();
        session.ClientApplicationID = "Coreloom";
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
        var offers = new List<WindowsUpdateDriver>();
        lock (_gate)
        {
            _offers.Clear();
        }

        int count = result.Updates.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic update = result.Updates.Item(i);
            string id = update.Identity.UpdateID;
            var offer = new WindowsUpdateDriver(id, (string)update.Title)
            {
                DriverClass = Try(() => (string?)update.DriverClass),
                Provider = Try(() => (string?)update.DriverProvider) ?? Try(() => (string?)update.DriverManufacturer),
                Model = Try(() => (string?)update.DriverModel),
                HardwareId = Try(() => (string?)update.DriverHardwareID),
                Date = Try<DateTime?>(() => (DateTime)update.DriverVerDate),
                SizeBytes = Try<long>(() => Convert.ToInt64((object)update.MaxDownloadSize, System.Globalization.CultureInfo.InvariantCulture)),
            };
            offers.Add(offer);
            lock (_gate)
            {
                _offers[id] = update;
            }
        }

        Log.Info(LogCategory, $"Windows Update offers {offers.Count} driver {(offers.Count == 1 ? "update" : "updates")}.");
        return offers;
    }

    private DriverInstallResult InstallWindowsUpdate(DriverUpdate update, IProgress<string>? progress)
    {
        object? offer;
        lock (_gate)
        {
            _offers.TryGetValue(update.Id, out offer);
        }

        if (offer is null)
        {
            return new DriverInstallResult(false, "Windows Update no longer offers it. Check again.");
        }

        try
        {
            dynamic session = Session();
            dynamic item = offer;
            if (!(bool)item.EulaAccepted)
            {
                item.AcceptEula();
            }

            dynamic list = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)!)!;
            list.Add(item);
            progress?.Report("Downloading…");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = list;
            dynamic downloaded = downloader.Download();
            if ((int)downloaded.ResultCode is not (2 or 3))
            {
                return new DriverInstallResult(false, "Windows Update couldn't download it. Try again later.");
            }

            progress?.Report(update.BlanksScreen ? "Installing… the screen may go dark for a moment" : "Installing…");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = list;
            dynamic installed = installer.Install();
            int code = installed.ResultCode;
            bool restart = installed.RebootRequired;
            Log.Info(LogCategory, $"Windows Update installed {update.Name} {update.NewVersion}: result {code}{(restart ? ", restart needed" : string.Empty)}.");
            return code switch
            {
                2 or 3 => new DriverInstallResult(true, restart ? "Installed. Restart Windows to finish." : "Installed.", restart),
                5 => new DriverInstallResult(false, "Windows Update stopped before it finished."),
                _ => new DriverInstallResult(false, "Windows Update couldn't install it. Settings › Windows Update › Update history says why."),
            };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Warn(LogCategory, $"Windows Update failed to install {update.Name}.", e);
            return new DriverInstallResult(false, WindowsUpdateProblem(e));
        }
    }

    private static string WindowsUpdateProblem(Exception e) => e is System.Runtime.InteropServices.COMException com
        ? unchecked((uint)com.HResult) switch
        {
            0x8024402C or 0x80244022 or 0x8024401C or 0x80072EE7 or 0x80072EFD => "Windows Update couldn't be reached. Check your internet connection.",
            0x8024001E or 0x80240016 => "Windows Update is busy with another update. Try again in a few minutes.",
            0x80070422 => "The Windows Update service is turned off.",
            uint code => $"Windows Update couldn't be asked (0x{code:X8}).",
        }
        : "Windows Update couldn't be asked.";

    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.Runtime.InteropServices.COMException or InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }
}
