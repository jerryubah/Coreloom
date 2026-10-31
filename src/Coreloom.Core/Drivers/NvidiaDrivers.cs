using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace Coreloom.Core.Drivers;

/// <summary>A graphics card in NVIDIA's driver lookup: its product family (pfid) and series (psid).</summary>
public sealed record NvidiaProduct(string Name, int FamilyId, int SeriesId);

/// <summary>NVIDIA's newest driver for a card, from its driver lookup.</summary>
public sealed record NvidiaRelease(string Version, string DownloadUrl)
{
    public string? Name { get; init; }

    public DateTime? Date { get; init; }

    public string? SizeText { get; init; }

    public string? DetailsUrl { get; init; }
}

/// <summary>
/// NVIDIA's public driver lookup, as nvidia.com/drivers uses it (since 1.8.6): the product list
/// (<see cref="ProductListUrl"/>, XML) maps a card's name to its family and series ids, and the lookup
/// (<see cref="LookupUrl"/>, JSON) answers the newest Game Ready (WHQL, DCH) driver for them, with its download.
/// </summary>
public static class NvidiaDrivers
{
    /// <summary>NVIDIA's list of product families (TypeID 3): name, family id, and the series id as its parent.</summary>
    public const string ProductListUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";

    /// <summary>NVIDIA's driver pages, when a card isn't in the lookup.</summary>
    public const string DriversPage = "https://www.nvidia.com/en-us/drivers/";

    /// <summary>Windows 10/11 64-bit in NVIDIA's lookup (the same package serves both).</summary>
    public const int Windows64 = 57;

    /// <summary>The newest WHQL Game Ready driver of the DCH kind for a card.</summary>
    public static string LookupUrl(NvidiaProduct product) =>
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup"
        + string.Create(CultureInfo.InvariantCulture, $"&psid={product.SeriesId}&pfid={product.FamilyId}&osID={Windows64}&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1");

    /// <summary>The card's name as NVIDIA's lists write it: Windows' adapter name without "NVIDIA " in front.</summary>
    public static string ProductName(string adapterName)
    {
        string name = adapterName.Trim();
        if (name.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase))
        {
            name = name[7..].Trim();
        }

        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Reads the product list (<c>&lt;LookupValue ParentID="120"&gt;&lt;Name&gt;GeForce RTX 3080&lt;/Name&gt;&lt;Value&gt;929&lt;/Value&gt;</c>).</summary>
    public static IReadOnlyList<NvidiaProduct> ParseProducts(string xml)
    {
        var products = new List<NvidiaProduct>();
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return products;
        }

        foreach (var value in document.Descendants().Where(e => e.Name.LocalName == "LookupValue"))
        {
            string? name = value.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value?.Trim();
            string? id = value.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim();
            string? parent = value.Attribute("ParentID")?.Value;
            if (!string.IsNullOrEmpty(name) && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int family)
                && int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int series))
            {
                products.Add(new NvidiaProduct(name, family, series));
            }
        }

        return products;
    }

    /// <summary>The card in the product list: by its exact name, else by the name without "Laptop GPU"/"GPU" at the end.</summary>
    public static NvidiaProduct? Find(IReadOnlyList<NvidiaProduct> products, string adapterName)
    {
        string name = ProductName(adapterName);
        var exact = products.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        string shorter = name.EndsWith(" GPU", StringComparison.OrdinalIgnoreCase) ? name[..^4].Trim() : name;
        return products.FirstOrDefault(p => p.Name.Equals(shorter, StringComparison.OrdinalIgnoreCase))
            ?? products.FirstOrDefault(p => p.Name.Equals(shorter + " GPU", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the lookup's answer (<c>IDS[0].downloadInfo</c>; its texts are URL-encoded). Null when it found nothing.</summary>
    public static NvidiaRelease? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0
                || !ids[0].TryGetProperty("downloadInfo", out var info))
            {
                return null;
            }

            string? Text(string key) => info.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? Uri.UnescapeDataString(value.GetString() ?? string.Empty).Trim() is { Length: > 0 } text ? text : null
                : null;

            string? version = Text("Version");
            string? url = Text("DownloadURL");
            if (version is null || url is null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            DateTime? date = Text("ReleaseDateTime") is { } when
                && DateTime.TryParseExact(when, ["ddd MMM d, yyyy", "ddd MMM dd, yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
                ? parsed
                : null;
            return new NvidiaRelease(version, url)
            {
                Name = Text("Name"),
                Date = date,
                SizeText = Text("DownloadURLFileSize"),
                DetailsUrl = Text("DetailsURL"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>NVIDIA's newer driver as an update for the installed one (Windows' version number), or null when it's not newer.</summary>
    public static DriverUpdate? AsUpdate(NvidiaRelease release, InstalledDriver card)
    {
        string? current = DriverCatalog.NvidiaVersion(card.Version);
        if (current is not null && DriverCatalog.CompareVersions(release.Version, current) <= 0)
        {
            return null;
        }

        return new DriverUpdate("nvidia", card.DeviceName, DriverSource.Nvidia, DriverCategory.Graphics)
        {
            Provider = "NVIDIA",
            CurrentVersion = current ?? card.Version,
            NewVersion = release.Version,
            Date = release.Date,
            SizeText = release.SizeText,
            DownloadUrl = release.DownloadUrl,
            DetailsUrl = release.DetailsUrl,
        };
    }

    /// <summary>
    /// The installer's switches: silent (the package extracts itself and installs with no windows), no restart (Windows
    /// asks when one is needed), and the settings kept.
    /// </summary>
    public const string SilentArguments = "-s -noreboot";

    /// <summary>What the installer's exit code means.</summary>
    public static DriverInstallResult Outcome(int exitCode, string version) => exitCode switch
    {
        0 => new DriverInstallResult(true, $"NVIDIA {version} installed."),
        1 or 3010 => new DriverInstallResult(true, $"NVIDIA {version} installed. Restart Windows to finish.", RestartNeeded: true),
        _ => new DriverInstallResult(false, string.Create(CultureInfo.InvariantCulture, $"NVIDIA's installer stopped (code {exitCode}). Try again, or install it from the NVIDIA app.")),
    };
}

/// <summary>
/// Microsoft's DirectX End-User Runtime (since 1.8.6): DirectX 12 itself is part of Windows and comes with Windows
/// Update, but games made for DirectX 9 to 11 often still need the extra files of the 2010 runtime (D3DX, XInput 1.3,
/// XAudio 2.7). The page shows which of them are missing and installs Microsoft's web installer.
/// </summary>
public static class DirectXRuntime
{
    /// <summary>Microsoft's DirectX End-User Runtime Web Installer.</summary>
    public const string WebInstallerUrl = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";

    /// <summary>The installer's quiet switch (no wizard).</summary>
    public const string QuietArguments = "/Q";

    /// <summary>The newest file of each part of the 2010 runtime, in System32.</summary>
    public static readonly IReadOnlyList<(string File, string Part)> Files =
    [
        ("d3dx9_43.dll", "Direct3D 9 extensions (D3DX9)"),
        ("d3dx10_43.dll", "Direct3D 10 extensions (D3DX10)"),
        ("d3dx11_43.dll", "Direct3D 11 extensions (D3DX11)"),
        ("D3DCompiler_43.dll", "Shader compiler 43"),
        ("xinput1_3.dll", "XInput 1.3 (controllers)"),
        ("XAudio2_7.dll", "XAudio 2.7 (sound)"),
    ];

    /// <summary>The parts whose file isn't in System32.</summary>
    public static IReadOnlyList<string> Missing(Func<string, bool> exists) => [.. Files.Where(f => !exists(f.File)).Select(f => f.Part)];

    /// <summary>The runtime as an update, when any part is missing.</summary>
    public static DriverUpdate? AsUpdate(IReadOnlyList<string> missing) => missing.Count == 0
        ? null
        : new DriverUpdate("directx", "DirectX End-User Runtime", DriverSource.DirectX, DriverCategory.Software)
        {
            Provider = "Microsoft",
            NewVersion = "June 2010",
            SizeText = "up to 100 MB",
            DownloadUrl = WebInstallerUrl,
            CurrentVersion = missing.Count == Files.Count ? "Not installed" : string.Create(CultureInfo.CurrentCulture, $"{missing.Count} of {Files.Count} parts missing"),
        };
}
