using System.IO.Compression;
using System.Security.Cryptography;

namespace Coreloom.Core.Connect;

/// <summary>A file of the phone app, ready to send: its bytes, a gzip copy for text, its type and an ETag.</summary>
public sealed record WebAsset(byte[] Body, byte[]? Gzip, string ContentType, string ETag);

/// <summary>
/// The phone app's files (<c>src/Coreloom.Phone</c>), embedded in Coreloom.Core under <c>connect-web/</c> and served by
/// Coreloom itself, so the app on the phone is always the version that matches the computer's Coreloom.
/// </summary>
public sealed class WebAssets
{
    public const string ResourcePrefix = "connect-web/";

    private readonly Dictionary<string, WebAsset> _files;

    public WebAssets(IReadOnlyDictionary<string, byte[]> files)
    {
        _files = new(StringComparer.Ordinal);
        foreach (var (path, bytes) in files)
        {
            string type = ContentTypeFor(path);
            bool text = type.StartsWith("text/", StringComparison.Ordinal) || type.Contains("json", StringComparison.Ordinal) || type.Contains("svg", StringComparison.Ordinal);
            byte[]? gzip = text && bytes.Length > 1024 ? Compress(bytes) : null;
            string etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes), 0, 8).ToLowerInvariant() + "\"";
            _files[path.TrimStart('/')] = new WebAsset(bytes, gzip, type, etag);
        }
    }

    public int Count => _files.Count;

    public IEnumerable<string> Paths => _files.Keys;

    /// <summary>The app's files embedded in this assembly.</summary>
    public static WebAssets Embedded()
    {
        var assembly = typeof(WebAssets).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            string normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[normalized[ResourcePrefix.Length..]] = memory.ToArray();
        }

        return new WebAssets(files);
    }

    public bool TryGet(string path, out WebAsset asset)
    {
        string key = path.TrimStart('/');
        if (key.Length == 0)
        {
            key = "index.html";
        }

        return _files.TryGetValue(key, out asset!);
    }

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".webmanifest" => "application/manifest+json; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
