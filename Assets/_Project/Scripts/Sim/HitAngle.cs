using System;

namespace PocketFootball.Sim
{
    // FEATURES.hitAngle (NO_HIT_ANGLE=1): a won tackle lands by how square and how hard the hit was (game.json "hit").
    // The hit is measured along the line between the two men: the closing speed is how fast the tackler comes at
    // the carrier along it (their velocities against each other), and how square it is is that share of all their
    // speed against each other (1 = straight through the middle of him, 0 = brushing past).
    //  BIG HIT: square (squareMin or more) and closing at bigHitClosingYdS or more. The carrier is driven back along
    //    the hit, knockbackPerYdS a yard of closing speed (at most knockbackMaxYd), and goes down there, but the ball
    //    is spotted where he was hit (forward progress, as the refs do it); the fumble odds are bigHitFumbleMul times
    //    as high.
    //  SQUARE: any other hit glanceMax or squarer (or the two men hardly moving against each other, below
    //    minRelYdS: a wrap-up): the tackle as before.
    //  GLANCING (squareness under glanceMax): no clean tackle. tripChance of the time he is tripped up and falls
    //    forward tripFallYd (the ball spotted where he was tripped); else he stays up but off balance for offBalanceS: he keeps offBalanceKeepFrac of his speed
    //    and is shoved the way he was hit at offBalanceShoveYdS, runs at offBalanceSpeedFrac of his top speed, any tackle on him gets offBalanceTackleBonus, and at the end he goes
    //    down on his own fallLaterChance of the time (LOST BALANCE). The tackler bounced off: he stumbles.
    // LivePlay's tackle calls Land; LivePlay.Step calls Step. The dive tackle (DefenseMoves) is a lunge at his legs and
    // brings him down as before. What the last hit was (World.Hit) is for the TV; the sim never reads it back.
    public enum HitKind { None, Square, BigHit, Trip, Glance, LostBalance }

    public sealed class HitState
    {
        public HitKind Last;
        public int Tick = -1, Tackler = -1, Carrier = -1;
        public float FromX, FromZ, ToX, ToZ;   // a big hit: where the carrier was hit and where he lands (yards)
        public int BigHits, Trips, Glances, LostBalance;   // counts since the game began
    }

    public sealed class HitConfig
    {
        public float squareMin, glanceMax, minRelYdS, bigHitClosingYdS, knockbackPerYdS, knockbackMaxYd, bigHitFumbleMul;
        public float tripChance, tripFallYd, offBalanceS, offBalanceSpeedFrac, offBalanceKeepFrac, offBalanceShoveYdS, offBalanceTackleBonus, fallLaterChance;
    }

    public static class HitAngle
    {
        public static bool On(World w) => w.Cfg.Feature("hitAngle");

        // How hard (closing speed, yards a second) and how square (0..1) the tackler meets the carrier.
        public static void Measure(Player tackler, Player carrier, out float closing, out float square, out float rel)
        {
            float nx = carrier.X - tackler.X, nz = carrier.Z - tackler.Z;
            float n = (float)Math.Sqrt(nx * nx + nz * nz);
            if (n > 0f) { nx /= n; nz /= n; } else { nx = 1f; nz = 0f; }
            float rx = tackler.VX - carrier.VX, rz = tackler.VZ - carrier.VZ;
            rel = (float)Math.Sqrt(rx * rx + rz * rz);
            closing = Math.Max(0f, rx * nx + rz * nz);
            square = rel > 0f ? closing / rel : 0f;
        }

        public static HitKind Classify(World w, Player tackler, Player carrier)
        {
            var c = w.Cfg.hit;
            Measure(tackler, carrier, out float closing, out float square, out float rel);
            if (rel < c.minRelYdS) return HitKind.Square;   // a wrap-up: nothing to glance off
            if (square >= c.squareMin && closing >= c.bigHitClosingYdS) return HitKind.BigHit;
            if (square < c.glanceMax) return HitKind.Glance;   // trip or off balance: Land rolls it
            return HitKind.Square;
        }

        // The tackle odds on a man already off balance.
        public static float TackleOdds(World w, Player carrier, float odds) =>
            On(w) && carrier.OffBalanceTicks > 0 ? odds + w.Cfg.hit.offBalanceTackleBonus : odds;

        // A won tackle roll. True: it is a tackle, spotted at spotX (a big hit: where he was hit, not where he landed);
        // fumbleMul: how much likelier the ball comes out. False: a glancing blow that left him on his feet.
        public static bool Land(World w, Player tackler, Player carrier, out float fumbleMul, out string how, out float spotX)
        {
            fumbleMul = 1f; how = ""; spotX = carrier.X;
            if (!On(w)) return true;
            var c = w.Cfg.hit;
            var s = w.Hit;
            var kind = Classify(w, tackler, carrier);
            s.Tick = w.Tick; s.Tackler = tackler.Slot; s.Carrier = carrier.Slot;
            s.FromX = s.ToX = carrier.X; s.FromZ = s.ToZ = carrier.Z;
            if (kind == HitKind.Glance) kind = w.Rng.Value() < c.tripChance ? HitKind.Trip : HitKind.Glance;
            s.Last = kind;
            float nx = carrier.X - tackler.X, nz = carrier.Z - tackler.Z, n = (float)Math.Sqrt(nx * nx + nz * nz);
            if (n > 0f) { nx /= n; nz /= n; } else { nx = w.Dir * (carrier.Team == w.Offense ? -1f : 1f); nz = 0f; }
            switch (kind)
            {
                case HitKind.BigHit:
                    Measure(tackler, carrier, out float closing, out _, out _);
                    float back = Math.Min(c.knockbackMaxYd, closing * c.knockbackPerYdS);
                    Shift(w, carrier, nx * back, nz * back);
                    s.ToX = carrier.X; s.ToZ = carrier.Z;   // (spotX stays where he was hit)
                    s.BigHits++;
                    fumbleMul = c.bigHitFumbleMul;
                    how = "BIG HIT";
                    return true;
                case HitKind.Trip:
                    // he goes down the way he was running
                    float sp = (float)Math.Sqrt(carrier.VX * carrier.VX + carrier.VZ * carrier.VZ);
                    if (sp > 0.5f) Shift(w, carrier, carrier.VX / sp * c.tripFallYd, carrier.VZ / sp * c.tripFallYd);
                    s.ToX = carrier.X; s.ToZ = carrier.Z;   // (spotted where he was tripped, so a fall never crosses the goal line)
                    s.Trips++;
                    fumbleMul = 0f;
                    how = "TRIPPED";
                    return true;
                case HitKind.Glance:
                    s.Glances++;
                    carrier.OffBalanceTicks = Math.Max(1, Huddle.Ticks(w, c.offBalanceS));
                    carrier.OffBalanceBy = tackler.Slot;
                    carrier.VX = carrier.VX * c.offBalanceKeepFrac + nx * c.offBalanceShoveYdS;   // he loses his stride, and goes the way he was hit
                    carrier.VZ = carrier.VZ * c.offBalanceKeepFrac + nz * c.offBalanceShoveYdS;
                    tackler.StumbleTicks = w.Cfg.play.stumbleTicks;
                    w.Events.Push(w, EventKind.Stumble, carrier, tackler, "OFF BALANCE");
                    return false;
            }
            return true;
        }

        // LivePlay.Step, before the tackles: the carrier's stagger just ran out (LivePlay.Act counts it down), and he
        // goes down on his own fallLaterChance of the time. True: the whistle blew.
        public static bool Step(World w, Player carrier)
        {
            if (!On(w) || carrier.OffBalanceTicks > 0 || carrier.OffBalanceBy < 0) return false;
            var by = w.Find(carrier.OffBalanceBy);
            carrier.OffBalanceBy = -1;
            if (w.Rng.Value() >= w.Cfg.hit.fallLaterChance) return false;
            var s = w.Hit;
            s.LostBalance++;
            s.Last = HitKind.LostBalance; s.Tick = w.Tick; s.Carrier = carrier.Slot; s.Tackler = by != null ? by.Slot : -1;
            s.FromX = s.ToX = carrier.X; s.FromZ = s.ToZ = carrier.Z;
            bool sack = carrier == LivePlay.Qb(w) && !w.Thrown && (carrier.X - w.LineOfScrimmageX) * w.Dir < 0f;
            // credited to the man who knocked him off balance, but nobody is on him: the TV puts only him down
            w.Events.Push(w, sack ? EventKind.Sack : EventKind.Tackle, by ?? carrier, carrier, "LOST BALANCE");
            LivePlay.Whistle(w, sack ? "SACKED" : "TACKLED", carrier.X);
            return true;
        }

        // Moves a man (a knock back, a fall) but not past the field's outer edge.
        static void Shift(World w, Player p, float dx, float dz)
        {
            var f = w.Cfg.field;
            float m = w.Cfg.players.boundsMarginYd;
            p.X = Math.Max(-f.HalfLength - m, Math.Min(f.HalfLength + m, p.X + dx));
            p.Z = Math.Max(-f.HalfWidth - m, Math.Min(f.HalfWidth + m, p.Z + dz));
        }
    }
}
