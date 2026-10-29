using System.Text;

namespace PocketParty.Net
{
    // The Wi-Fi join code (FEATURES.hotspotQr): the text a phone camera reads as "Join network?", one tap and the phone
    // is on the TV's hotspot with no internet needed. The standard form is WIFI:T:WPA;S:<name>;P:<password>;; with
    // \ ; , : and " escaped by a backslash. No password: T:nopass (an open network).
    public static class WifiQr
    {
        public const int MinPassword = 8, MaxPassword = 63;   // WPA2's own limits (Windows Mobile Hotspot asks for 8+)

        public static string Payload(string name, string password)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var sb = new StringBuilder("WIFI:");
            if (string.IsNullOrEmpty(password)) sb.Append("T:nopass;S:").Append(Escape(name)).Append(";;");
            else sb.Append("T:WPA;S:").Append(Escape(name)).Append(";P:").Append(Escape(password)).Append(";;");
            return sb.ToString();
        }

        // Why this name and password can't make a code (null: they can). An empty password is an open network.
        public static string Problem(string name, string password)
        {
            if (string.IsNullOrEmpty(name)) return "Type the hotspot's network name.";
            if (Encoding.UTF8.GetByteCount(name) > 32) return "The network name is too long (32 letters at most).";
            if (!string.IsNullOrEmpty(password) && (password.Length < MinPassword || password.Length > MaxPassword))
                return "The password needs " + MinPassword + " to " + MaxPassword + " letters.";
            return null;
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                if (c == '\\' || c == ';' || c == ',' || c == ':' || c == '"') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
