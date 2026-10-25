namespace Coreloom.Core.Optimize;

/// <summary>Where a startup entry is registered.</summary>
public enum StartupScope
{
    /// <summary>HKCU\...\Run: this user.</summary>
    User,

    /// <summary>HKLM\...\Run: everyone.</summary>
    Machine,

    /// <summary>HKLM\...\WOW6432Node\...\Run: everyone, 32-bit apps.</summary>
    Machine32,

    /// <summary>The user's Startup folder.</summary>
    UserFolder,

    /// <summary>The Startup folder for all users.</summary>
    CommonFolder,

    /// <summary>macOS: a launch agent in ~/Library/LaunchAgents (this user).</summary>
    MacUserAgent,

    /// <summary>macOS: a launch agent in /Library/LaunchAgents (every user; switched off per user).</summary>
    MacSystemAgent,
}

/// <summary>An app that starts when you sign in.</summary>
public sealed record StartupApp(string Name, string ValueName, string Command, StartupScope Scope, bool Enabled)
{
    public string? Publisher { get; init; }

    /// <summary>The program file, when it could be found in the command.</summary>
    public string? Executable { get; init; }

    public string Id => $"{Scope}:{ValueName}";

    public string ScopeText => Scope switch
    {
        StartupScope.User => "This user",
        StartupScope.UserFolder => "Startup folder",
        StartupScope.CommonFolder => "Startup folder, all users",
        StartupScope.MacUserAgent => "Background item",
        StartupScope.MacSystemAgent => "Background item, all users",
        _ => "All users",
    };
}

/// <summary>
/// The "StartupApproved" values Windows (Task Manager, Settings › Apps › Startup) uses to turn startup apps off and on
/// without removing them: 12 bytes, the first even when enabled and odd when disabled, then when it was disabled.
/// </summary>
public static class StartupApproved
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    /// <summary>The StartupApproved key for a scope (under HKCU for the user's entries, HKLM for everyone's).</summary>
    public static string KeyFor(StartupScope scope) => ApprovedRoot + scope switch
    {
        StartupScope.Machine32 => "Run32",
        StartupScope.UserFolder or StartupScope.CommonFolder => "StartupFolder",
        _ => "Run",
    };

    /// <summary>Whether the user's own registry hive holds the entry's approval (otherwise the machine's).</summary>
    public static bool IsUserHive(StartupScope scope) => scope is StartupScope.User or StartupScope.UserFolder;

    /// <summary>No value, or an even first byte (2, 6), means enabled.</summary>
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Value(bool enabled, DateTime utcNow)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled)
        {
            BitConverter.TryWriteBytes(value.AsSpan(4), utcNow.ToFileTimeUtc());
        }

        return value;
    }

    /// <summary>The program in a command line: the quoted part, or up to ".exe", or the first word.</summary>
    public static string? Executable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            int close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : text.Trim('"');
        }

        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            return text[..(exe + 4)];
        }

        int space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    /// <summary>A readable name when there is no better one: the program's file name without extension.</summary>
    public static string NameFrom(string valueName, string? executable)
    {
        if (valueName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return valueName[..^4]; // a shortcut in a Startup folder
        }

        if (!string.IsNullOrWhiteSpace(valueName) && !valueName.Contains('\\') && !valueName.Contains('{'))
        {
            return valueName;
        }

        return executable is null ? valueName : Path.GetFileNameWithoutExtension(executable.Replace('\\', '/').Split('/')[^1]);
    }
}
