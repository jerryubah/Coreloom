using System.Globalization;

namespace Coreloom.Core.Gaming;

/// <summary>How one of a check's parts looks: fine, worth knowing, or in the way.</summary>
public enum CheckTone
{
    Good,
    Info,
    Problem,
}

/// <summary>One line of a check: "Graphics card · GPU decompression", its tone, and what to do about it.</summary>
public sealed record ReadinessRow(string Name, string Value, CheckTone Tone, string? Detail = null);

/// <summary>
/// Whether Windows' faster storage path (BypassIO, Windows 11) works on a drive: null when it can't be asked; otherwise
/// supported or not, with the driver in the way and Windows' own reason.
/// </summary>
public sealed record BypassIoState(bool Supported, string? Driver = null, string? Reason = null);

/// <summary>A drive that holds games: its root ("D:\"), how it's connected ("NVMe", "SATA"…), solid state or not, BypassIO.</summary>
public sealed record GameDrive(string Root, string Bus, bool? IsSsd, BypassIoState? BypassIo);

/// <summary>A library game that ships Microsoft's DirectStorage runtime (dstorage.dll) and the drive it's on.</summary>
public sealed record DirectStorageGame(string Id, string Name, string Root);

/// <summary>What the check is made from (gathered by the platform service; evaluated here so it can be tested).</summary>
/// <param name="WindowsBuild">Windows' build number (22000 and up is Windows 11).</param>
/// <param name="GpuName">The graphics card the check is for.</param>
/// <param name="ShaderModel">Its highest Direct3D 12 shader model (0x60 = 6.0), or null when Direct3D 12 isn't available.</param>
/// <param name="Drives">The drives holding games (or the system drive when there are none).</param>
/// <param name="Games">The games that use DirectStorage.</param>
public sealed record DirectStorageInputs(int WindowsBuild, string? GpuName, int? ShaderModel, IReadOnlyList<GameDrive> Drives, IReadOnlyList<DirectStorageGame> Games);

/// <summary>Whether DirectStorage can do its job here.</summary>
public enum DirectStorageState
{
    /// <summary>Still being checked (or not a Windows PC).</summary>
    Unknown,

    /// <summary>No DirectX 12 graphics card with Shader Model 6.0: no GPU decompression.</summary>
    NotAvailable,

    /// <summary>It works, but something slows it down (a drive that isn't NVMe, BypassIO blocked).</summary>
    PartlyReady,

    Ready,
}

/// <summary>The check's outcome: state, a one-line summary, the first thing in the way, and every line for its details.</summary>
public sealed record DirectStorageReport(DirectStorageState State, string Summary, string? Problem, IReadOnlyList<ReadinessRow> Rows, IReadOnlyList<DirectStorageGame> Games)
{
    public static DirectStorageReport Unknown { get; } = new(DirectStorageState.Unknown, "Checking…", null, [], []);
}

/// <summary>
/// DirectStorage readiness (since 1.8.4). Games load their files with DirectStorage when they ship Microsoft's runtime;
/// what makes it fast is on the PC: a DirectX 12 graphics card with Shader Model 6.0 decompresses on the GPU, an NVMe
/// drive has the bandwidth, and Windows 11's BypassIO storage path saves processor time (Microsoft's DirectStorage 1.1
/// announcement). Nothing here can be switched on — Coreloom checks each part, finds the games that use it, and says
/// what's in the way.
/// </summary>
public static class DirectStorageCheck
{
    /// <summary>Direct3D 12 Shader Model 6.0, the least GPU decompression needs.</summary>
    public const int MinimumShaderModel = 0x60;

    /// <summary>Microsoft's DirectStorage runtime files that games ship next to their programs.</summary>
    public static readonly IReadOnlyList<string> RuntimeFiles = ["dstorage.dll", "dstoragecore.dll"];

    public static DirectStorageReport Evaluate(DirectStorageInputs inputs)
    {
        var rows = new List<ReadinessRow>();
        bool windows11 = inputs.WindowsBuild >= 22000;

        // The graphics card: GPU decompression.
        int model = inputs.ShaderModel ?? 0;
        if (inputs.ShaderModel is null || model < MinimumShaderModel)
        {
            string why = inputs.ShaderModel is null
                ? "Direct3D 12 isn't available for this graphics card."
                : $"It supports Shader Model {ShaderModelText(model)}; GPU decompression needs 6.0.";
            rows.Add(new ReadinessRow("Graphics card", "No GPU decompression", CheckTone.Problem, why));
            return new DirectStorageReport(DirectStorageState.NotAvailable, "Needs a DirectX 12 graphics card with Shader Model 6.0.", why, rows, inputs.Games);
        }

        rows.Add(new ReadinessRow("Graphics card", "GPU decompression", CheckTone.Good,
            $"{inputs.GpuName ?? "The graphics card"} supports DirectX 12 with Shader Model {ShaderModelText(model)}."));

        // Windows: both work; Windows 11 adds the faster storage path.
        rows.Add(windows11
            ? new ReadinessRow("Windows", "Windows 11", CheckTone.Good, "Windows 11's storage path (BypassIO) lets DirectStorage skip work in the file system.")
            : new ReadinessRow("Windows", "Windows 10", CheckTone.Info, "DirectStorage works on Windows 10; Windows 11 adds a faster storage path (BypassIO)."));

        // The drives the games are on.
        foreach (var drive in inputs.Drives)
        {
            string name = $"Drive {DriveName(drive.Root)}";
            var games = inputs.Games.Where(g => SameRoot(g.Root, drive.Root)).Select(g => g.Name).ToList();
            string holds = games.Count == 0 ? string.Empty : $" {JoinNames(games)} {(games.Count == 1 ? "is" : "are")} on it.";
            if (string.Equals(drive.Bus, "NVMe", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new ReadinessRow(name, "NVMe", CheckTone.Good, $"Fast enough for DirectStorage.{holds}"));
            }
            else if (drive.IsSsd == false)
            {
                rows.Add(new ReadinessRow(name, "Hard drive", CheckTone.Problem, $"DirectStorage games load much faster from an NVMe drive.{holds}"));
            }
            else if (drive.Bus is "SATA" or "ATA" || drive.IsSsd == true)
            {
                rows.Add(new ReadinessRow(name, $"{(drive.Bus is "Unknown" ? string.Empty : drive.Bus + " ")}SSD".Trim(), CheckTone.Problem,
                    $"DirectStorage works, but games load faster from an NVMe drive.{holds}"));
            }
            else
            {
                rows.Add(new ReadinessRow(name, drive.Bus is "Unknown" or "" ? "Not known" : drive.Bus, CheckTone.Info,
                    $"Windows doesn't say what kind of drive this is.{holds}"));
            }

            if (windows11 && drive.BypassIo is { } bypass)
            {
                rows.Add(bypass.Supported
                    ? new ReadinessRow($"BypassIO on {DriveName(drive.Root)}", "Supported", CheckTone.Good, "Reads skip the file system's extra layers.")
                    : new ReadinessRow($"BypassIO on {DriveName(drive.Root)}", "Not supported", CheckTone.Problem, BypassText(bypass)));
            }
        }

        // The games that use it.
        rows.Add(inputs.Games.Count == 0
            ? new ReadinessRow("Games using DirectStorage", "None found", CheckTone.Info, "None of your games ship Microsoft's DirectStorage runtime yet.")
            : new ReadinessRow("Games using DirectStorage", inputs.Games.Count.ToString(CultureInfo.CurrentCulture), CheckTone.Good, JoinNames(inputs.Games.Select(g => g.Name).ToList()) + "."));

        var problem = rows.FirstOrDefault(r => r.Tone == CheckTone.Problem);
        if (problem is not null)
        {
            return new DirectStorageReport(DirectStorageState.PartlyReady, $"{problem.Name}: {problem.Value}. {problem.Detail}", problem.Detail, rows, inputs.Games);
        }

        string summary = inputs.Games.Count == 0
            ? "Ready: GPU decompression and NVMe for games that use DirectStorage."
            : $"Ready for {inputs.Games.Count.ToString(CultureInfo.CurrentCulture)} {(inputs.Games.Count == 1 ? "game" : "games")} that use it: GPU decompression and NVMe{(windows11 ? " with BypassIO" : string.Empty)}.";
        return new DirectStorageReport(DirectStorageState.Ready, summary, null, rows, inputs.Games);
    }

    /// <summary>The drives to check: every drive holding a game (system drive first when there are no games), distinct.</summary>
    public static IReadOnlyList<string> DriveRoots(IEnumerable<GameEntry> games, string systemRoot)
    {
        var roots = games
            .Select(g => RootOf(g.InstallPath))
            .Where(r => r is not null)
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roots.Count > 0 ? roots : [systemRoot];
    }

    /// <summary>"D:\" for a Windows path ("D:\Games\X"), else null.</summary>
    public static string? RootOf(string? path) =>
        path is { Length: >= 2 } && char.IsAsciiLetter(path[0]) && path[1] == ':' ? char.ToUpperInvariant(path[0]) + @":\" : null;

    /// <summary>
    /// Whether a game's folder holds the DirectStorage runtime: looks at most <paramref name="maxDepth"/> folders deep and
    /// at <paramref name="maxEntries"/> entries, so a huge game folder costs little. Unreadable folders are skipped.
    /// </summary>
    public static bool UsesDirectStorage(string? installPath, int maxDepth = 3, int maxEntries = 6000)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
        {
            return false;
        }

        int seen = 0;
        var level = new List<string> { installPath };
        for (int depth = 0; depth <= maxDepth && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (string folder in level)
            {
                try
                {
                    foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                    {
                        if (++seen > maxEntries)
                        {
                            return false;
                        }

                        string name = Path.GetFileName(entry);
                        if (RuntimeFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (depth < maxDepth && Directory.Exists(entry) && !IsLink(entry))
                        {
                            next.Add(entry);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // a folder Coreloom can't read: skip it
                }
            }

            level = next;
        }

        return false;
    }

    /// <summary>"6.6" for 0x66.</summary>
    public static string ShaderModelText(int model) => $"{model >> 4}.{model & 0xF}";

    private static string BypassText(BypassIoState state)
    {
        string driver = string.IsNullOrWhiteSpace(state.Driver) ? string.Empty : $" {state.Driver.Trim()} is in the way.";
        string reason = string.IsNullOrWhiteSpace(state.Reason) ? string.Empty : $" Windows says: {state.Reason.Trim().TrimEnd('.')}.";
        return $"DirectStorage still works, with more processor work per read.{driver}{reason}";
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string DriveName(string root) => root.TrimEnd('\\', '/');

    private static bool SameRoot(string a, string b) => string.Equals(DriveName(a), DriveName(b), StringComparison.OrdinalIgnoreCase);

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        <= 4 => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
        _ => $"{string.Join(", ", names.Take(3))} and {(names.Count - 3).ToString(CultureInfo.CurrentCulture)} more",
    };
}
