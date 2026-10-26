namespace PocketFootball.Sim
{
    // Where each formation spot stands on the field, from the line of scrimmage and the offense's direction.
    // Two-way players (FEATURES.lobby): every player on a team holds one offense spot and one defense spot, and takes
    // the right one each time the team lines up. A person's own picks (Player.OffenseSpot / DefenseSpot) win; everyone
    // else on the team (bots, people who have not picked) takes the spots nobody picked, in slot order. In a bot game
    // that is exactly the old one-spot-for-both-sides order, so the bot-game baseline is the same with it on or off.
    public static class Formation
    {
        public static bool TwoWay(World w) => w.Cfg.Feature("lobby");

        public static void Place(World w, Player p)
        {
            if (TwoWay(w)) p.Spot = SpotOn(w, p, p.Team == w.Offense);
            var spots = w.Spots(p.Team);
            if (p.Spot < 0 || p.Spot >= spots.Count) return;
            var s = spots[p.Spot];
            float dir = w.Dir;           // +1: the offense attacks +X
            float own = p.Team == w.Offense ? -dir : dir;     // which way is "back" for this team
            p.HomeX = w.LineOfScrimmageX + own * s.back;
            p.HomeZ = s.z;
            p.Pos = s.pos;
            p.Number = s.number;
        }

        public static int SpotCount(World w, bool offense) => (offense ? w.Cfg.formation.offense : w.Cfg.formation.defense).Count;

        // The player's spot when the team is on offense (offense = true) or defense; -1 when the team has more players
        // than spots.
        public static int SpotOn(World w, Player p, bool offense)
        {
            if (Owns(w, p, offense)) return offense ? p.OffenseSpot : p.DefenseSpot;
            int rank = 0;
            foreach (var q in w.Players)
            {
                if (q == p) break;
                if (q.Team == p.Team && !Owns(w, q, offense)) rank++;
            }
            int n = SpotCount(w, offense);
            for (int s = 0; s < n; s++)
            {
                if (PickedBy(w, p.Team, offense, s) != null) continue;
                if (rank == 0) return s;
                rank--;
            }
            return -1;
        }

        // Who stands on this spot of the team's offense (or defense) line-up, or null.
        public static Player Holder(World w, int team, bool offense, int spot)
        {
            foreach (var q in w.Players) if (q.Team == team && SpotOn(w, q, offense) == spot) return q;
            return null;
        }

        // The player who picked this spot (the lowest slot wins if two ever did), or null.
        static Player PickedBy(World w, int team, bool offense, int spot)
        {
            if (spot < 0) return null;
            foreach (var q in w.Players) if (q.Team == team && (offense ? q.OffenseSpot : q.DefenseSpot) == spot) return q;
            return null;
        }

        static bool Owns(World w, Player p, bool offense)
        {
            int pick = offense ? p.OffenseSpot : p.DefenseSpot;
            return pick >= 0 && pick < SpotCount(w, offense) && PickedBy(w, p.Team, offense, pick) == p;
        }
    }
}
