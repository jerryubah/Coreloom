using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Coreloom.Core.Connect;

/// <summary>An address phones on this network can reach this computer at.</summary>
public sealed record LocalAddress(IPAddress Address, string Adapter, bool IsPreferred)
{
    public string Url(int port) => $"http://{Address}:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// This computer's IPv4 addresses on local networks, the one most likely to be the home network first: an adapter that
/// is up, has a default gateway and isn't a virtual switch (Hyper-V, WSL, Docker, VMware, VirtualBox, AirDrop's awdl…);
/// VPN addresses such as Tailscale's (100.64/10) come after.
/// </summary>
public static class LocalAddresses
{
    private static readonly string[] VirtualHints =
    [
        "vethernet", "hyper-v", "wsl", "docker", "vmware", "virtualbox", "vbox", "virtual", "loopback", "pseudo",
        "bridge", "awdl", "llw", "utun", "anpi", "ap1", "npcap", "teredo", "isatap",
    ];

    public static IReadOnlyList<LocalAddress> Find()
    {
        var found = new List<(IPAddress Address, string Adapter, int Score)>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            string label = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
            bool looksVirtual = VirtualHints.Any(label.Contains);
            bool wireless = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || !MiniHttpServer.IsLocalNetwork(address) || IPAddress.IsLoopback(address))
                {
                    continue;
                }

                byte[] b = address.GetAddressBytes();
                bool linkLocal = b[0] == 169 && b[1] == 254;
                bool vpn = b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                int score = (gateway ? 100 : 0) + (looksVirtual ? -60 : 0) + (linkLocal ? -80 : 0) + (vpn ? -40 : 0) + (wireless || adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 10 : 0)
                    + (b[0] == 192 && b[1] == 168 ? 5 : 0);
                found.Add((address, adapter.Name, score));
            }
        }

        var ordered = found.OrderByDescending(f => f.Score).ThenBy(f => f.Address.ToString(), StringComparer.Ordinal).ToList();
        return ordered.Select((f, i) => new LocalAddress(f.Address, f.Adapter, i == 0)).ToArray();
    }
}
