using System.Windows.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.Hardware.Gaming;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Services;

/// <summary>
/// Feeds intelligent VRAM management every two seconds while the user has it on (since 1.8.4; it took adaptive frame
/// pacing's place): the game in front (from gaming mode), its dedicated and shared graphics memory (Windows' GPU Process
/// Memory counters, nothing is added to the game) and what's in use on the card. The counters are opened only while
/// it's on and closed 30 seconds after the last game leaves the front.
/// </summary>
internal sealed class VramWatchService : IDisposable
{
    private const string LogCategory = "Gaming";
    private static readonly TimeSpan KeepOpen = TimeSpan.FromSeconds(30);

    private readonly ShellViewModel _shell;
    private readonly GamingModeService _gamingMode;
    private readonly Func<bool> _isOn;
    private readonly Func<(double Used, double Total)> _card;
    private readonly DispatcherTimer _timer;
    private GpuProcessMemory? _counters;
    private string? _problem;
    private DateTime _lastGame = DateTime.MinValue;
    private bool _wasOn;

    public VramWatchService(ShellViewModel shell, GamingModeService gamingMode, Func<bool> isOn, Func<(double Used, double Total)> card, Dispatcher dispatcher)
    {
        _shell = shell;
        _gamingMode = gamingMode;
        _isOn = isOn;
        _card = card;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        Close();
    }

    private void Tick()
    {
        var vram = _shell.Gaming.Vram;
        var now = DateTime.UtcNow;
        try
        {
            if (!_isOn())
            {
                Close();
                if (_wasOn)
                {
                    _wasOn = false;
                    vram.Tick(default, now); // ends the session in progress
                }

                return;
            }

            _wasOn = true;
            if (_gamingMode.GameProcessId is not int pid || _gamingMode.GameProgram is not { } program)
            {
                if (_counters is not null && now - _lastGame > KeepOpen)
                {
                    Close();
                }

                vram.Tick(new VramTick(null, null, null), now);
                return;
            }

            _lastGame = now;
            if (_counters is null && _problem is null)
            {
                _counters = GpuProcessMemory.Open(out _problem);
                if (_problem is not null)
                {
                    Log.Warn(LogCategory, $"VRAM management can't read graphics memory: {_problem}");
                }
            }

            VramSample? sample = null;
            if (_counters?.Read([pid]) is { } game)
            {
                var (used, total) = _card();
                long cardUsed = double.IsFinite(used) && used > 0 ? (long)used : -1;
                long cardTotal = double.IsFinite(total) && total > 0 ? (long)total : -1;
                sample = new VramSample(game.Dedicated, game.Shared, cardUsed, cardTotal);
            }

            vram.Tick(new VramTick(pid, program, sample, _counters is null ? _problem : null), now);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        {
            Log.WarnThrottled("vram-watch-service", TimeSpan.FromMinutes(10), LogCategory, "VRAM management couldn't read the game's graphics memory.", e);
        }
    }

    private void Close()
    {
        _counters?.Dispose();
        _counters = null;
        _problem = null;
    }
}
