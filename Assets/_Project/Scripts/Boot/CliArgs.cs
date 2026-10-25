using System;
using System.Globalization;

namespace PocketFootball.Boot
{
    // Command-line switches for the built game:
    // -shots <dir> -quit-after-shots -fixture <name> (saves <dir>/<fixture>.png after a fixed number of frames)
    // -seed <n> -bots <n> -bind <ip> (127.0.0.1 for tools, so no firewall prompt) -look-x <yards> (camera look-at along the field)
    public sealed class CliArgs
    {
        public string ShotsDir;
        public bool QuitAfterShots;
        public string Fixture = "field_sideline";
        public int Seed = 1;
        public string Bind;
        public float? LookX;
        public int Bots;              // -bots <n>: start with n bots (solo checks and screenshots)

        public static CliArgs Parse(string[] args)
        {
            var a = new CliArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string s = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (s == "-shots" && next != null) { a.ShotsDir = next; i++; }
                else if (s == "-quit-after-shots") a.QuitAfterShots = true;
                else if (s == "-fixture" && next != null) { a.Fixture = next; i++; }
                else if (s == "-bind" && next != null) { a.Bind = next; i++; }
                else if (s == "-bots" && next != null && int.TryParse(next, out int bots)) { a.Bots = bots; i++; }
                else if (s == "-seed" && next != null && int.TryParse(next, out int seed)) { a.Seed = seed; i++; }
                else if (s == "-look-x" && next != null && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float lx)) { a.LookX = lx; i++; }
            }
            return a;
        }

        public static CliArgs FromEnvironment() => Parse(Environment.GetCommandLineArgs());
    }
}
