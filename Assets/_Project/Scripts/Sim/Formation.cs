using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Where each formation spot stands on the field, from the line of scrimmage and the offense's direction.
    // Two-way players (FEATURES.lobby): every player on a team holds one offense spot and one defense spot, and takes
    // the right one each time the team lines up. A person's own picks (Player.OffenseSpot / DefenseSpot) win; everyone
    // else on the team (bots, people who have not picked) keeps the spot they have and only moves off a picked one.
    // With no picks nobody ever moves, so every game without a lobby (bot games, tests) is the same with it on or off.
    //
    // FEATURES.formations (NO_FORMATIONS=1): each play names a formation (formation.sets in game.json). In the huddle
    // everyone stands in the base line-up; once the calls are in (Phase.Set) each team lines up in its play's formation,
    // and an audible shifts the team into the new play's one. Spot order (who is who) never changes, only where they stand.
    public static class Formation
    {
        public static bool TwoWay(World w) => w.Cfg.Feature("lobby");

        public static bool SetsOn(World w) => w.Cfg.Feature("formations");

        // Where the team's spots are right now: the called play's formation at the line, else the base line-up.
        public static List<SpotConfig> SpotsOf(World w, int team)
        {
            bool offense = team == w.Offense;
            var baseSpots = offense ? w.Cfg.formation.offense : w.Cfg.formation.defense;
            if (!SetsOn(w) || w.Phase == Phase.Huddle || team < 0 || team >= w.Calls.Length || w.Calls[team] == null) return baseSpots;
            return For(w.Cfg, Huddle.Find(w, w.Calls[team]), offense) ?? baseSpots;
        }

        // The play's formation as full spots, or null (no formation, an unknown one, the wrong side or the wrong count).
        public static List<SpotConfig> For(GameConfig cfg, PlayConfig play, bool offense)
        {
            if (play == null || string.IsNullOrEmpty(play.formation)) return null;
            if (!cfg.formation.sets.TryGetValue(play.formation, out var set) || set.side != (offense ? "offense" : "defense")) return null;
            if (set.Built != null) return set.Built;
            var baseSpots = offense ? cfg.formation.offense : cfg.formation.defense;
            if (set.spots.Count != baseSpots.Count) return null;
            var built = new List<SpotConfig>();
            for (int i = 0; i < baseSpots.Count; i++)
            {
                var b = baseSpots[i];
                var at = set.spots[i];
                built.Add(new SpotConfig { pos = b.pos, number = b.number, back = at.Length > 0 ? at[0] : b.back, z = at.Length > 1 ? at[1] : b.z });
            }
            set.Built = built;
            return built;
        }

        // An audible changed the team's call at the line: the team shifts into the new play's formation.
        public static void Shift(World w, int team)
        {
            if (!SetsOn(w)) return;
            foreach (var p in w.Players)
            {
                if (p.Team != team) continue;
                Place(w, p);
                p.X = p.HomeX; p.Z = p.HomeZ; p.VX = p.VZ = 0;
                p.GoalX = p.X; p.GoalZ = p.Z;
            }
            if (team != w.Offense) return;
            // the ball back to the QB, and a punt or field goal line-up is redone next tick (Kicking.Align)
            w.Ball.Reset();
            foreach (var p in w.Players) if (p.Team == team && p.Spot == 0) w.Ball.GiveTo(p, w.Cfg.ball);
            w.Kick.Placed = false;
        }

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
        // than spots. A person's pick wins. Everyone else keeps the spot they stand on now (a person who took a bot's
        // place keeps the bot's spot); only a player whose spot was picked by someone moves, to the first free spot.
        public static int SpotOn(World w, Player p, bool offense)
        {
            if (Owns(w, p, offense)) return offense ? p.OffenseSpot : p.DefenseSpot;
            if (Keeps(w, p, offense)) return p.Spot;
            int rank = 0;
            foreach (var q in w.Players)
            {
                if (q == p) break;
                if (q.Team == p.Team && !Owns(w, q, offense) && !Keeps(w, q, offense)) rank++;
            }
            int n = SpotCount(w, offense);
            for (int s = 0; s < n; s++)
            {
                if (PickedBy(w, p.Team, offense, s) != null || KeptBy(w, p.Team, offense, s) != null) continue;
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

        // The player without a pick who stands on this unpicked spot (the lowest slot if two ever do), or null.
        static Player KeptBy(World w, int team, bool offense, int spot)
        {
            if (spot < 0 || spot >= SpotCount(w, offense) || PickedBy(w, team, offense, spot) != null) return null;
            foreach (var q in w.Players) if (q.Team == team && q.Spot == spot && !Owns(w, q, offense)) return q;
            return null;
        }

        static bool Keeps(World w, Player p, bool offense) => !Owns(w, p, offense) && KeptBy(w, p.Team, offense, p.Spot) == p;

        static bool Owns(World w, Player p, bool offense)
        {
            int pick = offense ? p.OffenseSpot : p.DefenseSpot;
            return pick >= 0 && pick < SpotCount(w, offense) && PickedBy(w, p.Team, offense, pick) == p;
        }
    }
}
