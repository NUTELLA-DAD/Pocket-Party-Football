using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    public enum CarrierMove { None, Spin, StiffArm, Dive }

    // WS-04: the ball carrier's moves and the fumble numbers (game.json "carrier"). Odds are 0..1; a *TackleMul
    // multiplies the tackle odds while the move lasts (0 = can't be tackled, 1 = no help).
    public sealed class CarrierConfig
    {
        // SPIN: spinTicks of slippery, at spinSpeedFrac of his speed; STIFF ARM: stiffArmTicks, only against a tackler
        // in front of him; DIVE: within diveRangeYd of the goal line, a lunge of diveLungeYd over diveTicks, then he is
        // down where he lands (unless he made it in). cooldownTicks: between two moves of one carrier.
        public int spinTicks, stiffArmTicks, diveTicks, cooldownTicks;
        public float spinTackleMul, spinSpeedFrac, stiffArmTackleMul, diveTackleMul, diveRangeYd, diveLungeYd;
        // a bot carrier makes a move when a defender gets within botMoveYd
        public float botMoveYd;
        // Fumbles.cs: a hit by a tackler going at least bigHitSpeedYdS knocks it loose fumbleChance of the time
        // (+ fumbleBlindsideBonus from behind); the ball rolls off at fumblePopYdS the way the hit went; both men are
        // down for downTicks. After recoverDelayTicks, the nearest man on his feet within recoverReachYd falls on it
        // recoverChance of the ticks; after scrambleMaxS the nearest man gets it whatever.
        public float fumbleChance, fumbleBlindsideBonus, bigHitSpeedYdS, fumblePopYdS, recoverReachYd, recoverChance, scrambleMaxS;
        public int downTicks, recoverDelayTicks;
    }

    // The carrier moves and fumble scramble of one game (World.Carry). Only the ball carrier is ever in a move.
    public sealed class CarrierState
    {
        public CarrierMove Move;               // the carrier's move right now
        public int Slot = -1;                  // who is making it
        public int Ticks;                      // ticks it has left
        public int StartTick;                  // when it began (the view counts its key frames from here)
        public int DownSlot = -1;              // a diver who came up short, on the grass until the next huddle
        public readonly SortedDictionary<int, int> Cooldown = new SortedDictionary<int, int>();   // slot -> ticks
        public readonly int[] Started = new int[Enum.GetValues(typeof(CarrierMove)).Length];   // moves made, by kind
        // a fumble (Fumbles.cs)
        public bool Loose;                     // the ball is on the grass and anyone can fall on it
        public int LooseTicks;
        public int FumbledBy = -1, HitBy = -1, RecoveredBy = -1;
        public int Fumbles, FumblesLost;
    }

    // WS-04, behind FEATURES.carriermoves (NO_CARRIERMOVES=1). The carrier's SPIN, STIFF ARM and DIVE: a person presses
    // them (PlayerInput.Move); a bot carrier spins away from a man coming from the side or behind, stiff-arms one in
    // front, dives when he is near the goal line with a man on him. Each move lowers the tackle odds for a moment
    // (TackleOdds, called by LivePlay's tackle). World.Step calls Act for every player after LivePlay.Act.
    public static class CarrierMoves
    {
        public static bool On(World w) => w.Cfg.Feature("carriermoves");

        // The tackle odds after the carrier's move (and 0 while a fumble is on the grass: there is nobody to tackle).
        public static float TackleOdds(World w, Player tackler, Player carrier, float odds)
        {
            if (Fumbles.Loose(w)) return 0f;
            if (!On(w)) return odds;
            var s = w.Carry;
            var c = w.Cfg.carrier;
            if (s.Slot != carrier.Slot) return odds;
            switch (s.Move)
            {
                case CarrierMove.Spin: return odds * c.spinTackleMul;
                case CarrierMove.StiffArm: return InFront(tackler, carrier) ? odds * c.stiffArmTackleMul : odds;
                case CarrierMove.Dive: return odds * c.diveTackleMul;
            }
            return odds;
        }

        // The tackler is ahead of the carrier, the way he runs (standing still: anyone is in front of him).
        public static bool InFront(Player tackler, Player carrier) =>
            (tackler.X - carrier.X) * carrier.VX + (tackler.Z - carrier.Z) * carrier.VZ >= 0f;

        // The way a player's team is going (+1 toward +X).
        public static float DirOf(World w, Player p) => p.Team == w.Offense ? w.Dir : -w.Dir;

        // Yards to the goal line he is running at (negative: he is in the end zone).
        public static float ToGoal(World w, Player p) => w.Cfg.field.GoalLineX - p.X * DirOf(w, p);

        public static void Act(World w, Player p, PlayerInput input)
        {
            if (!On(w)) return;
            var s = w.Carry;
            if (s.Cooldown.TryGetValue(p.Slot, out int cd) && cd > 0) s.Cooldown[p.Slot] = cd - 1;
            if (w.Phase != Phase.Live)
            {
                End(s);
                s.Cooldown.Clear();
                if (w.Phase == Phase.Huddle) s.DownSlot = -1;
                return;
            }
            if (LivePlay.Carrier(w) != p) { if (s.Slot == p.Slot) End(s); return; }
            if (s.Slot == p.Slot && s.Move != CarrierMove.None) { Continue(w, p); return; }
            var want = p.Human && !p.BotHelp ? input.Move : BotMove(w, p);
            if (want != CarrierMove.None) Start(w, p, want);
        }

        // Starts a move if the carrier may make it now. False: not now (cooling down, or a dive too far out).
        public static bool Start(World w, Player p, CarrierMove move)
        {
            var s = w.Carry;
            var c = w.Cfg.carrier;
            if (move == CarrierMove.None || (s.Cooldown.TryGetValue(p.Slot, out int cd) && cd > 0)) return false;
            int ticks = c.spinTicks;
            if (move == CarrierMove.StiffArm) ticks = c.stiffArmTicks;
            if (move == CarrierMove.Dive)
            {
                float toGoal = ToGoal(w, p);
                if (toGoal <= 0f || toGoal > c.diveRangeYd) return false;
                ticks = c.diveTicks;
            }
            if (move == CarrierMove.Spin) { p.VX *= c.spinSpeedFrac; p.VZ *= c.spinSpeedFrac; }
            s.Move = move;
            s.Slot = p.Slot;
            s.Ticks = Math.Max(1, ticks);
            s.StartTick = w.Tick;
            s.Cooldown[p.Slot] = s.Ticks + c.cooldownTicks;
            s.Started[(int)move]++;
            return true;
        }

        // One tick of the move under way.
        static void Continue(World w, Player p)
        {
            var s = w.Carry;
            var c = w.Cfg.carrier;
            if (s.Move == CarrierMove.Dive)
            {
                // the lunge: straight at the goal line, no steering
                p.X += DirOf(w, p) * c.diveLungeYd / Math.Max(1, c.diveTicks);
                p.VZ = 0f;
            }
            if (--s.Ticks > 0) return;
            bool dove = s.Move == CarrierMove.Dive;
            End(s);
            // short of the goal line: down where he landed (over it: LivePlay calls the touchdown this tick)
            if (dove && ToGoal(w, p) >= 0f && !p.OutOfBounds)
            {
                s.DownSlot = p.Slot;
                LivePlay.Whistle(w, "DOWN", p.X);
            }
        }

        public static void End(CarrierState s)
        {
            s.Move = CarrierMove.None;
            s.Slot = -1;
            s.Ticks = 0;
        }

        // A bot carrier (or a bot helping an idle person): a move when a defender closes within botMoveYd.
        // The QB in the pocket before his throw keeps still (his blockers are there for that).
        static CarrierMove BotMove(World w, Player p)
        {
            var c = w.Cfg.carrier;
            if (p == LivePlay.Qb(w) && !w.Thrown && p.Team == w.Offense) return CarrierMove.None;
            Player near = null; float nd = c.botMoveYd;
            foreach (var d in w.Players)
            {
                if (d.Team == p.Team || d.StumbleTicks > 0) continue;
                float dist = LivePlay.Dist(p, d);
                if (dist <= nd) { near = d; nd = dist; }
            }
            if (near == null) return CarrierMove.None;
            float toGoal = ToGoal(w, p);
            if (toGoal > 0f && toGoal <= c.diveRangeYd) return CarrierMove.Dive;
            return InFront(near, p) ? CarrierMove.StiffArm : CarrierMove.Spin;
        }
    }
}
