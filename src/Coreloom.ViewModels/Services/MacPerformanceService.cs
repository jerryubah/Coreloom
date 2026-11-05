using Coreloom.Core.Gaming;

namespace Coreloom.ViewModels.Services;

/// <summary>The charger's power: whether this Mac has High Power Mode, whether it's plugged in, and its mode there.</summary>
/// <param name="CanSwitchSilently">Coreloom may set the charger's power mode without asking (its sudoers rule is in place).</param>
public sealed record MacPowerState(bool HasHighPowerMode, bool OnCharger, int ChargerMode, bool CanSwitchSilently);

/// <summary>A process running through Rosetta 2.</summary>
public sealed record TranslatedProcess(int ProcessId, string Name);

/// <summary>
/// What the Mac's Gaming boosts read and change (since 1.9.4): power mode, memory, heat, the cores' work, MetalFX in
/// games, the displays, programs under Rosetta 2 and the apps running. The Mac app implements it; elsewhere it's the
/// unsupported stand-in.
/// </summary>
public interface IMacPerformanceService
{
    bool IsSupported { get; }

    /// <summary>Apple silicon (performance and efficiency cores, Rosetta 2).</summary>
    bool IsAppleSilicon { get; }

    Task<MacPowerState> PowerAsync(CancellationToken cancellationToken);

    /// <summary>Puts Coreloom's sudoers rule in place (or removes it), after macOS asks for the password.</summary>
    Task<TweakResult> AllowPowerSwitchingAsync(bool allow, CancellationToken cancellationToken);

    /// <summary>Sets the charger's power mode without asking, through the rule; false when that didn't work.</summary>
    Task<bool> SetChargerPowerModeAsync(int mode, CancellationToken cancellationToken);

    MemorySnapshot? Memory();

    ThermalPressure? ThermalState();

    /// <summary>Seconds per tick of <see cref="Coreloom.Core.Gaming.CoreTimes.CpuTicks"/>.</summary>
    double TicksToSeconds { get; }

    /// <summary>Every process's processor time so far, with the part on performance cores and per QoS class.</summary>
    IReadOnlyList<CoreTimes> CoreTimes();

    Task<MetalFxFeatures> MetalFxAsync(GameEntry game, CancellationToken cancellationToken);

    IReadOnlyList<DisplayDescription> Displays();

    Task<CpuArchitecture> ArchitectureAsync(GameEntry game, CancellationToken cancellationToken);

    IReadOnlyList<TranslatedProcess> TranslatedProcesses();

    IReadOnlyList<RunningAppInfo> RunningApps();

    /// <summary>Asks an app to quit, as Quit in its menu would (it may ask to save first).</summary>
    Task<bool> QuitAsync(RunningAppInfo app, CancellationToken cancellationToken);

    /// <summary>Opens an app again in the background.</summary>
    bool Reopen(string bundlePath);
}

/// <summary>Where the Mac's boosts don't apply.</summary>
public sealed class UnsupportedMacPerformanceService : IMacPerformanceService
{
    public bool IsSupported => false;

    public bool IsAppleSilicon => false;

    public double TicksToSeconds => 1e-9;

    public Task<MacPowerState> PowerAsync(CancellationToken cancellationToken) => Task.FromResult(new MacPowerState(false, false, 0, false));

    public Task<TweakResult> AllowPowerSwitchingAsync(bool allow, CancellationToken cancellationToken) =>
        Task.FromResult(new TweakResult(false, "Only on a Mac."));

    public Task<bool> SetChargerPowerModeAsync(int mode, CancellationToken cancellationToken) => Task.FromResult(false);

    public MemorySnapshot? Memory() => null;

    public ThermalPressure? ThermalState() => null;

    public IReadOnlyList<CoreTimes> CoreTimes() => [];

    public Task<MetalFxFeatures> MetalFxAsync(GameEntry game, CancellationToken cancellationToken) => Task.FromResult(MetalFxFeatures.None);

    public IReadOnlyList<DisplayDescription> Displays() => [];

    public Task<CpuArchitecture> ArchitectureAsync(GameEntry game, CancellationToken cancellationToken) => Task.FromResult(CpuArchitecture.Unknown);

    public IReadOnlyList<TranslatedProcess> TranslatedProcesses() => [];

    public IReadOnlyList<RunningAppInfo> RunningApps() => [];

    public Task<bool> QuitAsync(RunningAppInfo app, CancellationToken cancellationToken) => Task.FromResult(false);

    public bool Reopen(string bundlePath) => false;
}
