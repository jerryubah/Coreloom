namespace Coreloom.ViewModels.Infrastructure;

/// <summary>Who's signed in and which machine this is, for the greeting and the sidebar's chip (since 1.9.0).</summary>
public sealed class AccountInfo
{
    public AccountInfo(string? userName, string? machineName, string? displayName = null)
    {
        UserName = Clean(userName) ?? string.Empty;
        MachineName = Clean(machineName) ?? string.Empty;
        DisplayName = Clean(displayName) ?? Pretty(UserName);
    }

    /// <summary>The sign-in name ("jerry").</summary>
    public string UserName { get; }

    /// <summary>The name to show ("Jerry Ubah" when the system knows it, else the sign-in name with a capital).</summary>
    public string DisplayName { get; }

    /// <summary>The machine's name ("JERRY-PC").</summary>
    public string MachineName { get; }

    /// <summary>The first word of the display name, for the greeting; empty when there's no name.</summary>
    public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>One or two letters for the chip's circle ("JU"; "J" for one word; "?" for nothing).</summary>
    public string Initials
    {
        get
        {
            var words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).ToList();
            return words.Count == 0 ? "?" : string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>"jerry.ubah" → "Jerry Ubah"; "JERRY" → "Jerry".</summary>
    public static string Pretty(string userName)
    {
        var words = userName.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => w.All(char.IsUpper) || w.All(char.IsLower) ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : w));
    }
}

/// <summary>"Good morning", "Good afternoon" or "Good evening", with the name when there is one.</summary>
public static class Greetings
{
    public static string For(int hour, string? firstName)
    {
        string time = hour < 5 ? "Good evening" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        return string.IsNullOrWhiteSpace(firstName) ? time : $"{time}, {firstName}";
    }
}
