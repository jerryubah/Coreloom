using Coreloom.Core.Diagnostics;
using Coreloom.Mac.Native;

namespace Coreloom.Mac.Services;

/// <summary>
/// Offers, the first time Coreloom is opened from somewhere other than an Applications folder (the disk image, Downloads,
/// or the read-only place macOS runs downloaded apps from), to move it into /Applications — where it can update itself and
/// open at login. It copies the bundle, opens the copy and quits; the original is moved to the Trash when it isn't on a
/// read-only disk image.
/// </summary>
public static class AppMover
{
    public static bool IsInApplications(string bundle) =>
        bundle.StartsWith("/Applications/", StringComparison.Ordinal)
        || bundle.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications") + "/", StringComparison.Ordinal);

    /// <summary>
    /// Asks and moves. Returns true when the moved copy was opened (this one should quit). <paramref name="declined"/>
    /// tells it the user said no before (it asks once).
    /// </summary>
    public static bool OfferMove(bool declined, Action remember)
    {
        if (MacLifetimeService.BundlePath is not { } bundle || IsInApplications(bundle) || declined)
        {
            return false;
        }

        bool translocated = MacUpdateInstaller.IsTranslocated(bundle);
        bool onDiskImage = bundle.StartsWith("/Volumes/", StringComparison.Ordinal);
        string where = translocated ? "a temporary location macOS uses for downloaded apps" : onDiskImage ? "the disk image" : Path.GetDirectoryName(bundle) ?? "this folder";
        bool move = AppKit.Alert(
            "Move Coreloom to the Applications folder?",
            $"Coreloom is running from {where}. In Applications it can keep itself up to date and open at login.",
            confirm: true, ok: "Move to Applications", cancel: "Not now");
        if (!move)
        {
            remember();
            return false;
        }

        string target = "/Applications/Coreloom.app";
        try
        {
            if (Directory.Exists(target))
            {
                if (!AppKit.Alert("Replace the Coreloom in Applications?", "A copy of Coreloom is already in the Applications folder. It will be moved to the Trash and replaced with this one.", confirm: true, ok: "Replace"))
                {
                    return false;
                }

                var trash = Commands.RunAsync("/usr/bin/osascript", ["-e", $"tell application \"Finder\" to delete POSIX file {Commands.AppleScriptString(target)}"]).GetAwaiter().GetResult();
                if (!trash.Succeeded && Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
            }

            // --noqtn: the copy carries no download quarantine (the user has already approved opening Coreloom).
            var copy = Commands.RunAsync("/usr/bin/ditto", ["--noqtn", bundle, target], TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
            if (!copy.Succeeded)
            {
                AppKit.Alert("Coreloom couldn't be moved", $"Drag Coreloom into the Applications folder in Finder instead.\n\n{copy.Error}", confirm: false, warning: true);
                return false;
            }

            if (!translocated && !onDiskImage)
            {
                _ = Commands.RunAsync("/usr/bin/osascript", ["-e", $"tell application \"Finder\" to delete POSIX file {Commands.AppleScriptString(bundle)}"]).GetAwaiter().GetResult();
            }

            Log.Info("App", $"Moved Coreloom from {bundle} to {target}.");
            return Commands.Start("/usr/bin/open", "-n", "-a", target, "--args", "--wait-for", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("App", "Moving Coreloom to Applications failed.", e);
            AppKit.Alert("Coreloom couldn't be moved", "Drag Coreloom into the Applications folder in Finder instead.", confirm: false, warning: true);
            return false;
        }
    }
}
