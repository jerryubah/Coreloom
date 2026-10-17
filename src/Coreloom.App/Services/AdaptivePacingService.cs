using System.IO;
using System.Windows.Threading;
using Coreloom.App.Overlay;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Services;

/// <summary>
/// Feeds adaptive frame pacing once a second while the user has it on: the game in front (from gaming mode), its newest
/// second of frames (Windows' graphics events, the same measurement as the overlay's frame counter; nothing is added to
/// the game) and how busy the graphics card is. Measuring stops 30 seconds after the last game leaves the front, and
/// does nothing at all while adaptive pacing is off.
/// </summary>
internal sealed class AdaptivePacingService : IDisposable
{
    private const string Owner = "adaptive pacing";
    private const string LogCategory = "Gaming";
    private static readonly TimeSpan KeepMeasuring = TimeSpan.FromSeconds(30);

    private readonly ShellViewModel _shell;
    private readonly GamingModeService _gamingMode;
    private readonly EtwFrameRateSource _frames;
    private readonly Func<bool> _isOn;
    private readonly Func<double> _gpuLoad;
    private readonly DispatcherTimer _timer;
    private readonly double[] _buffer = new double[2048];
    private DateTime _lastGame = DateTime.MinValue;
    private bool _acquired;
    private bool _wasOn;
    private bool _ticking;

    public AdaptivePacingService(ShellViewModel shell, GamingModeService gamingMode, EtwFrameRateSource frames, Func<bool> isOn, Func<double> gpuLoad, Dispatcher dispatcher)
    {
        _shell = shell;
        _gamingMode = gamingMode;
        _frames = frames;
        _isOn = isOn;
        _gpuLoad = gpuLoad;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => _ = TickAsync();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        Release();
    }

    private async Task TickAsync()
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            var adaptive = _shell.Gaming.Adaptive;
            if (!_isOn())
            {
                Release();
                if (_wasOn)
                {
                    _wasOn = false;
                    await adaptive.TickAsync(default); // ends the session in progress
                }

                return;
            }

            _wasOn = true;
            if (_gamingMode.GameProcessId is not int pid)
            {
                if (_acquired && DateTime.UtcNow - _lastGame > KeepMeasuring)
                {
                    Release();
                }

                await adaptive.TickAsync(new PacingTick(null, null, null));
                return;
            }

            _lastGame = DateTime.UtcNow;
            if (!_acquired)
            {
                _acquired = true;
                _frames.Acquire(Owner);
            }

            string? problem = _frames.IsRunning ? null : _frames.Problem ?? "Windows' graphics events aren't available.";
            PacingSecond? second = null;
            if (problem is null)
            {
                int n = _frames.RecentFrameTimes(pid, _buffer, out double age);
                if (n >= 2 && age < 1500)
                {
                    second = PacingSecond.From(_buffer.AsSpan(0, n), _gpuLoad());
                }
            }

            await adaptive.TickAsync(new PacingTick(pid, _gamingMode.GameProgram, second, problem));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or IOException)
        {
            Log.WarnThrottled("adaptive-pacing", TimeSpan.FromMinutes(10), LogCategory, "Adaptive pacing couldn't read the game's frames.", e);
        }
        finally
        {
            _ticking = false;
        }
    }

    private void Release()
    {
        if (_acquired)
        {
            _acquired = false;
            _frames.Release(Owner);
        }
    }
}
