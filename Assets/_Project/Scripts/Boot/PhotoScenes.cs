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
            const float z = -14f;
            void Cast(int team, int spot, int number, string pos, float x, Pose pose)
            {
                var p = Spot(w, team, spot);
                if (p == null) return;
                p.Number = number; p.Pos = pos;
                Put(w, view, team, spot, x, z, pose);
            }
            Cast(away, 6, 68, "OL", -7.6f, Pose.Stance);
            Cast(away, 0, 7, "QB", -4.6f, Pose.Throw);
            Cast(away, 1, 24, "RB", -1.7f, Pose.Carry);
            Cast(home, 0, 9, "QB", 1.7f, Pose.Throw);
            Cast(home, 1, 21, "RB", 4.6f, Pose.Carry);
            Cast(home, 7, 64, "DL", 7.6f, Pose.Stance);
            float m = w.Cfg.field.yardM;
            rig.Hold(new Vector3(0f, 1.7f, z * m - 12f), new Vector3(0f, 1.25f, z * m), 21f);
            return new PhotoState { Name = "concept01", Hud = false, FirstDownX = -35f };
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
