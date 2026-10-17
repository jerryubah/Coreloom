using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coreloom.Core.Gaming;

/// <summary>A game added by hand (a standalone, DRM-free or otherwise unlisted game): its program and how to show it.</summary>
public sealed class CustomGame
{
    /// <summary>"custom:3f2a9c1b" (stays the same when the game is edited, so its limits and pictures stay with it).</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The game's program (full path to the .exe).</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Launch options passed to the program (optional).</summary>
    public string? Arguments { get; set; }

    /// <summary>A picture chosen for it (full path to a .png, .jpg or .ico), or null for the program's icon.</summary>
    public string? Picture { get; set; }

    public DateTimeOffset Added { get; set; }

    /// <summary>When Coreloom last saw it running.</summary>
    public DateTimeOffset? LastPlayed { get; set; }
}

/// <summary>What a picked program says about itself: its version info (for a name to suggest).</summary>
public sealed record ProgramInfo(string Path, string? ProductName = null, string? Description = null);

/// <summary>Games added by hand: turning them into library entries, checking what was picked, naming and ids.</summary>
public static class CustomGames
{
    public const string IdPrefix = "custom:";

    /// <summary>Picture files that can be chosen.</summary>
    public static IReadOnlyList<string> PictureExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".ico"];

    /// <summary>A library entry for the game: the store is "Added by you", it starts its own program.</summary>
    public static GameEntry ToEntry(CustomGame game)
    {
        string exe = game.Executable.Trim();
        string? folder = DirectoryOf(exe);
        string? args = string.IsNullOrWhiteSpace(game.Arguments) ? null : game.Arguments.Trim();
        return new GameEntry(game.Id, game.Name.Trim(), GameStore.Custom)
        {
            InstallPath = folder,
            Executable = exe,
            Launch = args is null ? exe : $"\"{exe}\" {args}",
            LastPlayed = game.LastPlayed,
            Artwork = IsPicture(game.Picture) ? new GameArtwork(game.Picture, null, null) : GameArtwork.None,
        };
    }

    /// <summary>A new id from the program's path and the time added (stable once saved).</summary>
    public static string NewId(string executable, DateTimeOffset added)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(executable.ToLowerInvariant() + "|" + added.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        return IdPrefix + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    public static bool IsCustomId(string? id) => id is not null && id.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A program that can be added: a full path to an .exe (whether it exists is checked by the caller).</summary>
    public static bool IsProgram(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsFullyQualified(path.Trim()) && string.Equals(ExtensionOf(path.Trim()), ".exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsPicture(string? path) =>
        !string.IsNullOrWhiteSpace(path) && PictureExtensions.Contains(ExtensionOf(path.Trim()).ToLowerInvariant());

    // Windows paths, read the same on any system (the tests run on Linux too).
    private static int LastSeparator(string path) => path.LastIndexOfAny(['\\', '/']);

    /// <summary>"D:\Games\Celeste\Celeste.exe" → "D:\Games\Celeste"; null for a bare file name or a drive root.</summary>
    public static string? DirectoryOf(string path)
    {
        int last = LastSeparator(path);
        return last <= 0 ? null : last == 2 && path[1] == ':' ? path[..3] : path[..last];
    }

    private static string FileNameOf(string path) => path[(LastSeparator(path) + 1)..];

    private static string ExtensionOf(string path)
    {
        string name = FileNameOf(path);
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..];
    }

    private static string StemOf(string path)
    {
        string name = FileNameOf(path);
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    /// <summary>"C:\…" or "\\server\share\…".</summary>
    private static bool IsFullyQualified(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        || path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2;

    /// <summary>
    /// A name for a picked program: its product name or description when they read like a name (not "Launcher" or an
    /// engine's name), else the file name made readable ("HollowKnight.exe" → "Hollow Knight", "dark_souls_3" →
    /// "Dark Souls 3").
    /// </summary>
    public static string SuggestName(string executable, string? productName = null, string? description = null)
    {
        foreach (string? candidate in new[] { productName, description })
        {
            string? text = candidate?.Trim();
            if (!string.IsNullOrEmpty(text) && text.Length is >= 2 and <= 80 && !IsGenericName(text))
            {
                return text;
            }
        }

        string stem = StemOf(executable.Trim());
        foreach (string suffix in new[] { "-Win64-Shipping", "-WinGDK-Shipping", "_x64", "x64", "_dx12", "_dx11", "-Win64", "64" })
        {
            if (stem.Length > suffix.Length + 2 && stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^suffix.Length];
                break;
            }
        }

        var words = new StringBuilder();
        for (int i = 0; i < stem.Length; i++)
        {
            char c = stem[i];
            if (c is '_' or '-' or '.')
            {
                words.Append(' ');
                continue;
            }

            bool boundary = i > 0 && char.IsUpper(c) && char.IsLower(stem[i - 1])
                || i > 0 && char.IsDigit(c) && char.IsLetter(stem[i - 1]);
            if (boundary)
            {
                words.Append(' ');
            }

            words.Append(c);
        }

        var parts = words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1 && w.All(char.IsLower) ? char.ToUpperInvariant(w[0]) + w[1..] : w);
        string name = string.Join(' ', parts);
        return name.Length == 0 ? "Game" : name;
    }

    /// <summary>
    /// Whether the folder is too broad to tell the game's processes by (a drive, a top folder like "C:\Games", or a
    /// personal folder like Desktop or Downloads): then only the game's own program counts as the game running.
    /// </summary>
    public static bool IsBroadFolder(string? folder)
    {
        string? normalized = GameLibrary.NormalizeFolder(folder);
        if (normalized is null)
        {
            return true;
        }

        int depth = normalized.TrimEnd('\\').Count(c => c == '\\');
        if (depth <= 1)
        {
            return true; // "C:\" or "C:\Games\"
        }

        string last = FileNameOf(normalized.TrimEnd('\\'));
        return last.Equals("Desktop", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Downloads", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Documents", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Program Files", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Games", StringComparison.OrdinalIgnoreCase) && depth <= 2
            || normalized.EndsWith(@"\Users\" + last + @"\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Drops broken entries (no name, no program, duplicate ids) and tidies the rest.</summary>
    public static List<CustomGame> Normalize(IEnumerable<CustomGame?>? games) =>
    [
        .. (games ?? [])
            .OfType<CustomGame>()
            .Where(g => IsCustomId(g.Id) && IsProgram(g.Executable))
            .Select(g =>
            {
                g.Name = string.IsNullOrWhiteSpace(g.Name) ? SuggestName(g.Executable) : g.Name.Trim();
                g.Arguments = string.IsNullOrWhiteSpace(g.Arguments) ? null : g.Arguments.Trim();
                g.Picture = IsPicture(g.Picture) ? g.Picture : null;
                return g;
            })
            .DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase),
    ];

    private static bool IsGenericName(string text)
    {
        string t = text.ToLowerInvariant();
        return t is "launcher" or "game" or "application" or "app" or "client" or "main" or "unity" or "unreal engine" or "ue4" or "ue5"
            or "godot engine" or "gamemaker" or "nw.js" or "electron" or "java(tm) platform se binary" or "game launcher"
            || t.StartsWith("unity ", StringComparison.Ordinal) || t.StartsWith("unrealgame", StringComparison.Ordinal)
            || t.Contains("bootstrapper", StringComparison.Ordinal) || t.Contains("crash", StringComparison.Ordinal);
    }
}
