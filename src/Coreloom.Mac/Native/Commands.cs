using System.Diagnostics;
using System.Text;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Mac.Native;

/// <summary>The result of a command: its exit code and what it printed.</summary>
public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    public static CommandResult Failed(string error) => new(-1, string.Empty, error);
}

/// <summary>
/// Runs macOS's own command-line tools (pmset, spctl, launchctl, diskutil, defaults…) with arguments passed one by one,
/// never through a shell. <see cref="RunAsAdministratorAsync"/> asks macOS for the user's password (its own dialog)
/// for the few changes that need it; Coreloom never stores or sees the password.
/// </summary>
public static class Commands
{
    private const string LogCategory = "Commands";

    public static async Task<CommandResult> RunAsync(string file, IEnumerable<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var timer = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellationToken);
        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null)
            {
                return CommandResult.Failed($"{file} couldn't be started.");
            }

            var output = process.StandardOutput.ReadToEndAsync(linked.Token);
            var error = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new CommandResult(process.ExitCode, (await output.ConfigureAwait(false)).Trim(), (await error.ConfigureAwait(false)).Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return CommandResult.Failed($"{Path.GetFileName(file)} didn't finish in time.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Warn(LogCategory, $"Running {file} failed.", e);
            return CommandResult.Failed(e.Message);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Kill(process);
            }

            process?.Dispose();
        }
    }

    public static Task<CommandResult> RunAsync(string file, params string[] arguments) => RunAsync(file, (IEnumerable<string>)arguments);

    /// <summary>Starts a program (or `open`) without waiting for it.</summary>
    public static bool Start(string file, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn(LogCategory, $"Starting {file} failed.", e);
            return false;
        }
    }

    /// <summary>
    /// Runs one fixed command line as administrator after macOS asks for the user's password ("Coreloom wants to make
    /// changes"). <paramref name="commandLine"/> must be built only from fixed text and <see cref="Quote"/>d values.
    /// Returns a failed result when the user cancels (exit code -128).
    /// </summary>
    public static Task<CommandResult> RunAsAdministratorAsync(string commandLine, string prompt, CancellationToken cancellationToken = default)
    {
        string script = $"do shell script {AppleScriptString(commandLine)} with prompt {AppleScriptString(prompt)} with administrator privileges";
        return RunAsync("/usr/bin/osascript", ["-e", script], TimeSpan.FromMinutes(5), cancellationToken);
    }

    /// <summary>True when the user dismissed macOS's password dialog.</summary>
    public static bool WasCancelled(CommandResult result) => result.Error.Contains("(-128)", StringComparison.Ordinal);

    /// <summary>A value quoted for /bin/sh: single quotes, with embedded single quotes closed and escaped.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>A string literal for AppleScript.</summary>
    public static string AppleScriptString(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
