using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The bots' film (tools/botreel.ps1): in the lab, on a bots-only drill (lab.json "watch..." drills: a punt, a run,
    // a pass), saves the TV picture every -bot-reel <ticks> from the snap to the whistle, half size, into
    // <shots>/botreel-<drill>/<tick>.png, plus a CSV of who had the ball and who was being held up by a block each
    // frame, then quits. -bot-reel-reps <n>: film that many plays of the drill (default 1). Screenshots run in lockstep
    // (one tick a frame), so the same seed films the same play.
    public sealed class BotReel : MonoBehaviour
    {
        PartyHost host;
        LabMode lab;
        string dir;
        int every = 6, reps = 1, filmed, lastStarts = -1;
        bool wasLive;
        readonly StringBuilder csv = new StringBuilder("tick,phase,play_s,ball,carrier,carrier_team,held_up,blocking,result\n");

        public static bool Requested(out int every)
        {
            every = 0;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-bot-reel" && int.TryParse(args[i + 1], out every)) return every > 0;
            return false;
        }

        public static BotReel Attach(GameObject on, PartyHost host, LabMode lab, string shotsDir, int every)
        {
            if (lab == null || lab.Lab == null || string.IsNullOrEmpty(shotsDir)) { Debug.LogWarning("[botreel] needs -lab and -shots"); return null; }
            var m = on.AddComponent<BotReel>();
            m.host = host; m.lab = lab; m.every = Mathf.Max(1, every);
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-bot-reel-reps" && int.TryParse(args[i + 1], out int r)) m.reps = Mathf.Max(1, r);
            m.dir = Path.Combine(shotsDir, "botreel-" + lab.Lab.Drill.id);
            if (Directory.Exists(m.dir)) Directory.Delete(m.dir, true);
            Directory.CreateDirectory(m.dir);
            return m;
        }

        IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                var w = host.World;
                bool live = w.Phase == Phase.Live;
                if (lab.Lab.Starts != lastStarts) { lastStarts = lab.Lab.Starts; wasLive = false; }
                if (live && w.PlayTicks % every == 0) Shoot(w);
                if (!live && wasLive)
                {
                    Shoot(w);   // the whistle
                    filmed++;
                    if (filmed >= reps) break;
                }
                wasLive = live;
            }
            File.WriteAllText(dir + ".csv", csv.ToString());
            Debug.Log("[botreel] wrote " + dir + " (" + filmed + " plays)");
            Application.Quit(0);
        }

        void Shoot(World w)
        {
            var ic = CultureInfo.InvariantCulture;
            var carrier = LivePlay.Carrier(w);
            int held = 0, blocking = 0;
            foreach (var p in w.Players) { if (p.BlockedTicks > 0) held++; if (p.Blocking) blocking++; }
            csv.Append(string.Format(ic, "{0},{1},{2:0.00},{3},{4},{5},{6},{7},{8}\n", w.Tick, w.Phase, w.PlayTicks / (float)w.Cfg.sim.tickHz, w.Ball.State,
                carrier != null ? carrier.Pos + "#" + carrier.Number : "", carrier != null ? carrier.Team : -1, held, blocking, w.Result));
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            // half size, averaging each 2x2 (a Blit through a render texture washed the colours out)
            int hw = shot.width / 2, hh = shot.height / 2;
            var src = shot.GetPixels32();
            var dst = new Color32[hw * hh];
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                {
                    int i = (y * 2) * shot.width + x * 2;
                    Color32 a = src[i], b = src[i + 1], c = src[i + shot.width], d = src[i + shot.width + 1];
                    dst[y * hw + x] = new Color32((byte)((a.r + b.r + c.r + d.r) / 4), (byte)((a.g + b.g + c.g + d.g) / 4), (byte)((a.b + b.b + c.b + d.b) / 4), 255);
                }
            var small = new Texture2D(hw, hh, TextureFormat.RGB24, false);
            small.SetPixels32(dst);
            small.Apply();
            File.WriteAllBytes(Path.Combine(dir, filmed.ToString("0") + "-" + w.Tick.ToString("00000") + ".png"), small.EncodeToPNG());
            Destroy(shot); Destroy(small);
        }
    }
}
