using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>
/// Every two seconds: is a game the app in front (one of the library's games, or an app filed under Games)? It tells the
/// shell, which then reads sensors less often while the game plays ("Lighter monitoring while gaming"), and while a
/// library game runs and "Keep the Mac awake while playing" is on it holds a display-sleep assertion (released as soon as
/// the game quits). It only looks at which app is in front; nothing is added to or read from the game.
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

            // Keep awake while any library game runs (checked every 6 s: it walks the process list).
            if (++_tick % 3 == 0 || (_assertion == 0 && gaming && known))
            {
                UpdateKeepAwake(library);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            Log.WarnThrottled("mac-gaming-mode", TimeSpan.FromMinutes(10), "Monitoring", "Checking for a game in front failed.", e);
        }
    }

    private void UpdateKeepAwake(IReadOnlyList<GameEntry> library)
    {
        bool want = _settings.Current.Gaming.KeepAwake && library.Count > 0
            && GameLibrary.FindRunning(library, MacGamingService.RunningPrograms()) is not null;
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
