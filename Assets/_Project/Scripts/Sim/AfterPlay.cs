using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    public enum AfterMood { None, Huff, HighFive, WalkBack, Wander, Linger }

    // FEATURES.afterPlay (NO_AFTER_PLAY=1): after the whistle nobody snaps back into formation. Each man pulls up after a
    // beat of his own (reactMinS..reactMaxS), then does one thing, drawn by weight (afterPlay.weights, in AfterMood
    // order from Huff):
    //   Huff      stands bent over, hands on his knees, getting his breath back (likelier the more tired he is), then walks back
    //   HighFive  walks to the nearest teammate within fiveYd and slaps hands with him, then walks back
    //   WalkBack  walks straight back to his side of the next line
    //   Wander    ambles off a few yards somewhere, stands there a moment, then walks back
    //   Linger    stays where the play left him for a while (looking about on the TV), then walks back
    // Every man walks at his own pace (walkFracMin..walkFracMax of a jog). The whistle carries over into the huddle:
    // Huddle.Begin still works out the new formation spots, but everyone keeps standing where he was and walks to his
    // new spot during the huddle (all of them by homeByS into it, at hurryFrac of a jog or more); the line-up at the
    // end of the huddle puts anyone still on his way on his spot, as before. The ball carrier at the whistle (and on a
    // tackle or a sack the man who brought him down) stays where he went down until the whistle is over. People are
    // walked like bots unless they push their own stick.
    // The draws come from the game's seed, the play number and the slot, not from World.Rng, so turning this on only
    // moves the men between plays and leaves the plays themselves to the rest of the sim. The TV reads MoodOf for the
    // poses (View/AfterPlayView.cs). Not hashed: it is drawn again from the World at every whistle.
    public static class AfterPlay
    {
        public sealed class Rec
        {
            public AfterMood Mood;
            public int Start;            // the tick he stops standing about and starts on it
            public int Hold;             // ticks: huffing, lingering, standing where he wandered to
            public int Partner = -1;     // HighFive: the man he goes to
            public int Fived = -1;       // the tick the hands met (-1: not yet)
            public int Arrived = -1;     // Wander: the tick he got there
            public float Walk;           // his pace, a share of a jog
            public float WanderX, WanderZ, BackX, BackZ;
            public bool Down, Driven;
            public float SaveX, SaveZ, SaveVX, SaveVZ;
        }

        public sealed class State
        {
            public int Play = -1;        // World.PlaysRun at the whistle these are for (-1: none yet)
            public bool Active;          // between that whistle and the next line-up
            public readonly SortedDictionary<int, Rec> BySlot = new SortedDictionary<int, Rec>();
        }

        public static bool On(World w) => w.Cfg.Feature("afterPlay");

        static int Ticks(World w, float s) => (int)Math.Round(s * w.Cfg.sim.tickHz);

        // Once a tick, before anyone moves: a fresh whistle draws everyone's mood; the line-up ends it.
        public static void Step(World w)
        {
            var s = w.After;
            if (!On(w)) { s.Active = false; return; }
            if (w.Phase == Phase.Set || w.Phase == Phase.Live) { s.Active = false; return; }
            if (w.Phase == Phase.Whistle && s.Play != w.PlaysRun && w.PlaysRun > 0) Draw(w);
        }

        // What a man is doing after the play, for the TV (None: nothing, or it's over).
        public static AfterMood MoodOf(World w, int slot, out Rec rec)
        {
            rec = null;
            if (!On(w) || !w.After.Active || !w.After.BySlot.TryGetValue(slot, out rec)) return AfterMood.None;
            return rec.Mood;
        }

        // True while AfterPlay walks this man (a bot, or a person not pushing his stick).
        public static bool Drives(World w, Player p, PlayerInput input)
        {
            if (!On(w) || !w.After.Active || (w.Phase != Phase.Whistle && w.Phase != Phase.Huddle)) return false;
            if (!w.After.BySlot.TryGetValue(p.Slot, out var r)) return false;
            r.Driven = !p.Human || (input.MoveX == 0f && input.MoveZ == 0f);
            return r.Driven;
        }

        // FEATURES.stayOnside leaves a man alone while he walks back in the huddle (the line-up puts him onside anyway).
        public static bool WalkingBack(World w, Player p) =>
            On(w) && w.After.Active && w.Phase == Phase.Huddle && w.After.BySlot.TryGetValue(p.Slot, out var r) && r.Driven;

        // Huddle.Begin lines everyone up: keep them where they stood instead (their new spots are their HomeX / HomeZ).
        public static bool Carries(World w) => On(w) && w.After.Active && w.Phase == Phase.Whistle;

        public static void Save(World w)
        {
            foreach (var p in w.Players)
                if (w.After.BySlot.TryGetValue(p.Slot, out var r)) { r.SaveX = p.X; r.SaveZ = p.Z; r.SaveVX = p.VX; r.SaveVZ = p.VZ; }
        }

        public static void Restore(World w)
        {
            foreach (var p in w.Players)
            {
                if (!w.After.BySlot.TryGetValue(p.Slot, out var r)) continue;
                p.X = r.SaveX; p.Z = r.SaveZ; p.VX = r.SaveVX; p.VZ = r.SaveVZ;
                p.GoalX = p.HomeX; p.GoalZ = p.HomeZ;
            }
        }

        // ---- the draw ----

        static void Draw(World w)
        {
            var s = w.After;
            var c = w.Cfg.afterPlay;
            s.Play = w.PlaysRun;
            s.Active = true;
            s.BySlot.Clear();
            int whistleEnd = w.Tick + w.PhaseTicks;
            var carrier = LivePlay.Carrier(w);
            Player tackler = null;
            if (carrier != null && (w.Result == "TACKLED" || w.Result == "SACKED")) tackler = Nearest(w, carrier, false, null);
            // the next play's way: a turnover hands the ball to the other team, who attack the other way
            int nextOffense = w.PendingTurnover ? w.Defense : w.Offense;
            float nextDir = w.PendingTurnover ? -w.Dir : w.Dir;
            var f = w.Cfg.field;
            foreach (var p in w.Players)
            {
                var r = new Rec();
                r.Start = w.Tick + Ticks(w, Lerp(c.reactMinS, c.reactMaxS, Roll(w, p.Slot, Salt.React)));
                r.Down = p == carrier || p == tackler;
                if (r.Down) r.Start = Math.Max(r.Start, whistleEnd);
                r.Walk = Lerp(c.walkFracMin, c.walkFracMax, Roll(w, p.Slot, Salt.Walk));
                float own = p.Team == nextOffense ? -nextDir : nextDir;
                r.BackX = w.NextLos + own * Math.Abs(p.HomeX - w.LineOfScrimmageX);
                r.BackZ = p.HomeZ;
                r.Mood = Pick(w, p, c);
                if (r.Down && r.Mood == AfterMood.HighFive) r.Mood = AfterMood.Huff;   // (getting up off the grass: no high five)
                if (r.Mood == AfterMood.Huff) r.Hold = Ticks(w, Lerp(c.huffMinS, c.huffMaxS, Roll(w, p.Slot, Salt.Hold)));
                if (r.Mood == AfterMood.Linger) r.Hold = Ticks(w, Lerp(c.lingerMinS, c.lingerMaxS, Roll(w, p.Slot, Salt.Hold)));
                if (r.Mood == AfterMood.Wander)
                {
                    r.Hold = Ticks(w, Lerp(c.lingerMinS, c.lingerMaxS, Roll(w, p.Slot, Salt.Hold)));
                    float dx = Roll(w, p.Slot, Salt.WanderX) - 0.5f, dz = Roll(w, p.Slot, Salt.WanderZ) - 0.5f;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d <= 0f) { dx = 1f; dz = 0f; d = 1f; }
                    float far = c.wanderYd * Lerp(0.5f, 1f, Roll(w, p.Slot, Salt.Hold));
                    r.WanderX = Clamp(p.X + dx / d * far, -f.HalfLength, f.HalfLength);
                    r.WanderZ = Clamp(p.Z + dz / d * far, -f.HalfWidth, f.HalfWidth);
                }
                s.BySlot[p.Slot] = r;
            }
            // pair up the high fives: each one with the nearest teammate still free (by slot, so the pairs never depend on order)
            foreach (var p in w.Players)
            {
                var r = s.BySlot[p.Slot];
                if (r.Mood != AfterMood.HighFive || r.Partner >= 0) continue;
                var mate = Nearest(w, p, true, q => { var qr = s.BySlot[q.Slot]; return !qr.Down && qr.Partner < 0 && qr.Mood != AfterMood.Huff; });
                if (mate == null || LivePlay.Dist(p, mate) > c.fiveYd) { r.Mood = AfterMood.WalkBack; continue; }
                var mr = s.BySlot[mate.Slot];
                mr.Mood = AfterMood.HighFive;
                mr.Partner = p.Slot; r.Partner = mate.Slot;
            }
        }

        static AfterMood Pick(World w, Player p, AfterPlayConfig c)
        {
            var wt = c.weights;
            float total = 0f;
            for (int i = 0; i < wt.Length; i++) total += Weight(p, c, i);
            if (total <= 0f) return AfterMood.WalkBack;
            float at = Roll(w, p.Slot, Salt.Mood) * total;
            for (int i = 0; i < wt.Length; i++)
            {
                at -= Weight(p, c, i);
                if (at < 0f) return (AfterMood)(i + 1);
            }
            return (AfterMood)wt.Length;
        }

        // A tired man is likelier to stand there blowing.
        static float Weight(Player p, AfterPlayConfig c, int i) =>
            Math.Max(0f, c.weights[i]) + (i == 0 ? c.tiredHuffBoost * (1f - p.Stamina) : 0f);

        static Player Nearest(World w, Player p, bool mates, Func<Player, bool> ok)
        {
            Player best = null; float bd = float.MaxValue;
            foreach (var q in w.Players)
            {
                if (q == p || (q.Team == p.Team) != mates || (ok != null && !ok(q))) continue;
                float d = LivePlay.Dist(p, q);
                if (d < bd) { best = q; bd = d; }
            }
            return best;
        }

        // ---- the walk ----

        public static PlayerInput Think(World w, Player p)
        {
            var c = w.Cfg.afterPlay;
            var r = w.After.BySlot[p.Slot];
            if (w.Tick < r.Start) return default;   // (pulling up: Mover eases him to a stop)
            bool late = w.Phase == Phase.Huddle && Huddle.Ticks(w, w.Cfg.huddle.seconds) - w.PhaseTicks >= Ticks(w, c.homeByS);
            if (!late)
                switch (r.Mood)
                {
                    case AfterMood.Huff:
                    case AfterMood.Linger:
                        if (w.Tick < r.Start + r.Hold) return default;
                        break;
                    case AfterMood.HighFive:
                        if (r.Fived < 0)
                        {
                            var mate = w.Find(r.Partner);
                            if (mate == null) { r.Fived = w.Tick; break; }
                            if (LivePlay.Dist(p, mate) <= c.fiveReachYd)
                            {
                                r.Fived = w.Tick;
                                if (w.After.BySlot.TryGetValue(mate.Slot, out var mr) && mr.Fived < 0) mr.Fived = w.Tick;
                                return default;
                            }
                            return Toward(p, r.Walk, mate.X, mate.Z, c.fiveReachYd * 0.5f);
                        }
                        if (w.Tick < r.Fived + Ticks(w, c.fiveHoldS)) return default;
                        break;
                    case AfterMood.Wander:
                        if (r.Arrived < 0)
                        {
                            var go = Toward(p, r.Walk, r.WanderX, r.WanderZ, c.arriveYd);
                            if (go.MoveX != 0f || go.MoveZ != 0f) return go;
                            r.Arrived = w.Tick;
                        }
                        if (w.Tick < r.Arrived + r.Hold) return default;
                        break;
                }
            // back to his side of the line: before the huddle, about where he'll stand; in the huddle, his new spot (late
            // in it, at a jog so he's there for the line-up)
            if (w.Phase != Phase.Huddle) return Toward(p, r.Walk, r.BackX, r.BackZ, c.arriveYd);
            return Toward(p, late ? Math.Max(r.Walk, c.hurryFrac) : r.Walk, p.HomeX, p.HomeZ, c.arriveYd);
        }

        static PlayerInput Toward(Player p, float speed, float x, float z, float arrive)
        {
            float dx = x - p.X, dz = z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= arrive) return default;
            return new PlayerInput { MoveX = dx / d * speed, MoveZ = dz / d * speed };
        }

        // ---- the draws: a hash of the game's seed, the play, the slot and what it is for (0 <= v < 1) ----

        enum Salt { React, Walk, Mood, Hold, WanderX, WanderZ }

        static float Roll(World w, int slot, Salt salt)
        {
            uint h = 2166136261u;   // hygiene:allow (FNV offset basis)
            void Mix(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 16777619u; }   // hygiene:allow (4 bytes; FNV prime)
                }
            }
            Mix(w.Seed); Mix(w.PlaysRun); Mix(slot); Mix((int)salt);
            unchecked { h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; }   // hygiene:allow (a final mix so close inputs land far apart)
            return (h >> 8) / (float)(1 << 24);   // hygiene:allow (24 bits of it)
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    public sealed class AfterPlayConfig
    {
        public float reactMinS, reactMaxS;            // the beat after the whistle before he starts on it
        public float walkFracMin, walkFracMax;        // his walking pace, a share of a jog
        public float[] weights = new float[0];        // Huff, HighFive, WalkBack, Wander, Linger
        public float tiredHuffBoost;                  // added to Huff's weight for an empty tank (times 1 - stamina)
        public float huffMinS, huffMaxS;
        public float lingerMinS, lingerMaxS;          // lingering, and standing where he wandered to
        public float wanderYd;
        public float fiveYd, fiveReachYd, fiveHoldS;  // how far he looks for a teammate, how close the hands meet, how long it lasts
        public float arriveYd;
        public float homeByS;                         // into the huddle: everyone is walking back by now...
        public float hurryFrac;                       // ...at least this share of a jog
        // the TV (View/AfterPlayView.cs)
        public int huffKeyFrames;                     // anim.fps frames between the breaths of a huff
        public float raiseYd;                         // a high five: the hand goes up this close to the man
        public int lookKeyFrames;                     // standing about: anim.fps frames between the head's turns
        public float lookDeg;                         // ...and how far it turns
    }
}
