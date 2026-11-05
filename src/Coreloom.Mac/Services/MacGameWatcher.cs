using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// Every two seconds: is a game the app in front (one of the library's games, or an app filed under Games)? It tells the
/// shell, which then reads sensors less often while the game plays ("Lighter monitoring while gaming"), and while a
/// library game runs and "Keep the Mac awake while playing" is on it holds a display-sleep assertion (released as soon as
/// the game quits). Since 1.9.4 it also hands the Gaming page's Mac boosts the running game every two seconds (High Power
/// Mode automation, the background app governor and the monitors follow it). It only looks at which app is in front and
/// which programs run; nothing is added to or read from the game.
/// </summary>
public sealed class MacGameWatcher : IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, bool> _categoryCache = new(StringComparer.Ordinal);
    private uint _assertion;
    private bool _gaming;
    private int _tick;
    private RunningGame? _running;

    public MacGameWatcher(ShellViewModel shell, SettingsService settings)
    {
        _shell = shell;
        _settings = settings;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Check());
    }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    private void Check()
    {
        try
        {
            var (bundle, pid) = AppKit.FrontmostApplication();
            bool other = pid != 0 && pid != Environment.ProcessId && bundle is not null;
            var library = _shell.Gaming.LibrarySnapshot();
            bool known = other && library.Count > 0 && Darwin.ProcessPath(pid) is { } path
                && GameLibrary.FindRunning(library, [new RunningProcess(pid, path)]) is not null;
            bool filedAsGame = other && !known && IsGameApp(bundle!);
            bool gaming = AdaptiveMonitoring.IsGameInFront(other, fillsScreen: filedAsGame, isLibraryGame: known, isDesktop: false);
            _shell.SetGameInFront(gaming, coversWindow: false);
            if (gaming != _gaming)
            {
                _gaming = gaming;
                Log.Info("Monitoring", gaming ? $"Game in front: {Path.GetFileNameWithoutExtension(bundle)}." : "No game in front.");
            }

            // Which library game runs (every 6 s, or at once when one comes to the front: it walks the process list), for
            // Keep awake and the Mac boosts.
            var gamingSettings = _settings.Current.Gaming;
            bool boosts = MacBoostsController.Tweaks.Any(_shell.Gaming.MacBoosts.IsOn) || gamingSettings.AppsToReopen.Count > 0;
            if (++_tick % 3 == 0 || (gaming && known && _running is null))
            {
                _running = (gamingSettings.KeepAwake || boosts) && library.Count > 0
                    ? GameLibrary.FindRunning(library, MacGamingService.RunningPrograms())
                    : null;
                UpdateKeepAwake();
            }

            if (boosts || _shell.Gaming.MacBoosts.Apps.IsOpen || _shell.Gaming.Details.IsOpen)
            {
                _ = _shell.Gaming.MacBoosts.TickAsync(new MacBoostTick(_running?.Game, _running?.ProcessIds.FirstOrDefault(), gaming), DateTime.UtcNow);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            Log.WarnThrottled("mac-gaming-mode", TimeSpan.FromMinutes(10), "Monitoring", "Checking for a game in front failed.", e);
        }
    }

    private void UpdateKeepAwake()
    {
        bool want = _settings.Current.Gaming.KeepAwake && _running is not null;
        if (want && _assertion == 0)
        {
            _assertion = IOKit.PreventDisplaySleep("Coreloom: a game is running");
            Log.Info("Gaming", "Keeping the Mac awake while the game runs.");
        }
        else if (!want && _assertion != 0)
        {
            IOKit.ReleaseAssertion(_assertion);
            _assertion = 0;
            Log.Info("Gaming", "The game quit; the Mac sleeps as usual.");
        }
    }

    /// <summary>The app's Info.plist files it under Games (remembered per app).</summary>
    private bool IsGameApp(string bundle)
    {
        if (!_categoryCache.TryGetValue(bundle, out bool game))
        {
            game = MacGamingService.DescribeApp(bundle)?.IsGame == true;
            _categoryCache[bundle] = game;
        }

        return game;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_assertion != 0)
        {
            IOKit.ReleaseAssertion(_assertion);
            _assertion = 0;
        }
    }
}
