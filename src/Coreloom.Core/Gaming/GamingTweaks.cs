using System.Globalization;
using System.Text.RegularExpressions;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Gaming;

/// <summary>The Windows and driver settings the Gaming page can switch.</summary>
public enum GamingTweak
{
    /// <summary>Hardware-accelerated GPU scheduling (HAGS).</summary>
    GpuScheduling,

    /// <summary>The Ultimate Performance power plan.</summary>
    UltimatePower,

    /// <summary>Windows Game Mode.</summary>
    GameMode,

    /// <summary>Windows 11's optimizations for windowed and borderless games (flip-model presentation).</summary>
    WindowedOptimizations,

    /// <summary>Xbox Game Bar's background recording ("Record what happened") turned off.</summary>
    NoBackgroundRecording,

    /// <summary>NVIDIA power management mode "Prefer maximum performance".</summary>
    MaxGpuPerformance,

    /// <summary>NVIDIA shader cache size "Unlimited".</summary>
    UnlimitedShaderCache,
}

/// <summary>Whether a tweak can be used here, whether it is on, and whether a restart is still needed for it.</summary>
public sealed record TweakStatus(bool IsSupported, bool IsOn, bool NeedsRestart = false, string? Note = null)
{
    public static TweakStatus Unsupported(string note) => new(false, false, false, note);
}

/// <summary>The result of switching a tweak or the frame cap.</summary>
public sealed record TweakResult(bool Success, string Message, bool NeedsRestart = false);

/// <summary>Size of one shader cache folder.</summary>
public sealed record ShaderCacheInfo(string Name, string Path, long Bytes, int Files);

/// <summary>Everything the Gaming page shows about this PC's settings.</summary>
public sealed record GamingStatus
{
    public static GamingStatus Unavailable { get; } = new() { Tweaks = new Dictionary<GamingTweak, TweakStatus>() };

    public required IReadOnlyDictionary<GamingTweak, TweakStatus> Tweaks { get; init; }

    /// <summary>An NVIDIA graphics card and driver were found (frame pacer, shader cache size and power mode use its driver).</summary>
    public bool HasNvidiaDriver { get; init; }

    /// <summary>The driver's frame-rate limit for all games (0 = off).</summary>
    public int FrameCap { get; init; }

    /// <summary>Refresh rate of the main display (Hz, 0 = unknown).</summary>
    public int RefreshRate { get; init; }

    /// <summary>"581.57" (null when unknown).</summary>
    public string? DriverVersion { get; init; }

    public IReadOnlyList<ShaderCacheInfo> ShaderCaches { get; init; } = [];

    public TweakStatus Tweak(GamingTweak tweak) => Tweaks.TryGetValue(tweak, out var status) ? status : TweakStatus.Unsupported("Not available on this PC.");
}

/// <summary>A Windows power plan from <c>powercfg /list</c>.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Reads <c>powercfg</c> output (language-independent: it goes by the GUIDs, names in brackets and the active star).</summary>
public static partial class PowerPlans
{
    /// <summary>The Ultimate Performance template Windows copies from.</summary>
    public static readonly Guid UltimateTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public static List<PowerPlan> Parse(string? output)
    {
        var plans = new List<PowerPlan>();
        if (string.IsNullOrEmpty(output))
        {
            return plans;
        }

        foreach (string line in output.Split('\n'))
        {
            var m = PlanLine().Match(line);
            if (m.Success && Guid.TryParse(m.Groups["id"].Value, out var id))
            {
                plans.Add(new PowerPlan(id, m.Groups["name"].Value.Trim(), m.Groups["star"].Success));
            }
        }

        return plans;
    }

    /// <summary>The GUID of the plan <c>powercfg -duplicatescheme</c> created.</summary>
    public static Guid? ParseCreated(string? output)
    {
        var m = output is null ? Match.Empty : AnyGuid().Match(output);
        return m.Success && Guid.TryParse(m.Value, out var id) ? id : null;
    }

    /// <summary>The Ultimate Performance plan: the one Coreloom made, the template itself, or one named so.</summary>
    public static PowerPlan? FindUltimate(IReadOnlyList<PowerPlan> plans, Guid? remembered)
    {
        return plans.FirstOrDefault(p => remembered is { } r && p.Id == r)
            ?? plans.FirstOrDefault(p => p.Id == UltimateTemplate)
            ?? plans.FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((?<name>[^)]*)\)\s*(?<star>\*)?")]
    private static partial Regex PlanLine();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex AnyGuid();
}

/// <summary>
/// Hardware-accelerated GPU scheduling lives in <c>HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers</c>, value
/// <c>HwSchMode</c>: 2 on, 1 off. Windows reads it when it starts.
/// </summary>
public static class GpuScheduling
{
    public const string Key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string Value = "HwSchMode";
    public const int On = 2;
    public const int Off = 1;

    /// <summary>What Windows will use after the next restart, given the registry value and the driver's default.</summary>
    public static bool WillBeOn(int? registryValue, bool enabledByDefault) => registryValue switch
    {
        On => true,
        Off => false,
        _ => enabledByDefault,
    };

    public static TweakStatus Status(bool supported, bool enabledNow, int? registryValue, bool enabledByDefault)
    {
        if (!supported)
        {
            return TweakStatus.Unsupported("Your graphics card or driver doesn't support it.");
        }

        bool willBeOn = WillBeOn(registryValue, enabledByDefault);
        return new TweakStatus(true, willBeOn, willBeOn != enabledNow);
    }
}

/// <summary>
/// Windows 11's per-user DirectX settings (Settings › Display › Graphics › Advanced): <c>HKCU\Software\Microsoft\DirectX\
/// UserGpuPreferences</c>, value <c>DirectXUserGlobalSettings</c>, a list like <c>SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;</c>.
/// </summary>
public static class DirectXUserSettings
{
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string Value = "DirectXUserGlobalSettings";
    public const string SwapEffectUpgrade = "SwapEffectUpgradeEnable";

    public static List<KeyValuePair<string, string>> Parse(string? value)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (string part in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0)
            {
                list.Add(new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
            }
        }

        return list;
    }

    public static bool IsOn(string? value, string key) =>
        Parse(value).Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value == "1");

    /// <summary>Sets one entry and keeps the others as they were.</summary>
    public static string Set(string? value, string key, bool on)
    {
        var all = Parse(value);
        string name = all.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key ?? key; // keep Windows' spelling
        var list = all.Where(p => !p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(new(name, on ? "1" : "0"));
        return string.Concat(list.Select(p => $"{p.Key}={p.Value};"));
    }
}

/// <summary>Frame-rate limits for the frame pacer.</summary>
public static class FrameCap
{
    public const int Minimum = 20;

    /// <summary>The highest limit the NVIDIA driver accepts is 1023; anything above 1000 isn't useful.</summary>
    public const int Maximum = 1000;

    /// <summary>
    /// The automatic limit: a little under the refresh rate so frames stay inside the variable-refresh (G-SYNC /
    /// FreeSync) range and never queue up behind V-Sync. Same formula as NVIDIA Reflex: refresh − refresh² ÷ 3600
    /// (144 Hz → 138, 165 Hz → 157, 240 Hz → 224).
    /// </summary>
    public static int AutoTarget(int refreshHz)
    {
        if (refreshHz <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Floor(refreshHz - (refreshHz * (double)refreshHz / 3600.0)), Minimum, Maximum);
    }

    /// <summary>The limit to use: <paramref name="target"/> (0 = automatic).</summary>
    public static int Resolve(int target, int refreshHz) => target <= 0 ? AutoTarget(refreshHz) : Clamp(target);

    public static int Clamp(int fps) => fps <= 0 ? 0 : Math.Clamp(fps, Minimum, Maximum);

    /// <summary>Common limits up to the display's refresh rate (and its automatic limit).</summary>
    public static List<int> Presets(int refreshHz)
    {
        int max = refreshHz > 0 ? refreshHz : 240;
        var list = new List<int> { 30, 45, 60, 72, 90, 100, 120, 141, 144, 160, 165, 200, 237, 240, 300, 360, 480, 500 }
            .Where(f => f <= max)
            .ToList();
        if (!list.Contains(max) && refreshHz > 0)
        {
            list.Add(max);
        }

        return list;
    }

    /// <summary>Time per frame at a limit ("7.25 ms").</summary>
    public static string FrameTimeText(int fps) => fps <= 0 ? "—" : (1000.0 / fps).ToString("0.##", CultureInfo.CurrentCulture) + " ms";
}

/// <summary>How evenly frames arrive.</summary>
public enum PacingGrade
{
    Unknown,
    Smooth,
    Uneven,
    Stuttering,
}

/// <summary>
/// Grades frame pacing from the frame rate and its 1% low: when the slowest frames are close to the average, frames
/// arrive evenly; a 1% low far below the average means stutters.
/// </summary>
public static class FramePacing
{
    public static PacingGrade Grade(double fps, double lowFps)
    {
        if (!(fps > 1) || !double.IsFinite(lowFps) || lowFps <= 0)
        {
            return PacingGrade.Unknown;
        }

        double ratio = lowFps / fps;
        return ratio >= 0.8 ? PacingGrade.Smooth : ratio >= 0.6 ? PacingGrade.Uneven : PacingGrade.Stuttering;
    }

    /// <summary>The 1% low as a share of the average (0–100).</summary>
    public static int Consistency(double fps, double lowFps) =>
        fps > 1 && lowFps > 0 ? (int)Math.Round(Math.Clamp(lowFps / fps, 0, 1) * 100) : 0;

    public static string Text(PacingGrade grade) => grade switch
    {
        PacingGrade.Smooth => "Smooth",
        PacingGrade.Uneven => "Uneven",
        PacingGrade.Stuttering => "Stuttering",
        _ => "—",
    };

    public static ThresholdLevel Level(PacingGrade grade) => grade switch
    {
        PacingGrade.Smooth => ThresholdLevel.Cool,
        PacingGrade.Uneven => ThresholdLevel.Elevated,
        PacingGrade.Stuttering => ThresholdLevel.Critical,
        _ => ThresholdLevel.Normal,
    };
}

/// <summary>Where graphics drivers, DirectX and Steam keep compiled shaders.</summary>
public static class ShaderCaches
{
    public static List<(string Name, string Path)> Locations(string localAppData, string programData, IEnumerable<string> steamLibraries)
    {
        var list = new List<(string, string)>
        {
            ("DirectX", Path.Combine(localAppData, "D3DSCache")),
            ("NVIDIA DirectX", Path.Combine(localAppData, "NVIDIA", "DXCache")),
            ("NVIDIA OpenGL and Vulkan", Path.Combine(localAppData, "NVIDIA", "GLCache")),
            ("NVIDIA (older drivers)", Path.Combine(programData, "NVIDIA Corporation", "NV_Cache")),
            ("AMD DirectX", Path.Combine(localAppData, "AMD", "DxCache")),
            ("AMD DirectX 12", Path.Combine(localAppData, "AMD", "DxcCache")),
            ("AMD Vulkan", Path.Combine(localAppData, "AMD", "VkCache")),
            ("Intel", Path.Combine(localAppData, "Intel", "ShaderCache")),
        };
        foreach (string library in steamLibraries)
        {
            list.Add(("Steam shader pre-cache", SteamLibrary.ShaderCacheFolder(library)));
        }

        return list;
    }

    /// <summary>
    /// What to say about the driver: a new driver means games compile their shaders again on their next start.
    /// </summary>
    public static string? DriverChangeText(string? previous, string? current)
    {
        if (string.IsNullOrEmpty(previous) || string.IsNullOrEmpty(current) || string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"The graphics driver changed from {previous} to {current}. Games compile their shaders again the first time you play them, so expect brief stutter for the first few minutes.";
    }
}
