using System.Security.Cryptography;
using System.Text;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;

namespace Coreloom.Mac.Services;

/// <summary>
/// The Mac's desktop picture, for the MacBook on the Overview: asked from macOS (NSWorkspace), converted once to a PNG
/// of screen size with macOS's own sips (which reads HEIC, JPEG, PNG and the dynamic pictures), and kept in Coreloom's
/// cache folder until the picture changes. Read on this Mac only; nothing is sent anywhere. A picture in a folder macOS
/// guards (Desktop, Documents, Downloads, iCloud Drive) is read only when the person turned the picture on themselves,
/// so macOS's "Coreloom would like to access…" question never comes out of the blue.
/// </summary>
public static class DesktopPicture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _cacheDirectory;
    private static string? _loadedKey;
    private static bool _allowProtected;

    /// <summary>The picture, about 960 points wide, or null (not loaded yet, or none could be read).</summary>
    public static Bitmap? Picture { get; private set; }

    /// <summary>The picture shrunk to a few dozen pixels: drawn large, it's the soft glow the screen casts on the keys.</summary>
    public static Bitmap? Glow { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Picture"/> changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>What Customize shows about the picture (bindable).</summary>
    public static DesktopPictureState State { get; } = new();

    /// <summary>Where converted pictures are kept (set by the app at startup).</summary>
    public static void Initialize(string cacheDirectory) => _cacheDirectory = Path.Combine(cacheDirectory, "Desktop picture");

    /// <summary>
    /// Loads the desktop picture if it isn't loaded yet or has changed (call when the Overview opens). Safe to call often:
    /// it's quick when nothing changed.
    /// </summary>
    public static void Refresh(bool allowProtected)
    {
        _allowProtected |= allowProtected;
        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        if (_cacheDirectory is null || !await Gate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            string? path = await Dispatcher.UIThread.InvokeAsync(CurrentPath);
            bool blocked = path is not null && IsProtected(path) && !_allowProtected;
            await Dispatcher.UIThread.InvokeAsync(() => State.IsBlocked = blocked);
            if (path is null || blocked || !File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            string key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            if (key == _loadedKey)
            {
                return;
            }

            Directory.CreateDirectory(_cacheDirectory);
            string png = Path.Combine(_cacheDirectory, Hash(key) + ".png");
            if (!File.Exists(png))
            {
                foreach (string old in Directory.EnumerateFiles(_cacheDirectory, "*.png"))
                {
                    File.Delete(old);
                }

                var result = await Commands.RunAsync("/usr/bin/sips", ["-s", "format", "png", "-s", "dpiWidth", "96", "-s", "dpiHeight", "96", "-Z", "960", path, "--out", png], TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                if (!result.Succeeded || !File.Exists(png))
                {
                    Log.Info("Overview", $"The desktop picture couldn't be read ({result.Error.Trim()}); the MacBook shows Coreloom's own.");
                    _loadedKey = key;
                    return;
                }
            }

            var (picture, glow) = await Task.Run(() =>
            {
                var bitmap = new Bitmap(png);
                return (bitmap, bitmap.CreateScaledBitmap(new PixelSize(36, 24), BitmapInterpolationMode.HighQuality));
            }).ConfigureAwait(false);
            _loadedKey = key;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var oldPicture = Picture;
                var oldGlow = Glow;
                Picture = picture;
                Glow = glow;
                Changed?.Invoke(null, EventArgs.Empty);
                oldPicture?.Dispose();
                oldGlow?.Dispose();
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Log.Info("Overview", $"The desktop picture couldn't be read: {e.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Shows a picture file as the desktop picture (Coreloom's screenshots, made away from a Mac).</summary>
    public static void ShowForPreview(string path)
    {
        var bitmap = new Bitmap(path);
        Picture = bitmap;
        Glow = bitmap.CreateScaledBitmap(new PixelSize(36, 24), BitmapInterpolationMode.HighQuality);
        _loadedKey = "preview";
        _cacheDirectory = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The main screen's desktop picture file (NSWorkspace), or null.</summary>
    private static string? CurrentPath()
    {
        try
        {
            using var pool = new ObjC.Pool();
            nint workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace"));
            nint screen = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen"));
            if (workspace == 0 || screen == 0)
            {
                return null;
            }

            nint url = ObjC.Send(workspace, ObjC.Sel("desktopImageURLForScreen:"), screen);
            return url == 0 ? null : ObjC.ToStringValue(ObjC.Send(url, ObjC.Sel("path")));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Folders macOS asks about before an app reads them.</summary>
    internal static bool IsProtected(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Other apps' folders too (a picture set from Photos lives in its container): macOS asks about those as well.
        string[] guarded = ["Desktop", "Documents", "Downloads", Path.Combine("Library", "Mobile Documents"), Path.Combine("Library", "Containers"), Path.Combine("Library", "Group Containers")];
        return guarded.Any(folder => path.StartsWith(Path.Combine(home, folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
}

/// <summary>What Customize shows about the desktop picture.</summary>
public sealed partial class DesktopPictureState : ObservableObject
{
    /// <summary>
    /// The picture is kept in a folder macOS guards and hasn't been chosen in Customize yet, so the MacBook shows
    /// Coreloom's picture until it is.
    /// </summary>
    [ObservableProperty]
    private bool _isBlocked;
}
