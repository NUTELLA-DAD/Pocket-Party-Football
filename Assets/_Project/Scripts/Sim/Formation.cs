namespace PocketFootball.Sim
{
    // Where each formation spot stands on the field, from the line of scrimmage and the offense's direction.
    public static class Formation
    {
        public static void Place(World w, Player p)
        {
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
    }
}
