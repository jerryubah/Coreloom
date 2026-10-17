namespace Coreloom.Core.Gaming;

/// <summary>A game's own frame-rate limit: set in the NVIDIA driver for the game's programs only.</summary>
public sealed class GameFrameCap
{
    /// <summary>The library id ("steam:1091500").</summary>
    public string GameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>What was picked: 0 automatic (a little under the refresh rate), otherwise frames per second.</summary>
    public int Choice { get; set; }

    /// <summary>The limit set in the driver, in frames per second.</summary>
    public int Fps { get; set; }

    /// <summary>The game's program files the driver matches ("Cyberpunk2077.exe").</summary>
    public List<string> Executables { get; set; } = [];

    /// <summary>Set by adaptive frame pacing (since 1.4.6) rather than by hand; adaptive pacing only changes limits it set.</summary>
    public bool Adaptive { get; set; }
}

/// <summary>Which program files are a game, so the driver's per-program limit reaches it.</summary>
public static class GameExecutables
{
    /// <summary>At most this many programs per game.</summary>
    public const int Max = 3;

    // Words in the names of programs that sit in game folders but aren't the game.
    private static readonly string[] HelperWords =
    [
        "unins", "uninstall", "setup", "install", "crash", "report", "redist", "prereq", "dxsetup", "dotnet", "vcredist",
        "helper", "updater", "update", "cefprocess", "webhelper", "webengine", "easyanticheat", "battleye", "beservice",
        "eac_", "launcherpatcher", "touchup", "cleanup", "benchmark", "config", "settings",
    ];

    /// <summary>
    /// The game's programs, best first: the one running now (when it lives in the game's folder), the one the store
    /// launches, Unreal Engine's "…-Win64-Shipping.exe" (the real game behind a small starter), and otherwise the largest
    /// program in the folder that isn't a helper. File names only, each once.
    /// </summary>
    public static List<string> Pick(GameEntry game, IEnumerable<(string Path, long Size)> files, string? runningPath = null)
    {
        var names = new List<string>();
        void Add(string? path)
        {
            string? name = FileName(path);
            if (name is not null && names.Count < Max && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        if (game.Store == GameStore.Custom)
        {
            Add(game.Executable); // the program the user picked comes first, whatever it is called
        }

        Add(runningPath);
        if (!IsHelper(FileName(game.Executable)))
        {
            Add(game.Executable);
        }

        var programs = files
            .Where(f => FileName(f.Path) is { } n && !IsHelper(n))
            .OrderByDescending(f => f.Size)
            .ToList();
        foreach (var shipping in programs.Where(f => FileName(f.Path)!.Contains("-Shipping", StringComparison.OrdinalIgnoreCase)))
        {
            Add(shipping.Path);
        }

        if (names.Count == 0 || game.Store == GameStore.Custom)
        {
            // (For a game added by hand, also the folder's main program: the picked one may only start it.)
            // Prefer a program that isn't a launcher; a game whose only program is "Launcher.exe" still gets that.
            Add(programs.FirstOrDefault(f => !FileName(f.Path)!.Contains("launcher", StringComparison.OrdinalIgnoreCase)).Path
                ?? programs.FirstOrDefault().Path);
        }

        return names;
    }

    /// <summary>"Cyberpunk2077.exe" from a full path (either slash), or null when it isn't a program.</summary>
    public static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string text = path.Trim();
        int comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..], out _))
        {
            text = text[..comma]; // "game.exe,0": an icon reference
        }

        text = text.Trim().Trim('"');
        string name = text[(text.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4 ? name : null;
    }

    private static bool IsHelper(string? name) =>
        name is null || GameLibrary.IsNotGame(name) || HelperWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
}
