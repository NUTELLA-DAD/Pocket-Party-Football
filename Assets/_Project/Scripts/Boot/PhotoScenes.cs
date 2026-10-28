using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using Pose = PocketFootball.View.FootballFigure.PoseKind;

namespace PocketFootball.Boot
{
    // Fixed, seeded "photo" scenes that recreate the concept pictures in docs/art/concepts (the gauntlet loop):
    // the same moment and the same camera, frozen. Chosen with -photo <name> or the environment PP_SHOT=<name>.
    // Scene setup only moves players and picks poses; the sim's rules are untouched and nothing steps.
    public sealed class PhotoState
    {
        public string Name;
        public int[] Score = { 0, 0 };
        public string Quarter = "1ST", Clock = "4:00", Down = "1ST & 10";
        public float FirstDownX;
        public readonly Dictionary<int, string> Badges = new Dictionary<int, string>();
        public bool Hud = true;
        // big team wordmarks across the top (text, colour, centre as a share of the screen width)
        public readonly List<(string text, string color, float x)> Titles = new List<(string, string, float)>();
    }

    public static class PhotoScenes
    {
        public static string Requested(CliArgs args)
        {
            if (!string.IsNullOrEmpty(args.Photo)) return args.Photo;
            var env = System.Environment.GetEnvironmentVariable("PP_SHOT");
            return string.IsNullOrEmpty(env) ? null : env;
        }

        public static PhotoState Setup(string name, World w, PlayersView view, CameraRig rig)
        {
            switch (name)
            {
                case "concept01": return Concept01(w, view, rig);
                case "concept04": return Concept04(w, view, rig);
                case "moves": return MovesLineup(w, view, rig);
                case "hands": return Hands(w, view, rig, "hands", new[] { Pose.Reach, Pose.Catch, Pose.Secure, Pose.Carry });
                case "hands2": return Hands(w, view, rig, "hands2", new[] { Pose.Throw, Pose.Run, Pose.Block, Pose.Stand });
                case "stadium": return StadiumShot(w, view, rig, new Vector3(-40f, 45f, -175f), new Vector3(0f, 25f, 0f), 50f);
                case "sunset": return StadiumShot(w, view, rig, new Vector3(-25f, 3f, -18f), new Vector3(3.7f, 13.6f, 23f), 55f);
            }
            Debug.LogWarning("[photo] unknown scene " + name);
            return null;
        }

        static Player Spot(World w, int team, int spot)
        {
            foreach (var p in w.Players) if (p.Team == team && p.Spot == spot) return p;
            return null;
        }

        static void Put(World w, PlayersView view, int team, int spot, float x, float z, Pose pose, float vx = 0, float vz = 0)
        {
            var p = Spot(w, team, spot);
            if (p == null) return;
            p.X = x; p.Z = z; p.VX = vx; p.VZ = vz;
            view.PoseOverride[p.Slot] = pose;
        }

        // The stadium and the sunset sky (stadium.bowl, sky): concept 04's moment on the field, seen from a held camera
        // ("stadium": from high outside the near corner; "sunset": low on the field, looking into the sun). No HUD.
        static PhotoState StadiumShot(World w, PlayersView view, CameraRig rig, Vector3 pos, Vector3 lookAt, float fovDeg)
        {
            var st = Concept04(w, view, rig);
            st.Name = pos.y > 20f ? "stadium" : "sunset";
            st.Hud = false;
            st.Badges.Clear();
            rig.Hold(pos, lookAt, fovDeg);
            return st;
        }

        // Every move pose side by side (View/Moves.cs), for checking the look: the front row the Chiefs, the back row
        // the Roughriders, each facing his own way of play.
        static PhotoState MovesLineup(World w, PlayersView view, CameraRig rig)
        {
            w.Offense = 0;
            w.LineOfScrimmageX = 0f;
            w.LineUp();
            Pose[] front = { Pose.Throw, Pose.Release, Pose.Handoff, Pose.Reach, Pose.Secure, Pose.Bobble, Pose.Juke, Pose.Whiff, Pose.Dive, Pose.FallFront, Pose.Prone };
            Pose[] back = { Pose.FallBack, Pose.Supine, Pose.Kneel, Pose.Celebrate, Pose.Block, Pose.Ready, Pose.Run, Pose.Carry, Pose.Catch, Pose.Stance, Pose.Stand };
            for (int i = 0; i < front.Length; i++) Put(w, view, 0, i, -19f + i * 3.8f, -7f, front[i]);
            for (int i = 0; i < back.Length; i++) Put(w, view, 1, i, -19f + i * 3.8f, 1f, back[i]);
            rig.SetLookAtX(0f);
            return new PhotoState { Name = "moves", Hud = false, FirstDownX = 10f };
        }

        // The hands up close (FEATURES.handCurl): four Chiefs side by side in poses that open, shut or hold the hand (a
        // ball in the carrier's and the thrower's right hands), seen square from the side at chest height. No HUD.
        static PhotoState Hands(World w, PlayersView view, CameraRig rig, string name, Pose[] poses)
        {
            const int team = 0;
            w.Offense = team;
            w.Dir = 1f;
            w.LineOfScrimmageX = -45f;
            w.LineUp();
            foreach (var p in w.Players) { p.X = p.Team == team ? -90f : 90f; p.Z = 0f; p.VX = 0f; p.VZ = 0f; }
            const float cx = 22.5f, z = -6f, gap = 1.8f;   // (yards; spot 0, the QB with the game ball, stays off)
            for (int i = 0; i < poses.Length; i++)
            {
                Put(w, view, team, i + 1, cx + (i - (poses.Length - 1) * 0.5f) * gap, z, poses[i]);
                var p = Spot(w, team, i + 1);
                if (p != null && (poses[i] == Pose.Carry || poses[i] == Pose.Throw)) view.PropBalls[p.Slot] = true;
            }
            float m = w.Cfg.field.yardM;
            rig.Hold(new Vector3(cx * m, 1.3f, z * m - 9f), new Vector3(cx * m, 1.3f, z * m), 30f);
            return new PhotoState { Name = name, Hud = false, FirstDownX = -35f };
        }

        // Concept 01, the team lineup: three Chiefs (68 in his stance, QB 7 throwing, RB 24 carrying) facing three
        // Roughriders (QB 9, RB 21, 64), close up on the grass, no HUD. Everyone else stands far off, out of the shot.
        static PhotoState Concept01(World w, PlayersView view, CameraRig rig)
        {
            const int home = 1, away = 0;
            w.Offense = away;
            w.Dir = 1f;   // the Chiefs face right, the Roughriders left (each turned toward the camera)
            w.LineOfScrimmageX = -45f;
            w.LineUp();
            foreach (var p in w.Players) { p.X = p.Team == away ? -90f : 90f; p.Z = 0f; p.VX = 0f; p.VZ = 0f; }
            const float z = -6f;   // (between the yard numbers and the hashes: plain grass under them)
            const float cx = 22.5f;   // (between the 20 and 25 lines: a few plain lines, away from the centre logo)
            void Cast(int team, int spot, int number, string pos, float x, Pose pose)
            {
                var p = Spot(w, team, spot);
                if (p == null) return;
                p.Number = number; p.Pos = pos;
                Put(w, view, team, spot, cx + x, z, pose);
            }
            // shoulder to shoulder like the picture (the camera sees all six at its 2.5:1 frame)
            // evenly spaced, none hiding another (the round-5 review)
            Cast(away, 6, 68, "OL", -4.4f, Pose.Stance);
            Cast(away, 0, 7, "QB", -2.6f, Pose.Throw);
            Cast(away, 1, 24, "RB", -1.15f, Pose.Carry);
            Cast(home, 0, 9, "QB", 1.45f, Pose.Handoff);   // (ball held out in front, as concept 01's 9: the throw hides it)
            Cast(home, 1, 21, "RB", 2.85f, Pose.Carry);
            Cast(home, 7, 64, "DL", 4.4f, Pose.Stance);
            // a football each for the QBs and backs (the Chiefs' QB already holds the game ball, right hand)
            foreach (var (team, spot, right) in new[] { (away, 1, false), (home, 0, true), (home, 1, false) })
            {
                var p = Spot(w, team, spot);
                if (p != null) view.PropBalls[p.Slot] = right;
            }
            float m = w.Cfg.field.yardM;
            rig.Hold(new Vector3(cx * m, 1.7f, z * m - 10.6f), new Vector3(cx * m, 1.35f, z * m), 22f);
            // the picture's plain cream card behind them, hiding the stadium
            var card = Looks.Flat(null, "Backdrop", new Vector3(cx * m, 6f, z * m + 7f), new Vector2(40f, 12f), Colors.Hex("#fbfaf6"), Looks.Toon(null), Looks.Paint);
            card.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            // (lit like the grass: its normals point up, so the toon shade never turns it lavender)
            var cm = card.GetComponent<MeshFilter>().mesh;
            cm.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            var st = new PhotoState { Name = "concept01", Hud = false, FirstDownX = -35f };
            // the picture's wordmarks over each team's half
            st.Titles.Add(("CHIEFS", w.Cfg.teams[away].color, 0.3f));
            st.Titles.Add(("ROUGHRIDERS", w.Cfg.teams[home].color, 0.7f));
            return st;
        }

        // Concept 04, the in-game camera: the Roughriders' QB drops back to throw from their own 40 (offense going right),
        // the lines engaged, receivers A and B out in their routes, the defence reading; 3RD 2:18, 2ND & 10.
        static PhotoState Concept04(World w, PlayersView view, CameraRig rig)
        {
            const int home = 1, away = 0;
            w.Offense = home;
            w.LineOfScrimmageX = -10f;
            w.LineUp();
            float los = w.LineOfScrimmageX;
            // offense (Roughriders): QB throwing, RB in protection, the line engaged, receivers on their routes
            Put(w, view, home, 0, los - 7.2f, 4.2f, Pose.Throw);
            Put(w, view, home, 1, los - 5.6f, -5.4f, Pose.Ready);
            Put(w, view, home, 2, los + 18.5f, -13.5f, Pose.Run, 6, 0);        // B
            Put(w, view, home, 3, los + 14.5f, 20.5f, Pose.Run, 6, 0);         // A
            Put(w, view, home, 4, los + 22f, 9f, Pose.Run, 5, 2);
            Put(w, view, home, 5, los + 18.5f, -2.5f, Pose.Run, 5, -1);
            float[] olZ = { 0f, -2.6f, 2.6f, -5.2f, 5.2f };
            for (int i = 0; i < 5; i++) Put(w, view, home, 6 + i, los - 2.1f - (i % 2) * 0.5f + olZ[i] * 0.18f, olZ[i] * 1.5f - 1f, Pose.Block);
            // defence (Chiefs): the line engaged, linebackers and backs reading
            float[] dlZ = { -1.5f, 1.5f, -4.5f, 4.5f };
            for (int i = 0; i < 4; i++) Put(w, view, away, 7 + i, los + 0.6f + (i % 2) * 0.5f + dlZ[i] * 0.18f, dlZ[i] * 1.5f - 1f, Pose.Block);
            Put(w, view, away, 0, los + 21.5f, -2.5f, Pose.Ready);          // on the TE
            Put(w, view, away, 1, los + 21.5f, -14.5f, Pose.Backpedal, -2, 0);   // on B
            Put(w, view, away, 2, los + 17.5f, 20f, Pose.Backpedal, -2, 0);     // on A
            Put(w, view, away, 3, los + 30f, 1.5f, Pose.Ready);
            Put(w, view, away, 4, los + 31f, 19.5f, Pose.Ready);
            Put(w, view, away, 5, los + 25f, 9f, Pose.Ready);              // on the slot
            Put(w, view, away, 6, los + 4.5f, 3.5f, Pose.Ready);            // a linebacker over the line (not alone on the logo)
            var qb = Spot(w, home, 0);
            if (qb != null) qb.Human = true;
            rig.SetLookAtX(0f);
            var st = new PhotoState
            {
                Name = "concept04", Score = new[] { 14, 21 }, Quarter = "3RD", Clock = "2:18", Down = "2ND & 10",
                FirstDownX = los + 10f,
            };
            var a = Spot(w, home, 3); var b = Spot(w, home, 2);
            if (a != null) st.Badges[a.Slot] = "A";
            if (b != null) st.Badges[b.Slot] = "B";
            return st;
        }
    }
}
