using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PocketFootball.Sim
{
    public enum DefMove { None, Swim, BullRush, Swat, Jump, Dive }

    // Defender moves (WS-03), behind FEATURES.defmoves (NO_DEFMOVES=1: none of this happens). Numbers in game.json "defense".
    //  SWIM (the line, while a blocker holds him): slips the block and runs free for swimFreeS. Best pressed in the
    //    first swimEarlyS of the block (swimEarlyChance), after that swimLateChance.
    //  BULL RUSH (the line, while held): drives the blocker back bullPushYd and leaves him stumbling. Best at the snap
    //    (bullSnapChance in the first bullSnapS of the play), after that bullChance.
    //  SWAT (coverage, a pass in the air): arms up for swatActiveS. If the ball comes within swatReachYd and under
    //    swatMaxHeightYd, he knocks it to the grass (incomplete): swatPerfectChance when the ball got there within
    //    swatPerfectS of the press, else swatChance. A whiff or a ball that never came: off balance for coverMissS.
    //  JUMP (coverage): the same, higher (jumpMaxHeightYd); he may pick it off (jumpPick*Chance) or knock it down
    //    (jumpKnockChance), else he whiffs.
    //  DIVE TACKLE (anyone on defense, at the ball carrier): a lunge at diveSpeedYdS for diveAirS. Contact within
    //    diveReachYd tackles him at diveChance, less diveStretchPenalty at full stretch, plus diveFromBehindBonus. A
    //    juke, a miss or a dive that lands short (too late, he's gone): the diver is down on the grass for diveDownS.
    // Odds by position: lineSkill (swim, bull rush) and coverSkill (swat, jump) add to the chance for that position.
    // Bots use them too (the bot* numbers). People press them through Press() (the phone buttons come with the
    // landscape layout). Runs after the ball moves and before LivePlay.Step, so a swat beats the catch on the same tick.
    public static class DefenseMoves
    {
        public sealed class Defender
        {
            public DefMove Move;          // under way (swat / jump: arms up; dive: in the air)
            public int Ticks;             // ticks since it began
            public DefMove Pressed;       // a person's press, used on the next step
            public int Cooldown;          // no new move until 0
            public int DownTicks;         // a dive that missed: on the grass
            public int MissTicks;         // a swat or jump that whiffed: off balance
            public float DirX, DirZ, AtX, AtZ;   // the dive's line; where the diver is (in the air) or lies (down)
            public DefMove Shown;         // the last move, for the TV (shownS long)
            public int ShownTicks;        // ticks since Shown began
            public bool ShownWon;
            public bool BotLineTried, BotCoverTried, BotDiveTried;   // a bot's one try per block / pass / play
        }

        public sealed class State
        {
            public readonly SortedDictionary<int, Defender> BySlot = new SortedDictionary<int, Defender>();
            public readonly SortedDictionary<DefMove, int> Tried = new SortedDictionary<DefMove, int>();
            public readonly SortedDictionary<DefMove, int> Won = new SortedDictionary<DefMove, int>();
        }

        // Kept beside the World (not in it, so World.Hash and the baseline stay as they were); the players' positions,
        // which the moves change, are hashed as ever.
        static readonly ConditionalWeakTable<World, State> states = new ConditionalWeakTable<World, State>();

        public static bool On(World w) => w.Cfg.Feature("defmoves");

        public static State Of(World w) => states.GetValue(w, _ => new State());

        public static Defender Get(World w, int slot)
        {
            var s = Of(w);
            if (!s.BySlot.TryGetValue(slot, out var d)) { d = new Defender(); s.BySlot[slot] = d; }
            return d;
        }

        public static int Tried(World w, DefMove m) => Of(w).Tried.TryGetValue(m, out int n) ? n : 0;
        public static int Won(World w, DefMove m) => Of(w).Won.TryGetValue(m, out int n) ? n : 0;

        // A person pressed a move button. Used on the next step if it fits the moment (else ignored).
        public static bool Press(World w, int slot, DefMove move)
        {
            if (!On(w) || move == DefMove.None) return false;
            Get(w, slot).Pressed = move;
            return true;
        }

        public static bool IsDown(World w, Player p) => On(w) && Of(w).BySlot.TryGetValue(p.Slot, out var d) && d.DownTicks > 0;

        public static void Step(World w)
        {
            if (!On(w)) return;
            var s = Of(w);
            var c = w.Cfg.defense;
            if (w.Phase == Phase.Huddle || w.Phase == Phase.Set) { s.BySlot.Clear(); return; }
            foreach (var p in w.Players)
            {
                if (p.Team != w.Defense) continue;
                var d = Get(w, p.Slot);
                if (d.Shown != DefMove.None && ++d.ShownTicks >= Huddle.Ticks(w, c.shownS)) d.Shown = DefMove.None;
                if (d.Cooldown > 0) d.Cooldown--;
                if (d.MissTicks > 0) { d.MissTicks--; Slow(w, p, c.coverMissSpeedFrac); }
                if (d.DownTicks > 0)
                {
                    // on the grass: stays where he landed, can't tackle
                    d.DownTicks--;
                    p.X = d.AtX; p.Z = d.AtZ; p.VX = p.VZ = 0f;
                    p.TackleCooldown = d.DownTicks > 0 ? d.DownTicks + 1 : 0;
                    d.Pressed = DefMove.None;
                    continue;
                }
                if (w.Phase != Phase.Live)
                {
                    // the whistle went (someone else's tackle, a catch out of bounds...): a diver still in the air lands
                    if (d.Move == DefMove.Dive) GoDown(w, p, d);
                    d.Pressed = DefMove.None;
                    continue;
                }
                if (w.Ball.State != BallState.Flying) d.BotCoverTried = false;
                if (p.BlockedTicks == 0) d.BotLineTried = false;
                if (!p.Human || p.BotHelp) BotThink(w, p, d);
                if (d.Move == DefMove.None && d.Pressed != DefMove.None && d.Cooldown == 0) Begin(w, p, d, d.Pressed);
                d.Pressed = DefMove.None;
                if (d.Move != DefMove.None) Advance(w, p, d);
            }
        }

        static void Begin(World w, Player p, Defender d, DefMove m)
        {
            var c = w.Cfg.defense;
            var carrier = LivePlay.Carrier(w);
            switch (m)
            {
                case DefMove.Swim:
                case DefMove.BullRush:
                    if (p.BlockedTicks == 0) return;   // nobody holding him
                    Count(w, m, false);
                    d.Cooldown = Huddle.Ticks(w, c.cooldownS);
                    if (m == DefMove.Swim) Swim(w, p, d); else Bull(w, p, d);
                    return;
                case DefMove.Swat:
                case DefMove.Jump:
                    if (w.Ball.State != BallState.Flying) return;
                    Count(w, m, false);
                    d.Move = m; d.Ticks = 0;
                    Show(d, m, false);
                    return;
                case DefMove.Dive:
                    if (carrier == null || carrier.Team == p.Team) return;
                    Count(w, m, false);
                    // at where he'll be halfway through the lunge
                    float half = Huddle.Ticks(w, c.diveAirS) * w.Dt * 0.5f;
                    float dx = carrier.X + carrier.VX * half - p.X, dz = carrier.Z + carrier.VZ * half - p.Z;
                    float dl = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dl > 0f) { d.DirX = dx / dl; d.DirZ = dz / dl; } else { d.DirX = -w.Dir; d.DirZ = 0f; }
                    d.AtX = p.X; d.AtZ = p.Z;
                    d.Move = m; d.Ticks = 0;
                    Show(d, m, false);
                    return;
            }
        }

        // One tick of a move under way.
        static void Advance(World w, Player p, Defender d)
        {
            var c = w.Cfg.defense;
            if (d.Move == DefMove.Swat || d.Move == DefMove.Jump)
            {
                var b = w.Ball;
                if (b.State != BallState.Flying) { End(w, d); return; }   // caught or down already
                float dx = b.X - p.X, dz = b.Z - p.Z;
                float top = d.Move == DefMove.Jump ? c.jumpMaxHeightYd : c.swatMaxHeightYd;
                if (dx * dx + dz * dz <= c.swatReachYd * c.swatReachYd && b.Y <= top) { Cover(w, p, d, d.Ticks <= Huddle.Ticks(w, c.swatPerfectS)); return; }
                if (++d.Ticks > Huddle.Ticks(w, c.swatActiveS)) { d.MissTicks = Huddle.Ticks(w, c.coverMissS); End(w, d); }
                return;
            }
            if (d.Move == DefMove.Dive)
            {
                int air = Math.Max(1, Huddle.Ticks(w, c.diveAirS));
                d.Ticks++;
                d.AtX += d.DirX * c.diveSpeedYdS * w.Dt; d.AtZ += d.DirZ * c.diveSpeedYdS * w.Dt;
                p.X = d.AtX; p.Z = d.AtZ;
                p.VX = d.DirX * c.diveSpeedYdS; p.VZ = d.DirZ * c.diveSpeedYdS;
                p.TackleCooldown = air + 1;   // the dive is his tackle try: no other one while in the air
                var carrier = LivePlay.Carrier(w);
                if (carrier != null && carrier.Team != p.Team && LivePlay.Dist(p, carrier) <= c.diveReachYd)
                {
                    float odds = c.diveChance - c.diveStretchPenalty * (d.Ticks - 1) / Math.Max(1, air - 1);
                    if ((carrier.X - p.X) * carrier.VX + (carrier.Z - p.Z) * carrier.VZ > 0f) odds += c.diveFromBehindBonus;
                    if (carrier.JukeTicks == 0 && w.Rng.Value() < odds)
                    {
                        Count(w, DefMove.Dive, true);
                        d.ShownWon = true;
                        End(w, d);
                        bool sack = carrier == LivePlay.Qb(w) && !w.Thrown && (carrier.X - w.LineOfScrimmageX) * w.Dir < 0f;
                        w.Events.Push(w, sack ? EventKind.Sack : EventKind.Tackle, p, carrier);
                        LivePlay.Whistle(w, sack ? "SACKED" : "TACKLED", carrier.X);
                        return;
                    }
                    w.Events.Push(w, EventKind.MissedTackle, p, carrier);
                    GoDown(w, p, d);   // bounced off him, or juked
                    return;
                }
                if (d.Ticks >= air) GoDown(w, p, d);   // landed short: too late
            }
        }

        static void GoDown(World w, Player p, Defender d)
        {
            var c = w.Cfg.defense;
            End(w, d);
            d.DownTicks = Huddle.Ticks(w, c.diveDownS);
            p.VX = p.VZ = 0f;
            p.TackleCooldown = d.DownTicks + 1;
        }

        static void End(World w, Defender d)
        {
            d.Move = DefMove.None;
            d.Ticks = 0;
            d.Cooldown = Huddle.Ticks(w, w.Cfg.defense.cooldownS);
        }

        static void Swim(World w, Player p, Defender d)
        {
            var c = w.Cfg.defense;
            float odds = (p.BlockedTicks <= Huddle.Ticks(w, c.swimEarlyS) ? c.swimEarlyChance : c.swimLateChance) + Skill(c.lineSkill, p.Pos);
            bool won = w.Rng.Value() < odds;
            Show(d, DefMove.Swim, won);
            if (!won) return;
            Count(w, DefMove.Swim, true);
            p.BlockedTicks = 0;
            p.FreeTicks = Huddle.Ticks(w, c.swimFreeS);
        }

        static void Bull(World w, Player p, Defender d)
        {
            var c = w.Cfg.defense;
            var o = Blocker(w, p);
            float odds = (w.PlayTicks <= Huddle.Ticks(w, c.bullSnapS) ? c.bullSnapChance : c.bullChance) + Skill(c.lineSkill, p.Pos);
            bool won = o != null && w.Rng.Value() < odds;
            Show(d, DefMove.BullRush, won);
            if (!won) return;
            Count(w, DefMove.BullRush, true);
            // drive him back the way the rusher faces him
            float dx = o.X - p.X, dz = o.Z - p.Z, dl = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dl <= 0f) { dx = w.Dir; dz = 0f; dl = 1f; }
            o.X += dx / dl * c.bullPushYd; o.Z += dz / dl * c.bullPushYd;
            o.VX = o.VZ = 0f;
            o.StumbleTicks = Math.Max(o.StumbleTicks, Huddle.Ticks(w, c.bullStunS));
            p.BlockedTicks = 0;
            p.FreeTicks = Huddle.Ticks(w, c.bullFreeS);
        }

        // The blocker holding him: the nearest one blocking within reach.
        static Player Blocker(World w, Player p)
        {
            Player best = null; float bd = w.Cfg.bots.blockReachYd;
            foreach (var o in w.Players)
            {
                if (o.Team == p.Team || !o.Blocking) continue;
                float dist = LivePlay.Dist(o, p);
                if (dist <= bd) { bd = dist; best = o; }
            }
            return best;
        }

        // A swat or jump reached the ball.
        static void Cover(World w, Player p, Defender d, bool perfect)
        {
            var c = w.Cfg.defense;
            float skill = Skill(c.coverSkill, p.Pos);
            float r = w.Rng.Value();
            var m = d.Move;
            End(w, d);
            if (m == DefMove.Jump)
            {
                float pick = (perfect ? c.jumpPickPerfectChance : c.jumpPickChance) + skill;
                if (r < pick) { Won(w, p, d, m); PickOff(w, p); return; }
                if (r < pick + c.jumpKnockChance) { Won(w, p, d, m); KnockDown(w); return; }
            }
            else if (r < (perfect ? c.swatPerfectChance : c.swatChance) + skill) { Won(w, p, d, m); KnockDown(w); return; }
            d.MissTicks = Huddle.Ticks(w, c.coverMissS);
        }

        static void Won(World w, Player p, Defender d, DefMove m) { Count(w, m, true); Show(d, m, true); }

        // To the grass where it was hit, rolling on a little: LivePlay calls it incomplete.
        static void KnockDown(World w)
        {
            var b = w.Ball;
            float f = w.Cfg.defense.swatRollFrac;
            float vx = b.VX * f, vz = b.VZ * f;
            b.Drop();
            b.Y = 0f; b.VX = vx; b.VZ = vz;
        }

        static void PickOff(World w, Player p)
        {
            w.Ball.GiveTo(p, w.Cfg.ball);
            w.Events.Push(w, EventKind.Interception, p, LivePlay.Qb(w));
            if (w.Cfg.Feature("intreturn")) w.Intercepted = true;
            else LivePlay.Whistle(w, "INTERCEPTED", p.X);
        }

        // Bots: a line move once per block, a swat or jump once per pass when the ball coming down is about to reach them, a dive
        // once per play at a carrier coming at them just out of tackle reach.
        static void BotThink(World w, Player p, Defender d)
        {
            var c = w.Cfg.defense;
            if (d.Move != DefMove.None || d.Cooldown > 0) return;
            if (p.BlockedTicks > 0 && !d.BotLineTried && p.BlockedTicks >= Huddle.Ticks(w, c.botLineMoveS))
            {
                d.BotLineTried = true;
                d.Pressed = w.Rng.Value() < c.botSwimFrac ? DefMove.Swim : DefMove.BullRush;
                return;
            }
            var b = w.Ball;
            if (b.State == BallState.Flying && !d.BotCoverTried)
            {
                if (b.VY >= 0f) return;   // bots go for it as it comes down to the receiver, not as it leaves the QB
                int k = TicksToReach(w, p, c.swatMaxHeightYd, Huddle.Ticks(w, c.swatActiveS));
                if (k >= 0 && k <= Huddle.Ticks(w, c.botCoverLeadS))
                {
                    d.BotCoverTried = true;
                    if (w.Rng.Value() < c.botCoverChance) d.Pressed = w.Rng.Value() < c.botJumpFrac ? DefMove.Jump : DefMove.Swat;
                }
                return;
            }
            var carrier = LivePlay.Carrier(w);
            if (carrier != null && carrier.Team != p.Team && !d.BotDiveTried)
            {
                float dist = LivePlay.Dist(p, carrier);
                bool closing = (carrier.X - p.X) * (carrier.VX - p.VX) + (carrier.Z - p.Z) * (carrier.VZ - p.VZ) < 0f;
                if (closing && dist > w.Cfg.play.tackleReachYd && dist <= c.botDiveFromYd)
                {
                    d.BotDiveTried = true;
                    if (w.Rng.Value() < c.botDiveChance) d.Pressed = DefMove.Dive;
                }
            }
        }

        // Ticks until the ball in the air is within swat reach of him (running on as he is) and under `top`; -1: not
        // within `within` ticks.
        static int TicksToReach(World w, Player p, float top, int within)
        {
            var b = w.Ball;
            float g = w.Cfg.ball.gravityYdS2, r2 = w.Cfg.defense.swatReachYd * w.Cfg.defense.swatReachYd;
            for (int k = 0; k <= within; k++)
            {
                float t = k * w.Dt;
                float y = b.Y + b.VY * t - g * t * t * 0.5f;
                if (y < 0f) return -1;
                float dx = b.X + b.VX * t - (p.X + p.VX * t), dz = b.Z + b.VZ * t - (p.Z + p.VZ * t);
                if (y <= top && dx * dx + dz * dz <= r2) return k;
            }
            return -1;
        }

        // Off balance or held: give back part of this tick's movement (like a block).
        static void Slow(World w, Player p, float keep)
        {
            p.X -= p.VX * w.Dt * (1f - keep);
            p.Z -= p.VZ * w.Dt * (1f - keep);
            p.VX *= keep; p.VZ *= keep;
        }

        static void Show(Defender d, DefMove m, bool won) { d.Shown = m; d.ShownTicks = 0; d.ShownWon = won; }

        static void Count(World w, DefMove m, bool won)
        {
            var t = won ? Of(w).Won : Of(w).Tried;
            t[m] = (t.TryGetValue(m, out int n) ? n : 0) + 1;
        }

        static float Skill(SortedDictionary<string, float> by, string pos) => by != null && pos != null && by.TryGetValue(pos, out float v) ? v : 0f;
    }

    // game.json "defense" (WS-03). Seconds and yards; chances are 0..1.
    public sealed class DefenseConfig
    {
        public float swimEarlyS, swimEarlyChance, swimLateChance, swimFreeS;
        public float bullSnapS, bullSnapChance, bullChance, bullPushYd, bullStunS, bullFreeS;
        public float swatReachYd, swatMaxHeightYd, swatActiveS, swatPerfectS, swatPerfectChance, swatChance, swatRollFrac;
        public float jumpMaxHeightYd, jumpPickPerfectChance, jumpPickChance, jumpKnockChance;
        public float coverMissS, coverMissSpeedFrac;
        public float diveSpeedYdS, diveAirS, diveReachYd, diveChance, diveStretchPenalty, diveFromBehindBonus, diveDownS;
        public float cooldownS, shownS;
        public SortedDictionary<string, float> lineSkill = new SortedDictionary<string, float>();
        public SortedDictionary<string, float> coverSkill = new SortedDictionary<string, float>();
        public float botLineMoveS, botSwimFrac, botCoverLeadS, botCoverChance, botJumpFrac, botDiveFromYd, botDiveChance;
    }
}
