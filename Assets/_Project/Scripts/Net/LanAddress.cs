using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PocketParty.Net
{
    // The address phones should use: a real LAN IPv4 (192.168.x, then 10.x, then 172.16-31.x), skipping virtual adapters.
    // The same rule as the old server.js. Falls back to 127.0.0.1.
    // Windows Mobile Hotspot always hands out 192.168.137.x and its PC is 192.168.137.1 (on a "Wi-Fi Direct Virtual
    // Adapter", so the virtual-name rule would skip it): Hotspot() finds that address on its own, and Best() never
    // picks it, so a PC on the home Wi-Fi and hosting a hotspot gives a separate address for each.
    public static class LanAddress
    {
        public const string WindowsHotspotPrefix = "192.168.137.";

        static readonly string[] VirtualNames = { "virtual", "vmware", "hyper-v", "vethernet", "loopback", "bluetooth", "wsl", "docker", "tailscale", "zerotier" };

        public static string Best(string hotspotPrefix = WindowsHotspotPrefix)
        {
            var found = new List<(int rank, string ip)>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                string label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                bool isVirtual = false;
                foreach (var v in VirtualNames) if (label.Contains(v)) isVirtual = true;
                if (isVirtual) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IsHotspot(ua.Address.ToString(), hotspotPrefix)) continue;
                    int r = Rank(ua.Address);
                    if (r > 0) found.Add((r, ua.Address.ToString()));
                }
            }
            found.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : string.CompareOrdinal(a.ip, b.ip));
            return found.Count > 0 ? found[0].ip : "127.0.0.1";
        }

        // This PC's own address on the hotspot it is hosting, virtual adapter or not; null when the hotspot is off.
        public static string Hotspot(string hotspotPrefix = WindowsHotspotPrefix)
        {
            if (string.IsNullOrEmpty(hotspotPrefix)) return null;
            var found = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && IsHotspot(ua.Address.ToString(), hotspotPrefix))
                        found.Add(ua.Address.ToString());
            }
            found.Sort(string.CompareOrdinal);
            return found.Count > 0 ? found[0] : null;
        }

        public static bool IsHotspot(string ip, string hotspotPrefix) =>
            !string.IsNullOrEmpty(hotspotPrefix) && ip != null && ip.StartsWith(hotspotPrefix, System.StringComparison.Ordinal);

        // 1 = best. 0 = not a private LAN address.
        public static int Rank(IPAddress a)
        {
            var b = a.GetAddressBytes();
            if (b[0] == 192 && b[1] == 168) return 1;
            if (b[0] == 10) return 2;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return 3;
            return 0;
        }
    }
}
