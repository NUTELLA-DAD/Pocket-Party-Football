using System;
using System.Globalization;

namespace PocketFootball.Boot
{
    // Command-line switches for the built game:
    // -shots <dir> -quit-after-shots -fixture <name> (saves <dir>/<fixture>.png after a fixed number of frames)
    // -title [-title-page howto|settings] (screenshots of the title screen, which tool runs skip otherwise)
    // -seed <n> -wander -warmup <ticks> -throw-demo <yards> -bind <ip> (127.0.0.1 for tools, so no firewall prompt) -look-x <yards> (camera look-at along the field)
    public sealed class CliArgs
    {
        public string ShotsDir;
        public bool QuitAfterShots;
        public string Fixture = "field_sideline";
        public int Seed = 1;
        public string Bind;
        public float? LookX;
        public bool Wander;           // -wander: the bots wander from the start (B toggles it in the game)
        public float? ThrowDemo;      // -throw-demo <yards>: the QB lobs the ball this far downfield at once (screenshots)
        public string Photo;          // -photo concept04: a frozen photo scene recreating a concept picture (also PP_SHOT=<name>)
        public bool CalledDemo;       // -called-demo: the offense has already called its play (screenshots of the TV banner)
        public int WarmupTicks;       // -warmup <ticks>: run the sim this many ticks before the first frame
        public bool Title;            // -title: show the title screen in a screenshot run too (TitleScreen.cs)
        public string TitlePage;      // -title-page howto | settings: open on that page (screenshots)

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
                else if (s == "-wander") a.Wander = true;
                else if (s == "-called-demo") a.CalledDemo = true;
                else if (s == "-title") a.Title = true;
                else if (s == "-title-page" && next != null) { a.TitlePage = next; i++; }
                else if (s == "-photo" && next != null) { a.Photo = next; i++; }
                else if (s == "-warmup" && next != null && int.TryParse(next, out int wu)) { a.WarmupTicks = wu; i++; }
                else if (s == "-throw-demo" && next != null && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float td)) { a.ThrowDemo = td; i++; }
                else if (s == "-seed" && next != null && int.TryParse(next, out int seed)) { a.Seed = seed; i++; }
                else if (s == "-look-x" && next != null && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float lx)) { a.LookX = lx; i++; }
            }
            return a;
        }

        public static CliArgs FromEnvironment() => Parse(Environment.GetCommandLineArgs());
    }
}
