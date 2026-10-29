using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Drive blocks (FEATURES.driveBlock; NO_DRIVE_BLOCK=1 turns it off): a person holding BLOCK also has a drive stick
    // (on the phone the BLOCK button turns into one: touch it to block, slide to drive). While he has his man held up
    // (Blocking.cs), pushing it shoves that man the stick's way at driveBlock.pushYdS, and the blocker steps with him so
    // the two stay locked. It is how a line opens a gap for a run up the middle.
    //  size:     FEATURES.ratings: the shove is strengthPerPoint stronger per point the blocker is bigger than his man
    //            (weaker if smaller), kept between minFrac and maxFrac.
    //  leverage: a defender leans where he is trying to go (a bot at the ball, a person wherever his stick says). A
    //            shove against that loses fightFrac of its strength at worst, so turning a man sideways is easy and
    //            driving him straight back is not; and while he fights it (more than fightShedAbove against the
    //            shove) his shed clock runs at double speed, so a bad drive gets him free sooner.
    //  stamina:  driving drains the blocker's stamina at staminaPerS; a tired man shoves at tiredFrac.
    // Bots never drive: it is a thing people do. The lane (Lane) is for the TV only (View: DriveBlockView).
    public static class DriveBlock
    {
        public static bool On(World w) => w.Cfg.Feature("driveBlock");

        // One shove this tick, for the TV: who, on whom, and its velocity (yards per second).
        public struct Push { public int Blocker, Defender; public float VX, VZ; }

        // Not hashed: Pushes is rebuilt every tick and DrivenAt only feeds the lane on the TV (the shoves themselves
        // move players, whose positions are hashed).
        public sealed class State
        {
            public readonly List<Push> Pushes = new List<Push>();
            public readonly SortedDictionary<int, int> DrivenAt = new SortedDictionary<int, int>();   // defender slot -> last tick shoved
            public int Snap = -1;                                                                       // the play the marks belong to
        }

        public static void Step(World w, IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            var s = w.Drive;
            s.Pushes.Clear();
            if (s.Snap != w.PlaysRun) { s.Snap = w.PlaysRun; s.DrivenAt.Clear(); }
            if (!On(w) || !LivePlay.On(w) || w.Phase != Phase.Live || inputs == null) return;
            var c = w.Cfg.driveBlock;
            float dt = w.Dt;
            var ball = LivePlay.Carrier(w);
            foreach (var o in w.Players)
            {
                if (!o.Human || o.BotHelp || !o.Blocking || o == ball) continue;
                if (!inputs.TryGetValue(o.Slot, out var input)) continue;
                float ux = input.DriveX, uz = input.DriveZ;
                float m = (float)Math.Sqrt(ux * ux + uz * uz);
                if (m < c.deadZone) continue;
                ux /= m; uz /= m;
                if (m > 1f) m = 1f;
                var d = Engaged(w, o);
                if (d == null) continue;

                float push = c.pushYdS * m * Strength(w, o, d) * (o.Tired ? c.tiredFrac : 1f);
                float against = Against(d, ux, uz);
                push *= 1f - c.fightFrac * against;
                if (against > c.fightShedAbove) d.BlockedTicks++;   // fighting it: the shed clock runs double

                float vx = ux * push, vz = uz * push;
                d.X += vx * dt; d.Z += vz * dt;
                o.X += vx * dt; o.Z += vz * dt;
                o.Stamina -= c.staminaPerS * dt;
                if (o.Stamina <= 0f) { o.Stamina = 0f; o.Tired = true; o.Sprinting = false; }
                s.Pushes.Add(new Push { Blocker = o.Slot, Defender = d.Slot, VX = vx, VZ = vz });
                s.DrivenAt[d.Slot] = w.Tick;
            }
        }

        // The man this blocker has held up right now: the nearest opponent in block reach that a block is holding.
        public static Player Engaged(World w, Player o)
        {
            float reach = w.Cfg.bots.blockReachYd, best = reach * reach;
            Player man = null;
            foreach (var d in w.Players)
            {
                if (d.Team == o.Team || d.BlockedTicks <= 0 || d.FreeTicks > 0) continue;
                float dx = d.X - o.X, dz = d.Z - o.Z, d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                best = d2; man = d;
            }
            return man;
        }

        // The shove's strength for this pair, from their sizes (1 with FEATURES.ratings off).
        public static float Strength(World w, Player blocker, Player defender)
        {
            var c = w.Cfg.driveBlock;
            float f = 1f + c.strengthPerPoint * Ratings.SizeEdge(w, blocker, defender);
            return Math.Max(c.minFrac, Math.Min(c.maxFrac, f));
        }

        // How hard the defender leans against a shove along (ux, uz): 0 = not at all (standing, or going with it or
        // across it) .. 1 = straight against it.
        public static float Against(Player d, float ux, float uz)
        {
            float v = (float)Math.Sqrt(d.VX * d.VX + d.VZ * d.VZ);
            if (v <= 0f) return 0f;
            return Math.Max(0f, -(d.VX * ux + d.VZ * uz) / v);
        }

        // The lane: the widest gap between neighbouring defenders in the box in front of a run (from laneBehindYd on the
        // offense's side of the line to laneDepthYd past it, laneHalfWidthYd either side of the carrier), while the
        // carrier is on offense and no more than laneDepthYd past the line. Open when it is laneOpenYd wide and a man
        // at one of its edges was shoved in the last laneMemoryS. zMid, width: its middle and width (yards).
        public static bool Lane(World w, out float zMid, out float width)
        {
            zMid = 0f; width = 0f;
            if (!On(w) || w.Phase != Phase.Live || w.Thrown || w.Intercepted) return false;
            var carrier = LivePlay.Carrier(w);
            if (carrier == null || carrier.Team != w.Offense) return false;
            var c = w.Cfg.driveBlock;
            if ((carrier.X - w.LineOfScrimmageX) * w.Dir > c.laneDepthYd) return false;
            Player lo = null, hi = null;
            float best = 0f;
            var box = Box(w, carrier.Z);
            for (int i = 0; i + 1 < box.Count; i++)
            {
                float gap = box[i + 1].Z - box[i].Z;
                if (gap <= best) continue;
                best = gap; lo = box[i]; hi = box[i + 1];
            }
            if (lo == null || best < c.laneOpenYd) return false;
            if (!Recent(w, lo) && !Recent(w, hi)) return false;
            zMid = (lo.Z + hi.Z) * 0.5f;
            width = best;
            return true;
        }

        static readonly List<Player> boxed = new List<Player>();

        // The defenders in the box, by Z (then slot, so ties never depend on order).
        static List<Player> Box(World w, float z0)
        {
            var c = w.Cfg.driveBlock;
            boxed.Clear();
            foreach (var d in w.Players)
            {
                if (d.Team != w.Defense) continue;
                float past = (d.X - w.LineOfScrimmageX) * w.Dir;
                if (past < -c.laneBehindYd || past > c.laneDepthYd || Math.Abs(d.Z - z0) > c.laneHalfWidthYd) continue;
                boxed.Add(d);
            }
            boxed.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Slot.CompareTo(b.Slot));
            return boxed;
        }

        static bool Recent(World w, Player d) =>
            w.Drive.DrivenAt.TryGetValue(d.Slot, out int at) && w.Tick - at <= w.Cfg.driveBlock.laneMemoryS * w.Cfg.sim.tickHz;
    }

    // game.json "driveBlock" (FEATURES.driveBlock). Shares are 0..1; pushYdS in yards per second.
    public sealed class DriveBlockConfig
    {
        public float deadZone;                         // the drive stick does nothing nearer the middle than this (0..1)
        public float pushYdS;                          // a full shove, even sizes, nobody fighting it
        public float strengthPerPoint, minFrac, maxFrac;   // size: per point bigger than his man, and the limits
        public float fightFrac, fightShedAbove;        // leverage: the most a defender's lean takes off, and when it speeds his shed
        public float staminaPerS, tiredFrac;           // driving's cost, and a tired man's shove
        public float laneBehindYd, laneDepthYd, laneHalfWidthYd, laneOpenYd, laneMemoryS;   // the lane on the TV
    }
}
