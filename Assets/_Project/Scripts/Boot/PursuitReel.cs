using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Boot
{
    // The pursuit gauntlet's camera (tools/pursuit.ps1): in the lab's "pursuit" drill, drives "you" (the ball carrier,
    // already in the open) along one set run a rep (straight down the field, a sweep to the sideline, a cut back, a
    // zigzag) while the whole defense chases him, and saves a picture every few ticks from the snap to the whistle plus
    // a CSV of every chaser's spot each tick (for the top-down trails) and how bunched up they were.
    // -pursuit-reel top | tv | close: straight down over the chase, the TV camera, or close from the TV side on the
    // carrier and the chaser nearest him (the breakdown and the tackle). -pursuit-every <ticks> (default 3),
    // -pursuit-reps <n> (default 4: each run once). Writes <shots>/pursuit-<cam>/<rep>-<tick>.png, <shots>/pursuit-<cam>.csv
    // and <shots>/pursuit-<cam>.txt (one line a rep), then quits. Screenshots run in lockstep (one tick a frame), so
    // every camera sees exactly the same chase.
    public sealed class PursuitReel : MonoBehaviour
    {
        public static readonly string[] Runs = { "straight", "sweep", "cutback", "zigzag" };

        // What the carrier is told to do s seconds into a rep of this run (+x down the field, the lab attacks +x; +z away
        // from the camera).
        public static PlayerInput At(string run, float s)
        {
            switch (run)
            {
                case "sweep": return s < 1.4f ? Go(0.55f, 0.83f) : Go(1f, 0.12f);                  // out to the far sideline, then up it
                case "cutback": return s < 1.2f ? Go(0.5f, 0.86f) : s < 2.4f ? Go(0.6f, -0.8f) : Go(1f, 0f);   // flow wide, plant and cut back across
                case "zigzag": return ((int)(s / 0.8f)) % 2 == 0 ? Go(0.85f, 0.53f) : Go(0.85f, -0.53f);
                default: return Go(1f, 0f);
            }
        }

        static PlayerInput Go(float x, float z) => new PlayerInput { MoveX = x, MoveZ = z, Sprint = true };

        PartyHost host;
        LabMode lab;
        CameraRig rig;
        GameConfig cfg;
        string cam, dir;
        int every = 3, reps = 4, filmed, lastStarts = -1, near = -1, nearSince;
        bool wasLive;
        readonly StringBuilder csv = new StringBuilder();
        readonly StringBuilder txt = new StringBuilder();
        readonly List<int> chasers = new List<int>();
        int bunched, ticks, gapN;
        float gapSum, gapMin = float.MaxValue;

        public static bool Requested(out string cam)
        {
            cam = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-pursuit-reel") cam = args[i + 1];
            return cam != null;
        }

        public static PursuitReel Attach(GameObject on, GameConfig cfg, PartyHost host, LabMode lab, CameraRig rig, string shotsDir, string cam)
        {
            if (lab == null || lab.Lab == null || string.IsNullOrEmpty(shotsDir) || lab.Lab.You < 0) { Debug.LogWarning("[pursuit] needs -lab -lab-drill pursuit and -shots"); return null; }
            var m = on.AddComponent<PursuitReel>();
            m.host = host; m.lab = lab; m.rig = rig; m.cfg = cfg; m.cam = cam;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-pursuit-every" && int.TryParse(args[i + 1], out int e)) m.every = Mathf.Max(1, e);
                if (args[i] == "-pursuit-reps" && int.TryParse(args[i + 1], out int r)) m.reps = Mathf.Max(1, r);
            }
            m.dir = Path.Combine(shotsDir, "pursuit-" + cam);
            if (Directory.Exists(m.dir)) Directory.Delete(m.dir, true);
            Directory.CreateDirectory(m.dir);
            host.BeforeTick += m.Feed;
            return m;
        }

        string Run => Runs[filmed % Runs.Length];

        void Feed(Dictionary<int, PlayerInput> inputs)
        {
            var w = host.World;
            if (lab.Lab.You < 0) return;
            inputs[lab.Lab.You] = w.Phase == Phase.Live ? At(Run, w.PlayTicks / (float)w.Cfg.sim.tickHz) : default;
        }

        Player Carrier => LivePlay.Carrier(host.World);

        // the chaser nearest the carrier (kept for at least a third of a second, so the close camera doesn't flick about)
        Player Nearest(World w, Player c)
        {
            Player best = null; float bd = float.MaxValue;
            foreach (var p in w.Players)
            {
                if (p.Team == c.Team) continue;
                float d = LivePlay.Dist(p, c);
                if (d < bd) { bd = d; best = p; }
            }
            var held = w.Find(near);
            if (held != null && held.Team != c.Team && w.Tick - nearSince < w.Cfg.sim.tickHz / 3) return held;
            if (best != null && best.Slot != near) { near = best.Slot; nearSince = w.Tick; }
            return best;
        }

        void LateUpdate()
        {
            var w = host.World;
            var c = Carrier;
            if (c == null) return;
            float yd = cfg.field.yardM, h = cfg.view.playerHeightM;
            var feet = host.Players.FeetPosition(c.Slot);
            switch (cam)
            {
                case "top":     // high over the chase, the whole width of the field in, a little ahead of the carrier
                    var mid = new Vector3(feet.x + 6f * yd, 0f, 0f);
                    rig.Hold(mid + new Vector3(0f, 44f * yd, -8f * yd), mid, 40f);
                    break;
                case "close":   // from the TV's side on the carrier and the chaser nearest him
                    var n = Nearest(w, c);
                    var other = n != null ? host.Players.FeetPosition(n.Slot) : feet;
                    var at = (feet + other) * 0.5f + Vector3.up * h * 0.5f;
                    float back = Mathf.Max(9f * yd, Mathf.Abs(feet.x - other.x) * 1.4f + 4f * yd);
                    rig.Hold(at + Quaternion.Euler(10f, 0f, 0f) * Vector3.back * back, at, 35f);
                    break;
                default:        // the TV camera's live framing, following him
                    rig.SetZoom(1f);
                    rig.SetLookAtX(c.X);
                    break;
            }
        }

        IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                var w = host.World;
                bool live = w.Phase == Phase.Live;
                if (lab.Lab.Starts != lastStarts) { lastStarts = lab.Lab.Starts; wasLive = false; }
                if (live)
                {
                    if (!wasLive) Begin(w);
                    Record(w);
                    if (w.PlayTicks % every == 0) Shoot(w);
                }
                if (!live && wasLive)
                {
                    Shoot(w);   // the whistle
                    End(w);
                    filmed++;
                    if (filmed >= reps) break;
                }
                wasLive = live;
            }
            File.WriteAllText(dir + ".csv", csv.ToString());
            File.WriteAllText(dir + ".txt", txt.ToString());
            Debug.Log("[pursuit] wrote " + dir + " (" + filmed + " reps)");
            Application.Quit(0);
        }

        void Begin(World w)
        {
            chasers.Clear();
            var c = Carrier;
            foreach (var p in w.Players) if (c != null && p.Team != c.Team) chasers.Add(p.Slot);
            if (csv.Length == 0)
            {
                csv.Append("rep,run,tick,play_s,carrier_x,carrier_z,hitter,bunched,min_gap,mean_gap");
                foreach (int s in chasers) { var p = w.Find(s); csv.Append(",").Append(p.Pos).Append(p.Number).Append("_x,").Append(p.Pos).Append(p.Number).Append("_z"); }
                csv.Append("\n");
            }
            bunched = ticks = gapN = 0; gapSum = 0f; gapMin = float.MaxValue;
        }

        // Each tick: where everyone is, and how bunched up the chasers away from the ball are (two closer than
        // bunchedYd, both further than pursuit.convergeYd from the carrier: piling into each other, not tackling).
        void Record(World w)
        {
            var c = Carrier;
            if (c == null) return;
            var ic = CultureInfo.InvariantCulture;
            float far = cfg.pursuit.convergeYd, close = cfg.players.radiusYd * 2f + 0.4f;
            int pairs = 0; float min = float.MaxValue, sum = 0f; int n = 0;
            for (int i = 0; i < chasers.Count; i++)
            {
                var a = w.Find(chasers[i]);
                if (a == null || LivePlay.Dist(a, c) <= far) continue;
                float nearest = float.MaxValue;
                for (int j = 0; j < chasers.Count; j++)
                {
                    var b = w.Find(chasers[j]);
                    if (i == j || b == null || LivePlay.Dist(b, c) <= far) continue;
                    float d = LivePlay.Dist(a, b);
                    nearest = Mathf.Min(nearest, d);
                    if (j > i && d < close) pairs++;
                }
                if (nearest < float.MaxValue) { min = Mathf.Min(min, nearest); sum += nearest; n++; }
            }
            bunched += pairs; ticks++;
            if (n > 0) { gapSum += sum; gapN += n; gapMin = Mathf.Min(gapMin, min); }
            csv.Append(string.Format(ic, "{0},{1},{2},{3:0.000},{4:0.00},{5:0.00},{6},{7},{8:0.00},{9:0.00}", filmed, Run, w.Tick, w.PlayTicks / (float)w.Cfg.sim.tickHz,
                c.X, c.Z, w.ChasePlan.Tick == w.Tick ? w.ChasePlan.Hitter : -1, pairs, n > 0 ? min : -1f, n > 0 ? sum / n : -1f));
            foreach (int s in chasers)
            {
                var p = w.Find(s);
                csv.Append(p != null ? string.Format(ic, ",{0:0.00},{1:0.00}", p.X, p.Z) : ",,");
            }
            csv.Append("\n");
        }

        void End(World w)
        {
            var ic = CultureInfo.InvariantCulture;
            txt.Append(string.Format(ic, "rep {0} {1}: {2} after {3:0.0} s, bunched pair-ticks {4}, closest two chasers {5:0.0} yd, average gap to the nearest team-mate {6:0.0} yd\n",
                filmed, Run, string.IsNullOrEmpty(w.Result) ? "(no whistle)" : w.Result, ticks / (float)w.Cfg.sim.tickHz, bunched,
                gapMin < float.MaxValue ? gapMin : 0f, gapN > 0 ? gapSum / gapN : 0f));
        }

        // The whole screen, half size (each 2x2 averaged), named <rep>-<tick>.
        void Shoot(World w)
        {
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
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
