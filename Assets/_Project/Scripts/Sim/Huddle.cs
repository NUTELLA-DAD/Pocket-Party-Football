using System.Collections.Generic;

namespace PocketFootball.Sim
{
    public enum Phase { Huddle, Set }

    // The secret huddle (M1-b). Each team picks a play from its own playbook: the caller (the person at spot 0, QB on
    // offense, middle LB on defense; else the first person on that side by spot) taps LOCK IN, teammates SUGGEST.
    // When the clock runs out the bot coach picks for a team that hasn't locked: the most-suggested play, else one at
    // random (seeded). Both locked (or time up) -> Set: everyone lines up with their job. Until M1-c's hike the Set
    // phase just lasts setSeconds and then the next huddle starts.
    // FEATURES.huddle off (NO_HUDDLE=1): the game stays in Set and nobody calls anything.
    public static class Huddle
    {
        public static bool On(World w) => w.Cfg.Feature("huddle");

        public static void Begin(World w)
        {
            w.Phase = Phase.Huddle;
            w.HuddleNumber++;
            w.PhaseTicks = Ticks(w, w.Cfg.huddle.seconds);
            for (int t = 0; t < w.Calls.Length; t++) w.Calls[t] = null;
            w.Suggestions.Clear();
            w.LineUp();
        }

        static int Ticks(World w, float seconds) => (int)(seconds * w.Cfg.sim.tickHz);

        public static string SideOf(World w, int team) => team == w.Offense ? "offense" : "defense";

        // The team's plays, by id.
        public static List<PlayConfig> Playbook(World w, int team)
        {
            var list = new List<PlayConfig>();
            string side = SideOf(w, team);
            foreach (var p in w.Cfg.plays) if (p.side == side) list.Add(p);
            return list;
        }

        public static PlayConfig Find(World w, string id)
        {
            foreach (var p in w.Cfg.plays) if (p.id == id) return p;
            return null;
        }

        // The person who calls the team's play, or null (all bots: the bot coach calls).
        public static Player Caller(World w, int team)
        {
            Player best = null;
            foreach (var p in w.Players)
                if (p.Team == team && p.Human && (best == null || p.Spot < best.Spot)) best = p;
            return best;
        }

        public static bool IsLocked(World w, int team) => w.Calls[team] != null;

        static bool Allowed(World w, Player p, string playId)
        {
            if (p == null || !p.Human || w.Phase != Phase.Huddle || IsLocked(w, p.Team)) return false;
            var play = Find(w, playId);
            return play != null && play.side == SideOf(w, p.Team);
        }

        // A teammate's thumbs-up (one per person; a new one replaces the old).
        public static bool Suggest(World w, int slot, string playId)
        {
            var p = w.Find(slot);
            if (!Allowed(w, p, playId)) return false;
            w.Suggestions[slot] = playId;
            return true;
        }

        // Only the caller can lock in.
        public static bool Lock(World w, int slot, string playId)
        {
            var p = w.Find(slot);
            if (!Allowed(w, p, playId) || Caller(w, p.Team) != p) return false;
            w.Calls[p.Team] = playId;
            if (AllLocked(w)) GoSet(w);
            return true;
        }

        static bool AllLocked(World w)
        {
            for (int t = 0; t < w.Calls.Length; t++) if (w.Calls[t] == null) return false;
            return true;
        }

        // Thumbs-ups per play for one team.
        public static SortedDictionary<string, int> Thumbs(World w, int team)
        {
            var c = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            foreach (var kv in w.Suggestions)
            {
                var p = w.Find(kv.Key);
                if (p == null || p.Team != team) continue;
                c[kv.Value] = (c.TryGetValue(kv.Value, out int n) ? n : 0) + 1;
            }
            return c;
        }

        public static void Step(World w)
        {
            if (!On(w)) return;
            if (--w.PhaseTicks > 0) return;
            if (w.Phase == Phase.Huddle)
            {
                for (int t = 0; t < w.Calls.Length; t++) if (w.Calls[t] == null) w.Calls[t] = BotCoach(w, t);
                GoSet(w);
            }
            else Begin(w);
        }

        static void GoSet(World w)
        {
            w.Phase = Phase.Set;
            w.PhaseTicks = Ticks(w, w.Cfg.huddle.setSeconds);
            w.LineUp();
        }

        // The most-suggested play (ties: the first by id), else a seeded random one from the playbook.
        static string BotCoach(World w, int team)
        {
            string best = null;
            int most = 0;
            foreach (var kv in Thumbs(w, team)) if (kv.Value > most) { best = kv.Key; most = kv.Value; }
            if (best != null) return best;
            var book = Playbook(w, team);
            return book.Count > 0 ? book[w.Rng.Below(book.Count)].id : "";
        }

        // A player's job in the called play (null in the huddle, or with no play).
        public static JobConfig JobOf(World w, Player p)
        {
            if (w.Phase != Phase.Set || p.Team < 0 || p.Team >= w.Calls.Length) return null;
            var play = Find(w, w.Calls[p.Team]);
            return play != null && p.Spot >= 0 && p.Spot < play.jobs.Count ? play.jobs[p.Spot] : null;
        }
    }
}
