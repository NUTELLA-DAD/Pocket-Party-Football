using PocketFootball.Sim;

namespace PocketFootball.Input
{
    // Which buttons a person's phone shows at this moment of the game (M1-f). The phone draws them (layouts.js):
    //   huddle  the playbook (only your team's)          qbpre   HIKE, FIND ME
    //   qb      a button per receiver shape, SCRAMBLE    carrier SPRINT, JUKE, SPIN, STIFF ARM, DIVE
    //   route   SPRINT, CALL FOR IT, CATCH               block   BLOCK (hold), SPRINT
    //   rush    SWIM, BULL RUSH, TACKLE, SWAT            lb      SPRINT, TACKLE, DIVE TACKLE, SWAT
    //   cover   SPRINT, TACKLE, JUMP, DIVE TACKLE        wait    FIND ME (after the whistle)
    //   walk    the plain stick and SPRINT (no live play: NO_LIVEPLAY=1)
    //   kick    the kick meter (the kicker only)            hold    FIND ME (the field goal holder, FEATURES.fgHolder)
    public static class PhoneLayouts
    {
        public static readonly string[] All = { "huddle", "qbpre", "qb", "carrier", "route", "block", "rush", "lb", "cover", "wait" };

        public static string For(World w, Player p)
        {
            if (!LivePlay.On(w)) return "walk";
            switch (w.Phase)
            {
                case Phase.Huddle: return "huddle";
                case Phase.Whistle: return "wait";
            }
            if (Kicking.WaitingToKick(w, p)) return "kick";   // WS-01: the kick meter, for the kicker only
            if (p != null && p == Kicking.Holder(w) && !w.Kick.Kicked) return "hold";   // FEATURES.fgHolder: he just holds it
            var carrier = LivePlay.Carrier(w);
            bool qb = p == LivePlay.Qb(w);
            if (w.Phase == Phase.Set) return qb ? "qbpre" : Role(p);
            if (carrier == p) return qb && !w.Thrown ? "qb" : "carrier";
            // an interception return: the team that threw it chases (TACKLE), the interceptor's team blocks
            if (w.Intercepted && carrier != null) return p.Team == carrier.Team ? "block" : "lb";
            return Role(p);
        }

        public static string Role(Player p)
        {
            switch (p.Pos)
            {
                case "OL": case "TE": return "block";
                case "WR": case "RB": return "route";
                case "DL": return "rush";
                case "LB": return "lb";
                case "CB": case "S": return "cover";
            }
            return "walk";
        }
    }
}
