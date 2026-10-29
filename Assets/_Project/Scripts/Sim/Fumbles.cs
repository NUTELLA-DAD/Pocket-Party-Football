using System;

namespace PocketFootball.Sim
{
    // WS-04, behind FEATURES.fumbles (NO_FUMBLES=1). A big hit (the tackler going at least bigHitSpeedYdS) can knock
    // the ball loose instead of ending the play: it rolls off the way the hit went, the carrier and the tackler are
    // down for a moment, and everyone else scrambles for it (bots run at the ball). The first man on his feet to fall
    // on it ends the play there: his team has the ball at that spot ("FUMBLE LOST" is a change of possession, like an
    // interception). A ball that rolls out of bounds stays with the team that fumbled. No fumbles on an interception
    // return. A recovery in an end zone is spotted just outside it, like any play.
    // LivePlay's tackle calls OnHit; LivePlay.Step calls Step before it checks for a loose ball (an incompletion);
    // the bots ask Loose / Chase.
    public static class Fumbles
    {
        public static bool On(World w) => w.Cfg.Feature("fumbles");

        // A fumble on the grass in a live play (a whistle that came some other way leaves nothing loose for the next).
        public static bool Loose(World w) => w.Carry.Loose && w.Phase == Phase.Live && w.Ball.State == BallState.Loose;

        // A successful tackle (mul: FEATURES.hitAngle's odds for this kind of hit). True: the ball came out (the play goes on), so no whistle.
        public static bool OnHit(World w, Player tackler, Player carrier, float mul = 1f)
        {
            if (!On(w) || w.Intercepted) return false;
            var c = w.Cfg.carrier;
            float speed = (float)Math.Sqrt(tackler.VX * tackler.VX + tackler.VZ * tackler.VZ);
            if (speed < c.bigHitSpeedYdS) return false;
            float chance = c.fumbleChance;
            if (!CarrierMoves.InFront(tackler, carrier)) chance += c.fumbleBlindsideBonus;
            chance *= mul;   // FEATURES.hitAngle: a big hit shakes it loose more often
            chance *= Weather.Fumble(w);   // FEATURES.weather: a wet or frozen ball squirts out more often
            if (w.Rng.Value() >= chance) return false;
            Pop(w, tackler, carrier);
            return true;
        }

        static void Pop(World w, Player tackler, Player carrier)
        {
            var c = w.Cfg.carrier;
            var s = w.Carry;
            CarrierMoves.End(s);
            s.Loose = true;
            s.LooseTicks = 0;
            s.FumbledBy = carrier.Slot;
            s.HitBy = tackler.Slot;
            s.RecoveredBy = -1;
            s.Fumbles++;
            var b = w.Ball;
            b.Drop();
            b.X = carrier.X; b.Y = 0f; b.Z = carrier.Z;
            // it squirts out the way the tackler was going
            float speed = (float)Math.Sqrt(tackler.VX * tackler.VX + tackler.VZ * tackler.VZ);
            if (speed > 0f) { b.VX = tackler.VX / speed * c.fumblePopYdS; b.VZ = tackler.VZ / speed * c.fumblePopYdS; }
            carrier.StumbleTicks = c.downTicks;
            tackler.StumbleTicks = c.downTicks;
        }

        // The scramble, once per tick of a live play. True while a fumble is on (LivePlay.Step stops there).
        public static bool Step(World w)
        {
            var s = w.Carry;
            if (!s.Loose) return false;
            if (!Loose(w)) { s.Loose = false; return false; }
            var c = w.Cfg.carrier;
            var f = w.Cfg.field;
            var b = w.Ball;
            s.LooseTicks++;
            // out of bounds: dead there, the team that fumbled keeps it
            if (Math.Abs(b.X) > f.HalfLength || Math.Abs(b.Z) > f.HalfWidth)
            {
                s.Loose = false;
                b.VX = b.VZ = 0f;
                LivePlay.Whistle(w, "FUMBLE OUT OF BOUNDS", b.X);
                return true;
            }
            if (s.LooseTicks < c.recoverDelayTicks) return true;
            bool timeUp = s.LooseTicks >= Huddle.Ticks(w, c.scrambleMaxS);
            // the nearest man on his feet within reach (time up: the nearest man at all)
            Player best = null; float bd = timeUp ? float.MaxValue : c.recoverReachYd;
            foreach (var p in w.Players)
            {
                if (!timeUp && p.StumbleTicks > 0) continue;
                float dx = p.X - b.X, dz = p.Z - b.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d <= bd) { best = p; bd = d; }
            }
            if (best == null) return true;
            if (!timeUp && w.Rng.Value() >= c.recoverChance) return true;
            Recover(w, best);
            return true;
        }

        static void Recover(World w, Player p)
        {
            var s = w.Carry;
            s.Loose = false;
            s.RecoveredBy = p.Slot;
            w.Ball.GiveTo(p, w.Cfg.ball);
            if (p.Team == w.Offense) { LivePlay.Whistle(w, "FUMBLE RECOVERED", p.X); return; }
            s.FumblesLost++;
            LivePlay.Whistle(w, "FUMBLE LOST", p.X);
            TurnOver(w);
        }

        // The other team has it at the spot (Downs' own turnover, for a result Downs doesn't know): first and ten
        // the other way, applied at the next huddle. NO_DOWNS=1: every play just restarts at the spot, so nothing.
        static void TurnOver(World w)
        {
            if (!Downs.On(w)) return;
            float dir = -w.Dir;
            float goal = w.Cfg.field.GoalLineX;
            float x = w.NextLos + dir * w.Cfg.downs.firstDownYd;
            w.PendingTurnover = true;
            w.Down = 1;
            w.FirstDownX = x * dir > goal ? goal * dir : x;
            w.Note = "TURNOVER";
        }

        // A bot during the scramble: everyone on his feet sprints for the ball.
        public static PlayerInput Chase(World w, Player p)
        {
            float dx = w.Ball.X - p.X, dz = w.Ball.Z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) return default;
            return new PlayerInput { MoveX = dx / d, MoveZ = dz / d, Sprint = true };
        }
    }
}
