using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Drive rushes (FEATURES.driveRush; NO_DRIVE_RUSH=1 turns it off): the defensive lineman's answer to the drive block
    // (DriveBlock.cs). On the phone the BULL RUSH button turns into a stick: touching it is the old bull rush press,
    // and sliding it shoves the blocker who has him held up (Blocking.cs) the stick's way at driveRush.pushYdS, the
    // rusher stepping with him so the two stay locked. It is how a person on the line opens a gap for a teammate, or
    // folds the pocket in on the QB.
    //  size:     FEATURES.ratings: the shove is strengthPerPoint stronger per point the rusher is bigger than the
    //            blocker (weaker if smaller), kept between minFrac and maxFrac.
    //  leverage: a blocker leans into his man, away from the ball he is guarding. A shove toward the ball runs into
    //            that lean and loses fightFrac of its strength at worst, so turning a blocker sideways is easy and
    //            driving him straight back into the QB is not. A shove mostly across the lean (less than
    //            shedAcrossBelow against it) is a rip: the rusher's shed clock runs at double speed, so he gets free
    //            sooner (and leaves a gap where the blocker was).
    //  stamina:  driving drains the rusher's stamina at staminaPerS; a tired man shoves at tiredFrac.
    // Bots never drive: it is a thing people do. The arrows on the TV come from Pushes (View: DriveBlockView).
    public static class DriveRush
    {
        public static bool On(World w) => w.Cfg.Feature("driveRush");

        // One shove this tick, for the TV: who, on whom, and its velocity (yards per second).
        public struct Push { public int Rusher, Blocker; public float VX, VZ; }

        // Not hashed: rebuilt every tick (the shoves themselves move players, whose positions are hashed).
        public sealed class State
        {
            public readonly List<Push> Pushes = new List<Push>();
        }

        public static void Step(World w, IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            var s = w.Rush;
            s.Pushes.Clear();
            if (!On(w) || !LivePlay.On(w) || w.Phase != Phase.Live || inputs == null) return;
            var c = w.Cfg.driveRush;
            float dt = w.Dt;
            var ball = LivePlay.Carrier(w);
            float bx = ball != null ? ball.X : w.Ball.X, bz = ball != null ? ball.Z : w.Ball.Z;
            foreach (var d in w.Players)
            {
                if (!d.Human || d.BotHelp || d.Blocking || d == ball || d.BlockedTicks <= 0 || d.FreeTicks > 0) continue;
                if (!inputs.TryGetValue(d.Slot, out var input)) continue;
                float ux = input.DriveX, uz = input.DriveZ;
                float m = (float)Math.Sqrt(ux * ux + uz * uz);
                if (m < c.deadZone) continue;
                ux /= m; uz /= m;
                if (m > 1f) m = 1f;
                var o = Blocker(w, d);
                if (o == null) continue;

                float push = c.pushYdS * m * Strength(w, d, o) * (d.Tired ? c.tiredFrac : 1f);
                float against = Against(o, bx, bz, ux, uz);
                push *= 1f - c.fightFrac * against;
                if (against < c.shedAcrossBelow) d.BlockedTicks++;   // a rip across him: the shed clock runs double

                float vx = ux * push, vz = uz * push;
                o.X += vx * dt; o.Z += vz * dt;
                d.X += vx * dt; d.Z += vz * dt;
                d.Stamina -= c.staminaPerS * dt;
                if (d.Stamina <= 0f) { d.Stamina = 0f; d.Tired = true; d.Sprinting = false; }
                s.Pushes.Add(new Push { Rusher = d.Slot, Blocker = o.Slot, VX = vx, VZ = vz });
            }
        }

        // The blocker who has this rusher held up right now: the nearest opponent in block reach who is blocking.
        public static Player Blocker(World w, Player d)
        {
            float reach = w.Cfg.bots.blockReachYd, best = reach * reach;
            Player man = null;
            foreach (var o in w.Players)
            {
                if (o.Team == d.Team || !o.Blocking) continue;
                float dx = o.X - d.X, dz = o.Z - d.Z, d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                best = d2; man = o;
            }
            return man;
        }

        // The shove's strength for this pair, from their sizes (1 with FEATURES.ratings off).
        public static float Strength(World w, Player rusher, Player blocker)
        {
            var c = w.Cfg.driveRush;
            float f = 1f + c.strengthPerPoint * Ratings.SizeEdge(w, rusher, blocker);
            return Math.Max(c.minFrac, Math.Min(c.maxFrac, f));
        }

        // How hard the blocker's lean fights a shove along (ux, uz): he leans away from the ball at (bx, bz), so
        // 1 = the shove goes straight at the ball, 0 = across him or away from it.
        public static float Against(Player o, float bx, float bz, float ux, float uz)
        {
            float lx = o.X - bx, lz = o.Z - bz;
            float l = (float)Math.Sqrt(lx * lx + lz * lz);
            if (l <= 0f) return 0f;
            return Math.Max(0f, -(lx * ux + lz * uz) / l);
        }
    }

    // game.json "driveRush" (FEATURES.driveRush). Shares are 0..1; pushYdS in yards per second.
    public sealed class DriveRushConfig
    {
        public float deadZone;                         // the stick does nothing nearer the middle than this (0..1)
        public float pushYdS;                          // a full shove, even sizes, straight across the blocker
        public float strengthPerPoint, minFrac, maxFrac;   // size: per point bigger than the blocker, and the limits
        public float fightFrac, shedAcrossBelow;       // leverage: the most the blocker's lean takes off, and how far across counts as a rip
        public float staminaPerS, tiredFrac;           // driving's cost, and a tired man's shove
    }
}
