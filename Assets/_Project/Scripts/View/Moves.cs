using System;
using System.Collections.Generic;
using PocketFootball.Sim;

namespace PocketFootball.View
{
    // What each player is doing on the TV beyond running and standing: the QB's arm cocked, his throw and handoff, a receiver's hands
    // up for a pass coming in, the catch tucked away, a drop, a juke, a missed tackle, the tackle itself (both men go
    // down and get up at the end of the whistle), a touchdown celebration, a blocker's shove. FEATURES.moves
    // (NO_MOVES=1: the plain run and stand poses only).
    // It only reads the World, once per sim tick, and spots the moments by what changed since the tick before (the ball
    // left the QB's hand, a pass ended up held, the whistle blew on a tackle). Frames count sim ticks at anim.fps, so
    // the moves step like the rest of the look and never depend on the frame rate. Pure C#: tests drive it with a World.
    public enum Move { None, Aim, Throw, Handoff, Reach, Secure, Bobble, Juke, Whiff, Tackle, Tackled, Celebrate, Block, Snap, TakeSnap }

    public struct MoveNow
    {
        public Move Kind;
        public int Frame;         // key frame since the move began (anim.fps)
        public bool OnBack;       // Tackled: hit from the front, so down on his back (else on his front)
        public bool GettingUp;    // Tackle / Tackled: the last part of the whistle
        public int UpFrame;       // key frame since he started getting up
        public int StartTick;     // tells one move from the next of the same kind
        public bool HasPartner;   // Tackle / Tackled: the other man in the tackle is known
        public int Partner;       // ... and his slot
    }

    public sealed class Moves
    {
        sealed class Rec { public Move Kind; public int Start, End; public bool Hold, OnBack; public int Partner = -1; }

        readonly SortedDictionary<int, Rec> bySlot = new SortedDictionary<int, Rec>();
        readonly HashSet<int> reaching = new HashSet<int>();
        readonly SortedDictionary<int, int> stumble = new SortedDictionary<int, int>();
        readonly SortedDictionary<int, int> juke = new SortedDictionary<int, int>();
        readonly HashSet<int> tried = new HashSet<int>();
        readonly List<int> done = new List<int>();
        int lastTick = -1;
        Phase phase;
        BallState ballState;
        int carrier = -1;

        public bool On(World w) => w.Cfg.Feature("moves");

        int Ticks(World w, float s) => (int)Math.Round(s * w.Cfg.sim.tickHz);

        int FrameOf(World w, int ticks) => ticks <= 0 ? 0 : (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz);

        void Start(World w, int slot, Move kind, float seconds, bool hold = false, bool onBack = false, int partner = -1)
        {
            bySlot[slot] = new Rec { Kind = kind, Start = w.Tick, End = hold ? int.MaxValue : w.Tick + Math.Max(1, Ticks(w, seconds)), Hold = hold, OnBack = onBack, Partner = partner };
        }

        // Call once the World has stepped (calling again on the same tick does nothing).
        public void Observe(World w)
        {
            if (w.Tick == lastTick) return;
            bool first = lastTick < 0;
            lastTick = w.Tick;
            var a = w.Cfg.anim;
            var b = w.Ball;
            if (!first && On(w))
            {
                // the ball: thrown, handed off, caught
                if (ballState == BallState.Held && carrier >= 0 && b.State == BallState.Flying) Start(w, carrier, Move.Throw, a.throwS);
                if (ballState == BallState.Held && b.State == BallState.Held && carrier >= 0 && b.Carrier >= 0 && b.Carrier != carrier)
                {
                    var from = w.Find(carrier); var to = w.Find(b.Carrier);
                    if (from != null && to != null && from.Team == to.Team) Start(w, carrier, Move.Handoff, a.handoffS);
                }
                if (ballState == BallState.Flying && b.State == BallState.Held && b.Carrier >= 0) Start(w, b.Carrier, Move.Secure, a.secureS);
                // a try at the catch that didn't stick
                foreach (int slot in w.CatchTried)
                    if (!tried.Contains(slot) && !(b.State == BallState.Held && b.Carrier == slot)) Start(w, slot, Move.Bobble, a.bobbleS);
                // jukes and missed tackles: the sim's own timers
                foreach (var p in w.Players)
                {
                    if (p.JukeTicks > 0 && (!juke.TryGetValue(p.Slot, out int j) || j == 0)) Start(w, p.Slot, Move.Juke, p.JukeTicks / (float)w.Cfg.sim.tickHz);
                    if (p.StumbleTicks > 0 && (!stumble.TryGetValue(p.Slot, out int s) || s == 0)) Start(w, p.Slot, Move.Whiff, p.StumbleTicks / (float)w.Cfg.sim.tickHz);
                }
                // the whistle: a tackle brings both men down; a touchdown, the carrier celebrates
                if (phase == Phase.Live && w.Phase == Phase.Whistle)
                {
                    var c = LivePlay.Carrier(w);
                    if (c != null && (w.Result == "TACKLED" || w.Result == "SACKED"))
                    {
                        var t = Tackler(w, c);
                        bool onBack = false;
                        if (t != null)
                        {
                            // he was running into the tackler (or, standing, faced him): he goes over backwards
                            float fx = c.VX, fz = c.VZ;
                            if (fx * fx + fz * fz < 0.25f) { fx = w.Dir * (c.Team == w.Offense ? 1f : -1f); fz = 0f; }
                            onBack = (t.X - c.X) * fx + (t.Z - c.Z) * fz > 0f;
                            Start(w, t.Slot, Move.Tackle, 0f, true, false, c.Slot);
                        }
                        Start(w, c.Slot, Move.Tackled, 0f, true, onBack, t != null ? t.Slot : -1);
                    }
                    else if (c != null && (w.Result == "TOUCHDOWN" || w.Result == "PICK SIX")) Start(w, c.Slot, Move.Celebrate, 0f, true);
                }
                // a new huddle: everyone back on their feet
                if (phase == Phase.Whistle && w.Phase != Phase.Whistle) bySlot.Clear();
            }
            // remember this tick for the next
            phase = w.Phase;
            ballState = b.State;
            carrier = b.State == BallState.Held ? b.Carrier : -1;
            tried.Clear();
            foreach (int slot in w.CatchTried) tried.Add(slot);
            juke.Clear(); stumble.Clear();
            foreach (var p in w.Players) { juke[p.Slot] = p.JukeTicks; stumble[p.Slot] = p.StumbleTicks; }
            // hands up: the man of each team who will be nearest the ball as it comes down within catching height,
            // looking reachS ahead (the ball on its flight, each man at his speed)
            reaching.Clear();
            if (On(w) && b.State == BallState.Flying)
            {
                int ahead = Math.Max(1, Ticks(w, a.reachS));
                float dt = w.Dt, g = w.Cfg.ball.gravityYdS2, top = w.Cfg.play.catchMaxHeightYd;
                for (int team = 0; team < w.Cfg.teams.Count; team++)
                {
                    Player best = null; float bd = a.reachYd;
                    foreach (var p in w.Players)
                    {
                        if (p.Team != team || w.CatchTried.Contains(p.Slot)) continue;
                        for (int k = 0; k <= ahead; k++)
                        {
                            float t = k * dt, y = b.Y + b.VY * t - g * t * t * 0.5f;
                            if (y < 0f) break;
                            if (y > top || (b.VY - g * t) > 0f) continue;   // still rising, or over his head
                            float dx = p.X + p.VX * t - (b.X + b.VX * t), dz = p.Z + p.VZ * t - (b.Z + b.VZ * t);
                            float d = (float)Math.Sqrt(dx * dx + dz * dz);
                            if (d <= bd) { best = p; bd = d; }
                        }
                    }
                    if (best != null) reaching.Add(best.Slot);
                }
            }
            // finished moves go
            done.Clear();
            foreach (var kv in bySlot) if (!kv.Value.Hold && w.Tick >= kv.Value.End) done.Add(kv.Key);
            foreach (int slot in done) bySlot.Remove(slot);
        }

        // The tackler: the man the sim says made it (this tick's Tackle or Sack on him), else the nearest man on the
        // other team. (A person's tackle reaches further than a bot's, so the nearest man is not always the one.)
        public static Player Tackler(World w, Player c)
        {
            foreach (var e in w.Events.ThisTick)
                if ((e.Kind == EventKind.Tackle || e.Kind == EventKind.Sack) && e.Other == c.Slot)
                {
                    var t = w.Find(e.Slot);
                    if (t != null) return t;
                }
            Player best = null; float bd = float.MaxValue;
            foreach (var p in w.Players)
            {
                if (p.Team == c.Team) continue;
                float d = LivePlay.Dist(p, c);
                if (d < bd) { best = p; bd = d; }
            }
            return best;
        }

        public MoveNow Of(World w, Player p)
        {
            if (!On(w)) return default;
            var a = w.Cfg.anim;
            if (bySlot.TryGetValue(p.Slot, out var r))
            {
                var m = new MoveNow { Kind = r.Kind, Frame = FrameOf(w, w.Tick - r.Start), OnBack = r.OnBack, StartTick = r.Start, HasPartner = r.Partner >= 0, Partner = r.Partner };
                int upTicks = Ticks(w, a.getUpS);
                if (r.Hold && w.Phase == Phase.Whistle && w.PhaseTicks <= upTicks && (r.Kind == Move.Tackle || r.Kind == Move.Tackled))
                {
                    m.GettingUp = true;
                    m.UpFrame = FrameOf(w, upTicks - w.PhaseTicks);
                }
                return m;
            }
            var snap = Snap.For(w, p);   // the snap (Snap.cs): the center down in his stance, the QB's hands out for it
            if (snap != Move.None) return new MoveNow { Kind = snap };
            if (reaching.Contains(p.Slot)) return new MoveNow { Kind = Move.Reach };
            if (w.Phase != Phase.Live) return default;
            // the QB with the ball in the pocket, not yet thrown: arm cocked
            var play = LivePlay.Called(w, w.Offense);
            if (p == LivePlay.Carrier(w) && p == LivePlay.Qb(w) && !w.Thrown && (play == null || play.runner < 0) && p.VX * p.VX + p.VZ * p.VZ <= 0.25f) return new MoveNow { Kind = Move.Aim };
            // a block: a defender held up, or a blocker with a man in front of him
            if (p.BlockedTicks > 0 || (p.Blocking && Engaged(w, p)))
                return new MoveNow { Kind = Move.Block, Frame = (int)(w.PlayTicks / Math.Max(1, Ticks(w, a.blockShoveS))) };
            return default;
        }

        // Where the tackler goes down so the two bodies meet (FEATURES.tackleContact, NO_TACKLE_CONTACT=1 turns it
        // off): the sim calls a tackle from reach (tackleReachYd, half again for a person), which can leave a yard or
        // more of grass between them. The tackler lands contactYd short of the carrier, on the line from where he
        // was, facing him (ux, uz: tackler to carrier); the carrier stays on his spot, where the ball is marked.
        // False: they stand on the same spot, no line to put him on.
        public static bool TackleContact(float tx, float tz, float cx, float cz, float contactYd, out float x, out float z, out float ux, out float uz)
        {
            float dx = cx - tx, dz = cz - tz, d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 0.01f) { x = tx; z = tz; ux = uz = 0f; return false; }
            ux = dx / d; uz = dz / d;
            x = cx - ux * contactYd; z = cz - uz * contactYd;
            return true;
        }

        static bool Engaged(World w, Player p)
        {
            float r = w.Cfg.bots.blockReachYd;
            foreach (var q in w.Players)
            {
                if (q.Team == p.Team) continue;
                float dx = q.X - p.X, dz = q.Z - p.Z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }
    }
}
