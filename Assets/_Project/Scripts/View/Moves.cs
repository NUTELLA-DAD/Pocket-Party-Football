using System;
using System.Collections.Generic;
using PocketFootball.Sim;

namespace PocketFootball.View
{
    // What each player is doing on the TV beyond running and standing: the QB's arm cocked, his throw and handoff, a receiver's hands
    // up for a pass coming in, the catch tucked away, a drop, a juke, a missed tackle, the tackle itself (both men go
    // down and get up at the end of the whistle; FEATURES.hitAngle: a big hit drives them back, a carrier knocked off
    // balance staggers, a trip or a lost balance puts him on his front), a touchdown celebration, a blocker's shove. FEATURES.moves
    // (NO_MOVES=1: the plain run and stand poses only).
    // It only reads the World, once per sim tick, and spots the moments by what changed since the tick before (the ball
    // left the QB's hand, a pass ended up held, the whistle blew on a tackle). Frames count sim ticks at anim.fps, so
    // the moves step like the rest of the look and never depend on the frame rate. Pure C#: tests drive it with a World.
    public enum Move { None, Aim, Throw, Handoff, Reach, Secure, Bobble, Juke, Whiff, Tackle, Tackled, Celebrate, Block, Snap, TakeSnap, Stagger }

    public struct MoveNow
    {
        public Move Kind;
        public int Frame;         // key frame since the move began (anim.fps)
        public bool OnBack;       // Tackled: hit from the front, so down on his back (else on his front)
        public bool GettingUp;    // Tackle / Tackled: the last part of the whistle
        public int UpFrame;       // key frame since he started getting up
        public int StartTick;     // tells one move from the next of the same kind
        public bool HasPartner;   // Tackle / Tackled: the other man in the tackle is known
        public int Partner;       // ... and his slot (Block: the man he's engaged with)
        public bool Locked;       // Block (FEATURES.blockLock): arms locked with Partner
        public bool BigHit;       // Tackle / Tackled (FEATURES.hitAngle): a straight-on big hit
        public float KnockX, KnockZ;   // ... how far (yards) he still has to slide to where the hit put him down
    }

    public sealed class Moves
    {
        sealed class Rec { public Move Kind; public int Start, End; public bool Hold, OnBack, BigHit; public int Partner = -1; public float KnockX, KnockZ; }

        readonly SortedDictionary<int, Rec> bySlot = new SortedDictionary<int, Rec>();
        readonly HashSet<int> reaching = new HashSet<int>();
        readonly HashSet<int> watching = new HashSet<int>();
        readonly SortedDictionary<int, int> blockPartner = new SortedDictionary<int, int>();   // slot -> the man he's blocking / blocked by
        // a locked pair slides together (or apart) on the TV until their hands meet: slot -> offset in yards, this tick and the last
        readonly SortedDictionary<int, (float x, float z)> lockOff = new SortedDictionary<int, (float x, float z)>();
        readonly SortedDictionary<int, (float x, float z)> lockWas = new SortedDictionary<int, (float x, float z)>();
        readonly List<int> keys = new List<int>();
        readonly SortedDictionary<int, int> stumble = new SortedDictionary<int, int>();
        readonly SortedDictionary<int, int> juke = new SortedDictionary<int, int>();
        readonly SortedDictionary<int, int> offBalance = new SortedDictionary<int, int>();
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
            int was = lastTick;
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
                    if (p.OffBalanceTicks > 0 && (!offBalance.TryGetValue(p.Slot, out int o) || o == 0)) Start(w, p.Slot, Move.Stagger, p.OffBalanceTicks / (float)w.Cfg.sim.tickHz);
                }
                // the whistle: a tackle brings both men down; a touchdown, the carrier celebrates
                if (phase == Phase.Live && w.Phase == Phase.Whistle)
                {
                    var c = LivePlay.Carrier(w);
                    if (c != null && (w.Result == "TACKLED" || w.Result == "SACKED"))
                    {
                        // FEATURES.hitAngle: how this tackle landed (the sim's note of it, since the last tick seen)
                        var h = w.Hit;
                        var kind = h.Carrier == c.Slot && h.Tick > was && h.Tick <= w.Tick ? h.Last : HitKind.None;
                        var t = kind == HitKind.LostBalance ? null : Tackler(w, c);   // lost his balance: nobody is on him
                        bool onBack = false;
                        if (t != null)
                        {
                            // he was running into the tackler (or, standing, faced him): he goes over backwards
                            float fx = c.VX, fz = c.VZ;
                            if (fx * fx + fz * fz < 0.25f) { fx = w.Dir * (c.Team == w.Offense ? 1f : -1f); fz = 0f; }
                            onBack = kind != HitKind.Trip && (t.X - c.X) * fx + (t.Z - c.Z) * fz > 0f;   // a trip: on his front
                            Start(w, t.Slot, Move.Tackle, 0f, true, false, c.Slot);
                        }
                        Start(w, c.Slot, Move.Tackled, 0f, true, onBack, t != null ? t.Slot : -1);
                        if (kind == HitKind.BigHit)
                        {
                            // both men slide from where the hit landed to where the carrier went down
                            foreach (var r in t != null ? new[] { bySlot[c.Slot], bySlot[t.Slot] } : new[] { bySlot[c.Slot] })
                            {
                                r.BigHit = true; r.KnockX = h.FromX - h.ToX; r.KnockZ = h.FromZ - h.ToZ;
                            }
                        }
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
            juke.Clear(); stumble.Clear(); offBalance.Clear();
            foreach (var p in w.Players) { juke[p.Slot] = p.JukeTicks; stumble[p.Slot] = p.StumbleTicks; offBalance[p.Slot] = p.OffBalanceTicks; }
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
            Watch(w);
            Engage(w);
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
                var m = new MoveNow { Kind = r.Kind, Frame = FrameOf(w, w.Tick - r.Start), OnBack = r.OnBack, StartTick = r.Start, HasPartner = r.Partner >= 0, Partner = r.Partner, BigHit = r.BigHit };
                if (r.BigHit)
                {
                    // the slide back, in stepped keys: knockKeys of them, the first already part of the way
                    float done = Math.Min(1f, (m.Frame + 1) / (float)Math.Max(1, a.knockKeys));
                    m.KnockX = r.KnockX * (1f - done); m.KnockZ = r.KnockZ * (1f - done);
                }
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
            if (blockPartner.TryGetValue(p.Slot, out int q))
                return new MoveNow { Kind = Move.Block, Frame = (int)(w.PlayTicks / Math.Max(1, Ticks(w, a.blockShoveS))), Locked = w.Cfg.Feature("blockLock"), Partner = q };
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

        // FEATURES.watchBall: while a pass is in the air, the man of each team nearest where it's aimed (within watchYd)
        // and anyone with his hands up for it turn their shoulders and head to follow the ball (PlayersView).
        public bool Watching(int slot) => watching.Contains(slot);

        void Watch(World w)
        {
            watching.Clear();
            var b = w.Ball;
            if (!On(w) || !w.Cfg.Feature("watchBall") || b.State != BallState.Flying) return;
            foreach (int slot in reaching) watching.Add(slot);
            float r = w.Cfg.anim.watchYd;
            for (int team = 0; team < w.Cfg.teams.Count; team++)
            {
                Player best = null; float bd = r * r;
                foreach (var p in w.Players)
                {
                    if (p.Team != team || w.CatchTried.Contains(p.Slot) || p.Slot == b.Carrier) continue;
                    float dx = p.X - b.TargetX, dz = p.Z - b.TargetZ, d = dx * dx + dz * dz;
                    if (d < bd) { best = p; bd = d; }
                }
                if (best != null) watching.Add(best.Slot);
            }
        }

        // Who blocks whom this tick: a defender held up (Blocking.cs) with the nearest blocker in reach, a blocker with
        // the nearest man of the other team in reach. Two men who picked each other are a locked pair (FEATURES.blockLock):
        // on the TV they slide, at lockSlideYdS and by at most lockPullYd each, to lockGapYd apart, so their hands meet.
        void Engage(World w)
        {
            blockPartner.Clear();
            if (On(w) && w.Phase == Phase.Live)
            {
                float r = w.Cfg.bots.blockReachYd;
                foreach (var p in w.Players)
                {
                    bool held = p.BlockedTicks > 0;
                    if (!held && !p.Blocking) continue;
                    Player best = null; float bd = r * r;
                    foreach (var q in w.Players)
                    {
                        if (q.Team == p.Team || (held && !q.Blocking)) continue;
                        float dx = q.X - p.X, dz = q.Z - p.Z, d = dx * dx + dz * dz;
                        if (d <= bd) { best = q; bd = d; }
                    }
                    if (best != null) blockPartner[p.Slot] = best.Slot;
                }
            }
            var a = w.Cfg.anim;
            bool lockOn = On(w) && w.Cfg.Feature("blockLock");
            float step = a.lockSlideYdS / w.Cfg.sim.tickHz;
            lockWas.Clear();
            foreach (var kv in lockOff) lockWas[kv.Key] = kv.Value;
            keys.Clear();
            foreach (var kv in lockOff) keys.Add(kv.Key);
            foreach (var kv in blockPartner) if (!lockOff.ContainsKey(kv.Key)) keys.Add(kv.Key);
            foreach (int slot in keys)
            {
                float tx = 0f, tz = 0f;
                var p = w.Find(slot);
                if (p != null && lockOn && blockPartner.TryGetValue(slot, out int qs) && blockPartner.TryGetValue(qs, out int back) && back == slot)
                {
                    var q = w.Find(qs);
                    float dx = q.X - p.X, dz = q.Z - p.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d > 1e-3f)
                    {
                        float pull = Math.Max(-a.lockPullYd, Math.Min(a.lockPullYd, (d - a.lockGapYd) * 0.5f));
                        tx = dx / d * pull; tz = dz / d * pull;
                    }
                }
                lockOff.TryGetValue(slot, out var o);
                float ex = tx - o.x, ez = tz - o.z, e = (float)Math.Sqrt(ex * ex + ez * ez);
                if (e > step) { ex *= step / e; ez *= step / e; }
                o = (o.x + ex, o.z + ez);
                if (p == null || (tx == 0f && tz == 0f && Math.Abs(o.x) < 1e-4f && Math.Abs(o.z) < 1e-4f)) { lockOff.Remove(slot); continue; }
                lockOff[slot] = o;
            }
        }

        // Where the TV draws a locked man, off his sim spot (yards), blended between the last tick and this one.
        public void LockOffset(int slot, float alpha, out float x, out float z)
        {
            lockOff.TryGetValue(slot, out var now);
            if (!lockWas.TryGetValue(slot, out var was)) was = now;
            x = was.x + (now.x - was.x) * alpha;
            z = was.z + (now.z - was.z) * alpha;
        }
    }
}
