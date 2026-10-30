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
    public enum Move { None, Aim, Throw, Handoff, Reach, Secure, Bobble, Juke, Whiff, Tackle, Tackled, Celebrate, Block, Snap, TakeSnap, Stagger, KickSet, Kick, Hold, Leap, DiveCatch, Tangle }

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
        public bool Full;         // Aim / Throw (FEATURES.throwMotion): wound up, the four-key throw
        public bool BigHit;       // Tackle / Tackled (FEATURES.hitAngle): a straight-on big hit
        public float KnockX, KnockZ;   // ... how far (yards) he still has to slide to where the hit put him down
        public int Hand;          // Reach (FEATURES.catchPlay): 0 both hands up, 1 his left hand only, 2 his right (a ball out to his side)
        public bool Won;          // Tangle: he came down with the ball
    }

    public sealed class Moves
    {
        sealed class Rec { public Move Kind; public int Start, End; public bool Hold, OnBack, BigHit, Won; public int Partner = -1; public float KnockX, KnockZ; }

        readonly SortedDictionary<int, Rec> bySlot = new SortedDictionary<int, Rec>();
        readonly HashSet<int> reaching = new HashSet<int>();
        readonly SortedDictionary<int, int> reachHand = new SortedDictionary<int, int>();   // FEATURES.catchPlay: the hand each reacher picked as he put it up
        readonly SortedDictionary<int, int> leapAt = new SortedDictionary<int, int>();      // FEATURES.catchPlay: slot -> the tick he left the grass
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
                if (ballState == BallState.Held && carrier >= 0 && b.State == BallState.Flying)
                {
                    // FEATURES.kickMotion: a kick is the kicker's leg, not a throw
                    // (FEATURES.fgHolder: the holder had it; the kicker's leg, the holder stays down)
                    var kicker = Kicking.Kicker(w);
                    var holder = Kicking.Holder(w);
                    bool held = holder != null && holder.Slot == carrier;
                    if (w.Kick.Kicked && kicker != null && (kicker.Slot == carrier || held) && w.Cfg.Feature("kickMotion")) Start(w, kicker.Slot, Move.Kick, a.kickS);
                    else if (!held) Start(w, carrier, Move.Throw, a.throwS);
                }
                // (not the field goal's line-up, where the holder takes it from the QB's spot: FEATURES.fgHolder)
                if (ballState == BallState.Held && b.State == BallState.Held && carrier >= 0 && b.Carrier >= 0 && b.Carrier != carrier && w.Phase != Phase.Set)
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
                // FEATURES.catchPlay: a man laying out for the ball, or two men up for it coming down tangled
                var cn = w.CatchNote;
                if (CatchOn(w) && cn.Tick >= was && cn.Tick < w.Tick)   // (stamped in World.Step before it moves Tick on)
                {
                    var c0 = w.Find(cn.Slot);
                    float downS = c0 != null ? c0.StumbleTicks / (float)w.Cfg.sim.tickHz : 0f;
                    if (cn.Style == CatchStyle.Dive && c0 != null) Start(w, cn.Slot, Move.DiveCatch, downS);
                    if (cn.Style == CatchStyle.Contested && c0 != null && w.Find(cn.Rival) is Player r0)
                    {
                        // the catcher lands with it (on a knee) or goes down on his face; the other man over backwards
                        Start(w, cn.Slot, Move.Tangle, downS, false, false, cn.Rival);
                        bySlot[cn.Slot].Won = cn.Caught;
                        Start(w, cn.Rival, Move.Tangle, r0.StumbleTicks / (float)w.Cfg.sim.tickHz, false, true, cn.Slot);
                    }
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
            Leaps(w);
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
                var m = new MoveNow { Kind = r.Kind, Frame = FrameOf(w, w.Tick - r.Start), OnBack = r.OnBack, StartTick = r.Start, HasPartner = r.Partner >= 0, Partner = r.Partner, BigHit = r.BigHit, Won = r.Won, Full = w.Cfg.Feature("throwMotion") };
                if (r.BigHit)
                {
                    // the slide back, in stepped keys: knockKeys of them, the first already part of the way
                    float done = Math.Min(1f, (m.Frame + 1) / (float)Math.Max(1, a.knockKeys));
                    m.KnockX = r.KnockX * (1f - done); m.KnockZ = r.KnockZ * (1f - done);
                }
                int upTicks = Ticks(w, a.getUpS);
                // FEATURES.catchPlay: up off the grass over the last getUpS of a dive or a tangle
                if ((r.Kind == Move.DiveCatch || r.Kind == Move.Tangle) && r.End - w.Tick <= upTicks)
                {
                    m.GettingUp = true;
                    m.UpFrame = FrameOf(w, upTicks - (r.End - w.Tick));
                }
                if (r.Hold && w.Phase == Phase.Whistle && w.PhaseTicks <= upTicks && (r.Kind == Move.Tackle || r.Kind == Move.Tackled))
                {
                    m.GettingUp = true;
                    m.UpFrame = FrameOf(w, upTicks - w.PhaseTicks);
                }
                return m;
            }
            // FEATURES.kickMotion: the kicker over the ball (Frame 1: a punt, the ball held out)
            if (w.Cfg.Feature("kickMotion") && Kicking.WaitingToKick(w, p) && !w.Kick.Approach) return new MoveNow { Kind = Move.KickSet, Frame = Kicking.Mode(w) == KickKind.Punt ? 1 : 0 };
            // FEATURES.fgHolder: the holder is down on one knee from the line-up until the ball is gone
            if (p == Kicking.Holder(w) && !w.Kick.Kicked) return new MoveNow { Kind = Move.Hold };
            var snap = Snap.For(w, p);   // the snap (Snap.cs): the center down in his stance, the QB's hands out for it
            if (snap != Move.None) return new MoveNow { Kind = snap };
            if (reaching.Contains(p.Slot)) return new MoveNow { Kind = Move.Reach, Hand = reachHand.TryGetValue(p.Slot, out int hand) ? hand : 0 };
            if (w.Phase != Phase.Live) return default;
            // the QB with the ball in the pocket, not yet thrown: arm cocked (FEATURES.qbStrafe: on the move too, his legs
            // striding under it, until he runs past the line)
            var play = LivePlay.Called(w, w.Offense);
            if (p == LivePlay.Carrier(w) && p == LivePlay.Qb(w) && !w.Thrown && (play == null || play.runner < 0) && (p.VX * p.VX + p.VZ * p.VZ <= 0.25f || (w.Cfg.Feature("qbStrafe") && Facing.InPocket(w, p)))) return new MoveNow { Kind = Move.Aim, Full = w.Cfg.Feature("throwMotion") };
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

        public static bool CatchOn(World w) => w.Cfg.Feature("catchPlay");

        // FEATURES.catchPlay: the hand each man reaching for the ball puts up, picked as he starts to reach: one hand
        // (the one on the ball's side) for a ball more than oneHandYd out to his side of his run, else both; and a jump
        // ball: the two men reaching for it from both teams, within catchPlay.contestYd of each other, both leave the
        // grass as it comes down (leapS before it is at catching height, the top of the jump as it gets there).
        void Leaps(World w)
        {
            var b = w.Ball;
            var a = w.Cfg.anim;
            keys.Clear();
            foreach (var kv in reachHand) if (!reaching.Contains(kv.Key)) keys.Add(kv.Key);
            foreach (int slot in keys) reachHand.Remove(slot);
            keys.Clear();
            foreach (var kv in leapAt) if (w.Tick - kv.Value > Ticks(w, a.leapS) || w.Phase != Phase.Live) keys.Add(kv.Key);
            foreach (int slot in keys) leapAt.Remove(slot);
            if (!On(w) || !CatchOn(w) || b.State != BallState.Flying) return;
            foreach (int slot in reaching)
            {
                if (reachHand.ContainsKey(slot)) continue;
                var p = w.Find(slot);
                if (p == null) continue;
                float sp = (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
                int hand = 0;
                if (sp > 1f)
                {
                    float side = ((b.TargetZ - p.Z) * p.VX - (b.TargetX - p.X) * p.VZ) / sp;   // + on his left (facing +x, left is +z)
                    if (Math.Abs(side) > a.oneHandYd) hand = side > 0f ? 1 : 2;
                }
                reachHand[slot] = hand;
            }
            // a jump ball: one reacher a team, close together, the ball about to come down to them
            Player x = null, y = null;
            foreach (int slot in reaching) { var p = w.Find(slot); if (p == null) continue; if (x == null) x = p; else if (p.Team != x.Team) y = p; }
            if (x == null || y == null || LivePlay.Dist(x, y) > w.Cfg.catchPlay.contestYd + a.leapCloseYd) return;
            float g = w.Cfg.ball.gravityYdS2, drop = b.Y - w.Cfg.play.catchMaxHeightYd;
            if (b.VY > 0f && drop > 0f) return;
            float until = drop <= 0f ? 0f : (b.VY + (float)Math.Sqrt(b.VY * b.VY + 2f * g * drop)) / g;   // seconds until it is at catching height
            if (until > a.leapS * 0.5f) return;
            foreach (var p in new[] { x, y })
                if (!leapAt.ContainsKey(p.Slot) && !bySlot.ContainsKey(p.Slot)) { leapAt[p.Slot] = w.Tick; Start(w, p.Slot, Move.Leap, a.leapS); }
        }

        // FEATURES.catchPlay: how high (yd) a man up for a jump ball has his feet off the grass: a hop over anim.leapS from
        // when he left it, leapYd at the top, in stepped keys at anim.fps (no wobble); the hop carries on if the ball
        // lands in the middle of it and he comes down tangled. 0 for everyone else.
        public float LiftYd(World w, int slot)
        {
            if (!leapAt.TryGetValue(slot, out int at)) return 0f;
            int keys = FrameOf(w, Ticks(w, w.Cfg.anim.leapS)) + 1;
            float t = Math.Min(1f, (FrameOf(w, w.Tick - at) + 1f) / keys);
            return w.Cfg.anim.leapYd * 4f * t * (1f - t);
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
