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
    // The momentum gauntlet's camera (tools/motion.ps1): in the lab's "cuts" drill, drives "you" along a fixed run
    // (straight, small cuts back and forth, a full reverse at a sprint, a reverse at a jog, a hard 90 degree cut, a stop)
    // and saves a picture every few ticks, cropped around the runner, plus a CSV of his speed, facing and lean.
    // Screenshots run in lockstep (one tick a frame), so every camera sees exactly the same run.
    // -motion-cam tv | close | side | behind: the TV camera (live framing, following him), a close look from the TV side,
    // square from the side at his middle, or from behind his first run. -motion-run stride: the stride reel instead.
    // -motion-every <ticks> (default 2). Writes <shots>/motion-<cam>/<tick>.png and <shots>/motion-<cam>.csv, then quits.
    public sealed class MotionReel : MonoBehaviour
    {
        // The run: (ticks, move x, move z, sprint, label). +x is down the field (the lab attacks +x), +z away from the camera.
        public static readonly (int ticks, float x, float z, bool sprint, string label)[] Run =
        {
            (12, 0f, 0f, false, "stand"),
            (45, 1f, 0f, false, "start"),
            (15, 0.94f, 0.34f, false, "cuts"), (15, 0.94f, -0.34f, false, "cuts"), (15, 0.94f, 0.34f, false, "cuts"), (15, 0.94f, -0.34f, false, "cuts"),
            (30, 1f, 0f, true, "sprint"),
            (60, -1f, 0f, true, "reverse"),
            (20, -1f, 0f, false, "jog back"),
            (50, 1f, 0f, false, "reverse jog"),
            (30, 0f, 1f, false, "cut 90"),
            (30, 0f, 0f, false, "stop"),
        };

        // The stride reel (-motion-run stride, FEATURES.strideCycle): straight down the field at a walk, a jog and a
        // sprint, easing back down to a walk and a stop, for the arms and legs.
        public static readonly (int ticks, float x, float z, bool sprint, string label)[] StrideRun =
        {
            (12, 0f, 0f, false, "stand"),
            (60, 0.27f, 0f, false, "walk"),
            (45, 1f, 0f, false, "jog"),
            (45, 1f, 0f, true, "sprint"),
            (30, 1f, 0f, false, "ease"),
            (45, 0.27f, 0f, false, "walk again"),
            (30, 0f, 0f, false, "stop"),
        };

        static (int ticks, float x, float z, bool sprint, string label)[] Picked
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-motion-run" && args[i + 1] == "stride") return StrideRun;
                return Run;
            }
        }

        public static int Length { get { int n = 0; foreach (var r in Picked) n += r.ticks; return n; } }

        // What "you" is told to do on tick t of the run (t from 0), and that part's label.
        public static PlayerInput At(int t, out string label)
        {
            foreach (var r in Picked)
            {
                if (t < r.ticks) { label = r.label; return new PlayerInput { MoveX = r.x, MoveZ = r.z, Sprint = r.sprint }; }
                t -= r.ticks;
            }
            label = "end";
            return default;
        }

        PartyHost host;
        LabMode lab;
        CameraRig rig;
        GameConfig cfg;
        string cam, dir;
        int every, t = -1;
        readonly StringBuilder csv = new StringBuilder("tick,label,x,z,vx,vz,speed,yaw,lean_fwd,lean_side,plant,pose,stride,knee_l,knee_r,elbow_l,elbow_r,model_knee_l,model_knee_r,model_elbow_l,model_elbow_r,toe_l,ball_l\n");

        public static bool Requested(out string cam)
        {
            cam = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-motion-cam") cam = args[i + 1];
            return cam != null;
        }

        public static MotionReel Attach(GameObject on, GameConfig cfg, PartyHost host, LabMode lab, CameraRig rig, string shotsDir, string cam)
        {
            if (lab == null || lab.Lab == null || string.IsNullOrEmpty(shotsDir)) { Debug.LogWarning("[motion] needs -lab and -shots"); return null; }
            var m = on.AddComponent<MotionReel>();
            m.host = host; m.lab = lab; m.rig = rig; m.cfg = cfg; m.cam = cam;
            m.dir = Path.Combine(shotsDir, "motion-" + cam);
            Directory.CreateDirectory(m.dir);
            var args = System.Environment.GetCommandLineArgs();
            m.every = 2;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-motion-every" && int.TryParse(args[i + 1], out int e)) m.every = Mathf.Max(1, e);
            host.BeforeTick += m.Feed;
            return m;
        }

        void Feed(Dictionary<int, PlayerInput> inputs)
        {
            if (lab.Lab.You < 0) return;
            t++;
            inputs[lab.Lab.You] = At(t, out _);
        }

        Player Runner => lab.Lab.You >= 0 ? host.World.Find(lab.Lab.You) : null;

        void LateUpdate()
        {
            var p = Runner;
            if (p == null) return;
            var feet = host.Players.FeetPosition(p.Slot);
            float yd = cfg.field.yardM, h = cfg.view.playerHeightM;
            var chest = feet + Vector3.up * h * 0.55f;
            switch (cam)
            {
                case "close":   // from the TV's side and angle, about 9 yards across
                    rig.Hold(chest + Quaternion.Euler(cfg.camera.pitchDeg, 0f, 0f) * Vector3.back * 9f * yd, chest, 35f);
                    break;
                case "side":    // square from the TV's side at his middle, about 9 yards across: the whole of him, arms and legs
                    var mid = feet + Vector3.up * h * 0.5f;
                    rig.Hold(mid + Vector3.back * 9f * yd, mid, 35f);
                    break;
                case "behind":  // from behind his first run (down the field), a little above
                    rig.Hold(chest + new Vector3(-9f * yd, 2.5f * yd, 0f), chest, 35f);
                    break;
                default:        // the TV camera's live framing, following him
                    rig.SetZoom(1f);
                    rig.SetLookAtX(p.X);
                    break;
            }
        }

        IEnumerator Start()
        {
            int end = Length;
            while (t < end)
            {
                yield return new WaitForEndOfFrame();
                var p = Runner;
                if (p == null || t < 0) continue;
                At(t, out string label);
                Record(p, label);
                if (t % every == 0) Shoot(p);
            }
            File.WriteAllText(dir + ".csv", csv.ToString());
            Debug.Log("[motion] wrote " + dir + " (" + end + " ticks)");
            Application.Quit(0);
        }

        void Record(Player p, string label)
        {
            var ic = CultureInfo.InvariantCulture;
            float fwd = 0f, side = 0f, plant = 0f, yaw = float.NaN;
            if (host.Players.MomentumOf(p.Slot, out var ms)) { Momentum.Lean(ms, cfg.anim, out fwd, out side, out plant); yaw = ms.Yaw; }
            var fig = host.Players.Figure(p.Slot);
            float speed = Mathf.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
            csv.Append(string.Format(ic, "{0},{1},{2:0.00},{3:0.00},{4:0.00},{5:0.00},{6:0.00},{7:0.0},{8:0},{9:0},{10:0.0},{11},{12:0.000},{13}\n",
                t, label, p.X, p.Z, p.VX, p.VZ, speed, yaw, fwd, side, plant, fig != null ? fig.Current.ToString() : "", host.Players.StridePhaseOf(p.Slot), Bends(fig, ic)));
        }

        // the figure's knee and elbow bends, then the model's as drawn (the two should move together), then the left foot's
        // pitch and its toes' bend (FEATURES.footFlex)
        static string Bends(FootballFigure fig, CultureInfo ic)
        {
            if (fig == null) return ",,,,,,,,,";
            fig.Bends(out float kl, out float kr, out float el, out float er);
            var skin = fig.GetComponentInChildren<ModelSkin>();
            float mkl = float.NaN, mkr = float.NaN, mel = float.NaN, mer = float.NaN;
            if (skin != null) skin.Bends(out mkl, out mkr, out mel, out mer);
            return string.Format(ic, "{0:0},{1:0},{2:0},{3:0},{4:0},{5:0},{6:0},{7:0},{8:0},{9:0}", kl, kr, el, er, mkl, mkr, mel, mer, fig.ToeL, fig.BallL);
        }

        // A square around the runner (the TV camera: a third of the screen high; the close ones: most of it).
        void Shoot(Player p)
        {
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            var c = rig.Cam.WorldToScreenPoint(host.Players.FeetPosition(p.Slot) + Vector3.up * cfg.view.playerHeightM * 0.5f);
            int size = Mathf.Min(shot.height, Mathf.RoundToInt(shot.height * (cam == "close" || cam == "behind" || cam == "side" ? 0.9f : 0.34f)));
            int x0 = Mathf.Clamp(Mathf.RoundToInt(c.x) - size / 2, 0, shot.width - size);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(c.y) - size / 2, 0, shot.height - size);
            var crop = new Texture2D(size, size, TextureFormat.RGB24, false);
            crop.SetPixels(shot.GetPixels(x0, y0, size, size));
            crop.Apply();
            File.WriteAllBytes(Path.Combine(dir, t.ToString("0000") + ".png"), crop.EncodeToPNG());
            Destroy(shot); Destroy(crop);
        }
    }
}
