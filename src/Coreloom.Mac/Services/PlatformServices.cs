using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Mac.Native;
using Coreloom.Mac.Native;
using Coreloom.ViewModels.Services;

namespace Coreloom.Mac.Services;

/// <summary>Links, folders, files, the clipboard and dialogs, the macOS way (Finder, NSSavePanel, NSAlert).</summary>
public sealed class MacShellService : IShellService
{
    public void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "x-apple.systempreferences"))
        {
            Commands.Start("/usr/bin/open", uri.ToString());
        }
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Commands.Start("/usr/bin/open", path);
        }
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Commands.Start("/usr/bin/open", path);
        }
    }

    /// <summary>Shows a file in Finder, selected.</summary>
    public static void Reveal(string path) => Commands.Start("/usr/bin/open", "-R", path);

    public void CopyToClipboard(string text) => AppKit.CopyText(text);

    public string? PickSaveFile(string suggestedName, string filter) =>
        AppKit.SavePanel(suggestedName, AppKit.Extensions(filter), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) =>
        AppKit.OpenPanel(title, AppKit.Extensions(filter), initialDirectory);

    public void ShowMessage(string title, string message) => AppKit.Alert(title, message, confirm: false);

    public bool Confirm(string title, string message) => AppKit.Alert(title, message, confirm: true);
}

/// <summary>Restarting and quitting. Coreloom on a Mac never runs as administrator.</summary>
public sealed class MacLifetimeService(Action exit) : IAppLifetime, IElevationService
{
    public bool IsElevated => false;

    /// <summary>The app bundle Coreloom runs from ("/Applications/Coreloom.app"), or null when run from a plain folder.</summary>
    public static string? BundlePath
    {
        get
        {
            string executable = Environment.ProcessPath ?? string.Empty;
            int contents = executable.LastIndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            return contents > 0 ? executable[..(contents + 4)] : null;
        }
    }

    public void Restart()
    {
        if (Relaunch($"--wait-for {Environment.ProcessId}"))
        {
            exit();
        }
    }

    public void Exit() => exit();

    /// <summary>Starts a new copy (a new instance of the bundle, so it doesn't just bring this one to the front).</summary>
    public static bool Relaunch(string arguments)
    {
        string[] args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return BundlePath is { } bundle
            ? Commands.Start("/usr/bin/open", ["-n", "-a", bundle, "--args", .. args])
            : Environment.ProcessPath is { } path && Commands.Start(path, args);
    }
}

/// <summary>
/// "Open at login" through ServiceManagement's SMAppService (macOS 13+): the app itself is registered as a login item,
/// listed and switchable in System Settings › General › Login Items. It starts with --startup so it can stay in the
/// menu bar when "Start minimized" is on.
/// </summary>
public sealed class MacStartupService : IStartupService
{
    private const long StatusEnabled = 1;
    private const long StatusRequiresApproval = 2;
    private const string LaunchAgentLabel = "com.jerryubah.coreloom.login";

    static MacStartupService() => NativeLibrary.TryLoad("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement", out _);

    private static string LaunchAgentPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", LaunchAgentLabel + ".plist");

    public bool IsStartAtLogonRegistered => Status() is StatusEnabled or StatusRequiresApproval || File.Exists(LaunchAgentPath);

    private static long Status()
    {
        using var pool = new ObjC.Pool();
        nint service = MainApp();
        return service == 0 ? -1 : ObjC.SendLongResult(service, ObjC.Sel("status"));
    }

    private static nint MainApp()
    {
        nint cls = ObjC.Class("SMAppService");
        return cls == 0 ? 0 : ObjC.Send(cls, ObjC.Sel("mainAppService"));
    }

    public Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized) => Task.FromResult(Apply(enabled, minimized));

    private static bool Apply(bool enabled, bool minimized)
    {
        // The login item can't carry arguments: "start minimized" is read from the settings when it starts at login.
        _ = minimized;
        using var pool = new ObjC.Pool();
        nint service = MainApp();
        if (service != 0 && MacLifetimeService.BundlePath is not null)
        {
            nint error = 0;
            bool ok;
            unsafe
            {
                ok = ObjC.SendBool(service, ObjC.Sel(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:"), (nint)(&error));
            }

            long status = ObjC.SendLongResult(service, ObjC.Sel("status"));
            if (ok || (enabled ? status is StatusEnabled or StatusRequiresApproval : status is not (StatusEnabled or StatusRequiresApproval)))
            {
                RemoveLaunchAgent();
                Log.Info("Startup", enabled ? $"Registered as a login item (status {status})." : "Removed the login item.");
                return true;
            }

            string reason = error == 0 ? "unknown" : ObjC.ToStringValue(ObjC.Send(error, ObjC.Sel("localizedDescription"))) ?? "unknown";
            Log.Warn("Startup", $"SMAppService {(enabled ? "register" : "unregister")} failed ({reason}); using a launch agent.");
        }

        return enabled ? WriteLaunchAgent() : RemoveLaunchAgent();
    }

    /// <summary>The fallback (an app outside a bundle, or SMAppService refusing): a per-user launch agent that opens the app.</summary>
    private static bool WriteLaunchAgent()
    {
        string target = MacLifetimeService.BundlePath ?? Environment.ProcessPath ?? string.Empty;
        if (target.Length == 0)
        {
            return false;
        }

        string program = MacLifetimeService.BundlePath is null
            ? $"<string>{Escape(target)}</string><string>--startup</string>"
            : $"<string>/usr/bin/open</string><string>-a</string><string>{Escape(target)}</string><string>--args</string><string>--startup</string>";
        string plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{LaunchAgentLabel}</string>
              <key>ProgramArguments</key><array>{program}</array>
              <key>RunAtLoad</key><true/>
              <key>ProcessType</key><string>Interactive</string>
            </dict>
            </plist>
            """;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
            File.WriteAllText(LaunchAgentPath, plist);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Startup", "Writing the login launch agent failed.", e);
            return false;
        }
    }

    private static bool RemoveLaunchAgent()
    {
        try
        {
            if (File.Exists(LaunchAgentPath))
            {
                File.Delete(LaunchAgentPath);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Startup", "Removing the login launch agent failed.", e);
            return false;
        }
    }

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? text;
}

/// <summary>Log locations and verbosity.</summary>
public sealed class MacDiagnosticsService(string logDirectory, MemoryLogSink memory) : IDiagnosticsService
{
    public string LogDirectory { get; } = logDirectory;

    public void SetVerboseLogging(bool enabled) => Log.MinimumLevel = enabled ? LogLevel.Debug : LogLevel.Info;

    public IReadOnlyList<LogEntry> RecentLog() => memory.Snapshot();

    public IReadOnlyList<LogRecord> ReadLog()
    {
        Log.Flush();
        return LogFile.Read(LogFile.Files(LogDirectory));
    }
}

/// <summary>Coreloom's own threads aren't metered on a Mac; the Diagnostics card shows the whole process instead.</summary>
public static class ProcessInfo
{
    public static string Describe() => $"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}, PID {Environment.ProcessId}";

    public static TimeSpan Uptime => DateTime.Now - Process.GetCurrentProcess().StartTime;
}
