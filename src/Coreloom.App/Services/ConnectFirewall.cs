using System.Diagnostics;
using System.IO;
using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Connect;

namespace Coreloom.App.Services;

/// <summary>
/// Lets phones reach Coreloom Connect through Windows Defender Firewall: one inbound rule, "Coreloom Connect", for
/// Coreloom's program and port on private and domain networks (never public ones), added when Connect is turned on and
/// removed when it's turned off. Coreloom runs as administrator, so no prompt is needed. When the network in use is set
/// to Public, Windows keeps phones out anyway; Settings › Connect says so and offers Windows' network settings.
/// </summary>
internal sealed class ConnectFirewall : IConnectPlatform
{
    private const string RuleName = "Coreloom Connect";
    private const string LogCategory = "Connect";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public bool CanOpenNetworkSettings => true;

    public async Task<string?> AllowAsync(int port, CancellationToken token)
    {
        string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        string program = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Coreloom.exe");
        await WindowsOptimizeService.RunAsync(netsh, $"advfirewall firewall delete rule name=\"{RuleName}\"", Timeout, token);
        var (exit, output, error) = await WindowsOptimizeService.RunAsync(
            netsh,
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{program}\" protocol=TCP localport={port} profile=private,domain enable=yes description=\"Lets phones on your network see Coreloom's readings (Settings › Connect).\"",
            Timeout,
            token);
        if (exit != 0)
        {
            Log.Warn(LogCategory, $"Adding the firewall rule failed ({exit}): {(output + error).Trim()}");
            return "Coreloom couldn't add its firewall rule. If Windows asks whether Coreloom may use the network, allow it on private networks.";
        }

        Log.Info(LogCategory, $"Firewall rule added for port {port} (private and domain networks).");
        return await PublicNetworkNoteAsync(token);
    }

    public async Task RemoveAsync(CancellationToken token)
    {
        string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        var (exit, _, _) = await WindowsOptimizeService.RunAsync(netsh, $"advfirewall firewall delete rule name=\"{RuleName}\"", Timeout, token);
        Log.Info(LogCategory, exit == 0 ? "Firewall rule removed." : "No firewall rule to remove.");
    }

    public void OpenNetworkSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, "Opening Windows' network settings failed.", ex);
        }
    }

    /// <summary>A note when every connected network is Public (phones are then kept out), else null.</summary>
    private static async Task<string?> PublicNetworkNoteAsync(CancellationToken token)
    {
        try
        {
            var (exit, output, _) = await WindowsOptimizeService.PowerShellAsync(
                "Get-NetConnectionProfile | Where-Object { $_.IPv4Connectivity -ne 'Disconnected' } | ForEach-Object { $_.NetworkCategory.ToString() }",
                Timeout,
                token);
            var categories = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (exit == 0 && categories.Length > 0 && categories.All(c => c.Equals("Public", StringComparison.OrdinalIgnoreCase)))
            {
                Log.Info(LogCategory, "The network in use is set to Public.");
                return "Your network is set to Public, so Windows keeps phones out. Set it to Private in Windows' network settings to connect your phone.";
            }
        }
        catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn(LogCategory, "Couldn't check the network's category.", ex);
        }

        return null;
    }
}
