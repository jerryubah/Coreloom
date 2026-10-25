using System.Globalization;
using System.Text.Json;

namespace Coreloom.Core.Optimize;

/// <summary>A threat Microsoft Defender found.</summary>
public sealed record ThreatInfo(string Name, int Severity, string? Resource, bool Active)
{
    /// <summary>Defender's severity ids: 1 low, 2 moderate, 4 high, 5 severe.</summary>
    public string SeverityText => Severity switch
    {
        >= 5 => "Severe",
        4 => "High",
        2 or 3 => "Moderate",
        1 => "Low",
        _ => "Unknown",
    };
}

/// <summary>What protects the PC: Microsoft Defender's state, or the other antivirus that replaced it.</summary>
public sealed record ProtectionStatus
{
    public static ProtectionStatus Unavailable(string problem) => new() { Problem = problem };

    /// <summary>Defender answered (it may still be passive because another antivirus is in charge).</summary>
    public bool DefenderAvailable { get; init; }

    public bool RealTimeProtection { get; init; }

    public bool AntivirusEnabled { get; init; }

    /// <summary>"Normal", "Passive Mode", "EDR Block Mode"…</summary>
    public string? Mode { get; init; }

    public DateTime? SignaturesUpdated { get; init; }

    public string? SignatureVersion { get; init; }

    public DateTime? LastQuickScan { get; init; }

    public DateTime? LastFullScan { get; init; }

    public IReadOnlyList<ThreatInfo> Threats { get; init; } = [];

    /// <summary>Antivirus products Windows Security knows about (Defender included).</summary>
    public IReadOnlyList<string> Products { get; init; } = [];

    /// <summary>Why the status couldn't be read, if it couldn't.</summary>
    public string? Problem { get; init; }

    public int ActiveThreats => Threats.Count(t => t.Active);

    /// <summary>Another antivirus is in charge (Defender is passive or off).</summary>
    public string? OtherProduct => Products.FirstOrDefault(p => !p.Contains("Defender", StringComparison.OrdinalIgnoreCase));

    public bool IsDefenderInCharge => DefenderAvailable && AntivirusEnabled && !string.Equals(Mode, "Passive Mode", StringComparison.OrdinalIgnoreCase);

    /// <summary>Protected: Defender's real-time protection is on, or another antivirus is in charge.</summary>
    public bool IsProtected => IsDefenderInCharge ? RealTimeProtection : OtherProduct is not null;

    public bool SignaturesOutdated(DateTime now) => IsDefenderInCharge && (SignaturesUpdated is not { } updated || now - updated > TimeSpan.FromDays(3));

    public bool ScanOverdue(DateTime now) => IsDefenderInCharge && (Latest(LastQuickScan, LastFullScan) is not { } scanned || now - scanned > TimeSpan.FromDays(7));

    public static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;

    /// <summary>
    /// macOS: the built-in protections and whether each is on (XProtect, Gatekeeper, System Integrity Protection,
    /// FileVault, the firewall). Empty on Windows, where Defender's fields above apply.
    /// </summary>
    public IReadOnlyList<SecurityCheck> Checks { get; init; } = [];
}

/// <summary>
/// One of macOS's protections: on or off, what it does, and whether it matters for safety (the firewall and FileVault
/// are recommended; XProtect, Gatekeeper and System Integrity Protection should never be off).
/// </summary>
public sealed record SecurityCheck(string Id, string Name, bool IsOn, string Detail, bool Essential);

/// <summary>Reads the output of macOS's own security tools (spctl, csrutil, fdesetup, socketfilterfw).</summary>
public static class MacSecurity
{
    /// <summary>"assessments enabled" → true; "assessments disabled" → false.</summary>
    public static bool? Gatekeeper(string? spctlStatus) =>
        spctlStatus is null ? null
        : spctlStatus.Contains("assessments enabled", StringComparison.OrdinalIgnoreCase) ? true
        : spctlStatus.Contains("assessments disabled", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    /// <summary>"System Integrity Protection status: enabled." → true.</summary>
    public static bool? IntegrityProtection(string? csrutilStatus)
    {
        if (csrutilStatus is null)
        {
            return null;
        }

        int colon = csrutilStatus.IndexOf("status:", StringComparison.OrdinalIgnoreCase);
        string state = colon >= 0 ? csrutilStatus[(colon + 7)..].TrimStart() : csrutilStatus;
        return state.StartsWith("enabled", StringComparison.OrdinalIgnoreCase) ? true
            : state.StartsWith("disabled", StringComparison.OrdinalIgnoreCase) ? false
            : null;
    }

    /// <summary>"FileVault is On." → true; "FileVault is Off." → false (also while encryption is in progress: on).</summary>
    public static bool? FileVault(string? fdesetupStatus) =>
        fdesetupStatus is null ? null
        : fdesetupStatus.Contains("FileVault is On", StringComparison.OrdinalIgnoreCase) || fdesetupStatus.Contains("Encryption in progress", StringComparison.OrdinalIgnoreCase) ? true
        : fdesetupStatus.Contains("FileVault is Off", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    /// <summary>"Firewall is enabled. (State = 1)" → true; "Firewall is disabled. (State = 0)" → false.</summary>
    public static bool? Firewall(string? socketFilterState) =>
        socketFilterState is null ? null
        : socketFilterState.Contains("(State = 0)", StringComparison.Ordinal) || socketFilterState.Contains("disabled", StringComparison.OrdinalIgnoreCase) ? false
        : socketFilterState.Contains("enabled", StringComparison.OrdinalIgnoreCase) || socketFilterState.Contains("(State = 1)", StringComparison.Ordinal) || socketFilterState.Contains("(State = 2)", StringComparison.Ordinal) ? true
        : null;
}

/// <summary>
/// Reads Microsoft Defender through its own PowerShell module (part of Windows), so nothing is reimplemented: status
/// with <c>Get-MpComputerStatus</c>, threats with <c>Get-MpThreat</c>, and installed antivirus products from Windows
/// Security Center.
/// </summary>
public static class DefenderScripts
{
    /// <summary>Prints one line of JSON (see <see cref="ParseStatus"/>). Dates are UTC in ISO 8601.</summary>
    public const string Status = """
        $ErrorActionPreference = 'Stop'
        function Iso($d) { if ($d -and $d.Year -gt 1601) { $d.ToUniversalTime().ToString('o') } else { $null } }
        $products = @()
        try { $products = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object { $_.displayName }) } catch { }
        try {
          $s = Get-MpComputerStatus
          $threats = @()
          try {
            $threats = @(Get-MpThreat -ErrorAction Stop | ForEach-Object {
              [pscustomobject]@{ name = [string]$_.ThreatName; severity = [int]$_.SeverityID; resource = [string](@($_.Resources) | Select-Object -First 1); active = [bool]$_.IsActive }
            })
          } catch { }
          [pscustomobject]@{
            available = $true
            realTime = [bool]$s.RealTimeProtectionEnabled
            antivirus = [bool]$s.AntivirusEnabled
            mode = [string]$s.AMRunningMode
            signaturesUpdated = Iso $s.AntivirusSignatureLastUpdated
            signatureVersion = [string]$s.AntivirusSignatureVersion
            lastQuickScan = Iso $s.QuickScanEndTime
            lastFullScan = Iso $s.FullScanEndTime
            threats = $threats
            products = $products
          } | ConvertTo-Json -Depth 4 -Compress
        } catch {
          [pscustomobject]@{ available = $false; error = $_.Exception.Message; products = $products } | ConvertTo-Json -Depth 3 -Compress
        }
        """;

    /// <summary>Removes the active threats Defender has found (quarantines or deletes as Defender decides).</summary>
    public const string RemoveThreats = "$ErrorActionPreference = 'Stop'; Remove-MpThreat; 'ok'";

    public static ProtectionStatus ParseStatus(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ProtectionStatus.Unavailable("Windows Security didn't answer.");
        }

        // PowerShell can put a byte order mark or a warning around the JSON: parse from the first { to the last }.
        int start = json.IndexOf('{');
        int end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return ProtectionStatus.Unavailable("Windows Security gave an answer Coreloom couldn't read.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json.AsMemory(start, end - start + 1));
            var root = doc.RootElement;
            var products = Strings(root, "products");
            if (!Bool(root, "available"))
            {
                return new ProtectionStatus
                {
                    Products = products,
                    Problem = Text(root, "error") is { } error ? $"Microsoft Defender isn't available ({error.Trim()})" : "Microsoft Defender isn't available.",
                };
            }

            var threats = new List<ThreatInfo>();
            if (root.TryGetProperty("threats", out var list))
            {
                foreach (var item in list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [list])
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        threats.Add(new ThreatInfo(Text(item, "name") ?? "Unknown threat", Int(item, "severity"), Text(item, "resource"), Bool(item, "active")));
                    }
                }
            }

            return new ProtectionStatus
            {
                DefenderAvailable = true,
                RealTimeProtection = Bool(root, "realTime"),
                AntivirusEnabled = Bool(root, "antivirus"),
                Mode = Text(root, "mode"),
                SignaturesUpdated = Date(root, "signaturesUpdated"),
                SignatureVersion = Text(root, "signatureVersion"),
                LastQuickScan = Date(root, "lastQuickScan"),
                LastFullScan = Date(root, "lastFullScan"),
                Threats = threats,
                Products = products,
            };
        }
        catch (JsonException)
        {
            return ProtectionStatus.Unavailable("Windows Security gave an answer Coreloom couldn't read.");
        }
    }

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static DateTime? Date(JsonElement e, string name) =>
        Text(e, name) is { } text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date.ToLocalTime()
            : null;

    /// <summary>A string or an array of strings (PowerShell writes a single item without the array).</summary>
    private static IReadOnlyList<string> Strings(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return [];
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => [v.GetString()!],
            JsonValueKind.Array => v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).Where(s => s.Length > 0).Distinct().ToList(),
            _ => [],
        };
    }
}

/// <summary>Result of a Defender scan.</summary>
public sealed record ScanOutcome(bool Completed, int ThreatsFound, string? Problem);

/// <summary>Result of freeing up memory: available memory before and after, and how many apps were trimmed.</summary>
public sealed record MemoryCleanResult(long AvailableBefore, long AvailableAfter, long Total, int ProcessesTrimmed)
{
    public long Freed => Math.Max(0, AvailableAfter - AvailableBefore);

    /// <summary>Every process looked at: trimmed, skipped or failed, with its memory before and after (since 1.4.6).</summary>
    public IReadOnlyList<MemoryTrimEntry> Processes { get; init; } = [];
}

/// <summary>Result of a maintenance task (flush DNS, optimize drives…).</summary>
public sealed record MaintenanceResult(bool Succeeded, string Message);
