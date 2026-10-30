using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Player ratings by position (FEATURES.ratings). Each position gets speed, size (weight and strength) and hands on
    // a 1..10 scale; `neutral` (5) plays exactly like the game with the switch off. The ratings go with the spot a man
    // is playing (a WR on offense who covers as a CB has the WR's numbers, then the CB's).
    //  speed: top speed (jog and sprint) changes speedPerPoint per point.
    //  size:  a block lasts blockPerPoint longer per point the blocker is bigger than his man (shorter if smaller);
    //         a tackle's odds gain tacklePerPoint per point the tackler is bigger than the carrier.
    //  hands: the catch chance (offense and defense) changes handsPerPoint per point, never above catchChanceMax.
    public static class Ratings
    {
        public static bool On(World w) => w.Cfg.Feature("ratings");

        static PosRating Of(World w, Player p)
        {
            var own = SeasonRules.Rating(w, p);   // FEATURES.season: a recruit plays on his own numbers
            if (own != null) return own;
            var by = w.Cfg.ratings.positions;
            return p != null && by != null && p.Pos != null && by.TryGetValue(p.Pos, out var r) ? r : null;
        }

        // How many points above (or below) neutral; 0 with the switch off or a position with no ratings.
        static float Speed(World w, Player p) { var r = Of(w, p); return r == null ? 0f : r.speed - w.Cfg.ratings.neutral; }
        static float Size(World w, Player p) { var r = Of(w, p); return r == null ? 0f : r.size - w.Cfg.ratings.neutral; }
        static float Hands(World w, Player p) { var r = Of(w, p); return r == null ? 0f : r.hands - w.Cfg.ratings.neutral; }

        // The position's speed on the 1..10 scale (neutral when it has none), switch on or off: who is quick enough
        // to return a kick (Kicking, FEATURES.fastReturner).
        public static int SpeedRating(World w, Player p) { var r = Of(w, p); return r != null ? r.speed : (int)w.Cfg.ratings.neutral; }

        // Mover: the share of the plain top speed this man runs at.
        public static float SpeedFrac(World w, Player p) => On(w) ? Math.Max(0f, 1f + w.Cfg.ratings.speedPerPoint * Speed(w, p)) : 1f;

        // LivePlay's catch roll: the plain chance, scaled by his hands.
        public static float Catch(World w, Player p, float plain)
        {
            if (!On(w)) return plain;
            float c = plain * (1f + w.Cfg.ratings.handsPerPoint * Hands(w, p));
            return Math.Max(0f, Math.Min(Math.Max(plain, w.Cfg.receiving.catchChanceMax), c));
        }

        // Blocking: the share of bots.shedAfterS this blocker holds this defender for.
        public static float ShedFrac(World w, Player blocker, Player defender) =>
            On(w) ? Math.Max(0f, 1f + w.Cfg.ratings.blockPerPoint * (Size(w, blocker) - Size(w, defender))) : 1f;

        // FEATURES.driveBlock: how many points bigger a is than b (0 with the switch off).
        public static float SizeEdge(World w, Player a, Player b) => On(w) ? Size(w, a) - Size(w, b) : 0f;

        // TryTackle: added to the tackle odds.
        public static float TackleBonus(World w, Player tackler, Player carrier) =>
            On(w) ? w.Cfg.ratings.tacklePerPoint * (Size(w, tackler) - Size(w, carrier)) : 0f;
    }

    // game.json "ratings". Ratings are 1..10; the per-point numbers are shares (speed, block, hands) or odds (tackle).
    public sealed class RatingsConfig
    {
        public float neutral;
        public float speedPerPoint, blockPerPoint, tacklePerPoint, handsPerPoint;
        public SortedDictionary<string, PosRating> positions = new SortedDictionary<string, PosRating>(StringComparer.Ordinal);
    }

    public sealed class PosRating
    {
        public int speed, size, hands;
    }
}
