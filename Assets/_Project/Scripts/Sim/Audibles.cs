using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Audibles and hot routes (WS-13, FEATURES.audibles; NO_AUDIBLES=1 turns it off). At the line (Phase.Set, after
    // the huddle and before the hike) the team's caller may change the called play perDown times per down, to another
    // play from the same playbook. An offense person on a receiver spot (play.receivers) may swap his own route for one
    // of the hotRoutes (or back to the called route with ""); a bot playing for him runs it, and the QB's reads use it.
    // An audible clears that team's hot routes (the routes belonged to the old play). Both stay team-private: the phones
    // hear about them only through Boot/AudibleHost.cs, which sends to that team's phones only.
    // Everything here belongs to one down (World.HuddleNumber); the next huddle starts it all fresh.
    public sealed class AudiblesConfig
    {
        public int perDown;
        public List<HotRouteConfig> hotRoutes = new List<HotRouteConfig>();
    }

    // pts: [ahead, inward] yards from the receiver's own spot (inward = toward the middle of the field).
    public sealed class HotRouteConfig
    {
        public string id, name, text;
        public List<float[]> pts = new List<float[]>();
    }

    // One World's audible state (World.Audible). Not hashed: what it changes (the call, where players run) is.
    public sealed class AudibleState
    {
        public int HuddleNumber = -1;                                                  // the down these belong to
        public readonly SortedDictionary<int, int> Used = new SortedDictionary<int, int>();          // team -> audibles this down
        public readonly SortedDictionary<int, string> Hot = new SortedDictionary<int, string>();     // slot -> hot route id
        public readonly SortedDictionary<int, JobConfig> HotJobs = new SortedDictionary<int, JobConfig>();   // slot -> the route as a job
        public readonly SortedDictionary<int, int> Version = new SortedDictionary<int, int>();       // team -> bumps on each change
    }

    public static class Audibles
    {
        public static bool On(World w) => w.Cfg.Feature("audibles");

        static AudibleState Fresh(World w)
        {
            var s = w.Audible;
            if (s.HuddleNumber != w.HuddleNumber) { s.HuddleNumber = w.HuddleNumber; s.Used.Clear(); s.Hot.Clear(); s.HotJobs.Clear(); }
            return s;
        }

        static bool AtTheLine(World w) => On(w) && Huddle.On(w) && w.Phase == Phase.Set;

        public static int Left(World w, int team)
        {
            if (!AtTheLine(w)) return 0;
            var s = w.Audible;   // (reads only: the TV side asks this too)
            int used = s.HuddleNumber == w.HuddleNumber && s.Used.TryGetValue(team, out int n) ? n : 0;
            int left = w.Cfg.audibles.perDown - used;
            return left > 0 ? left : 0;
        }

        public static int VersionOf(World w, int team) => w.Audible.Version.TryGetValue(team, out int v) ? v : 0;

        static void Bump(World w, int team) => w.Audible.Version[team] = VersionOf(w, team) + 1;

        // Can this person change the team's call right now?
        public static bool CanAudible(World w, Player p) =>
            p != null && p.Human && Left(w, p.Team) > 0 && Huddle.Caller(w, p.Team) == p;

        // The caller changes the play at the line. Another play from the same playbook, not the one already called.
        public static bool Call(World w, int slot, string playId)
        {
            var p = w.Find(slot);
            if (!CanAudible(w, p)) return false;
            var play = Huddle.Find(w, playId);
            if (play == null || play.side != Huddle.SideOf(w, p.Team) || playId == w.Calls[p.Team]) return false;
            var s = Fresh(w);
            w.Calls[p.Team] = playId;
            s.Used[p.Team] = (s.Used.TryGetValue(p.Team, out int n) ? n : 0) + 1;
            var drop = new List<int>();
            foreach (var kv in s.Hot) { var q = w.Find(kv.Key); if (q == null || q.Team == p.Team) drop.Add(kv.Key); }
            foreach (int k in drop) { s.Hot.Remove(k); s.HotJobs.Remove(k); }
            Formation.Shift(w, p.Team);   // FEATURES.formations: line up in the new play's formation
            Bump(w, p.Team);
            return true;
        }

        // Can this person pick a hot route right now? An offense person on a receiver spot, at the line.
        public static bool CanHot(World w, Player p) =>
            p != null && p.Human && AtTheLine(w) && p.Team == w.Offense && w.Cfg.play.receivers.Contains(p.Spot) && w.Calls[p.Team] != null;

        public static HotRouteConfig FindRoute(World w, string id)
        {
            foreach (var r in w.Cfg.audibles.hotRoutes) if (r.id == id) return r;
            return null;
        }

        // A receiver swaps to a hot route ("" = back to the called route).
        public static bool PickHot(World w, int slot, string routeId)
        {
            var p = w.Find(slot);
            if (!CanHot(w, p)) return false;
            var s = Fresh(w);
            if (string.IsNullOrEmpty(routeId))
            {
                if (!s.Hot.Remove(slot)) return false;
                s.HotJobs.Remove(slot);
            }
            else
            {
                var r = FindRoute(w, routeId);
                if (r == null || (s.Hot.TryGetValue(slot, out var had) && had == routeId)) return false;
                s.Hot[slot] = routeId;
                s.HotJobs[slot] = JobFor(w, p, r);
            }
            Bump(w, p.Team);
            return true;
        }

        // This player's hot route id this down, or "".
        public static string RouteOf(World w, Player p)
        {
            var s = w.Audible;
            return p != null && On(w) && s.HuddleNumber == w.HuddleNumber && s.Hot.TryGetValue(p.Slot, out var id) ? id : "";
        }

        // The hot route as a job in the play frame ([ahead, z] from the ball), or null. Bots.JobOfCalled asks this first.
        public static JobConfig HotJob(World w, Player p)
        {
            var s = w.Audible;
            if (!On(w) || p.Team != w.Offense || s.HuddleNumber != w.HuddleNumber) return null;
            return s.HotJobs.TryGetValue(p.Slot, out var job) ? job : null;
        }

        // The route's points moved from the receiver's own spot into the play frame.
        public static JobConfig JobFor(World w, Player p, HotRouteConfig r)
        {
            var spots = w.Spots(p.Team);
            var home = p.Spot >= 0 && p.Spot < spots.Count ? spots[p.Spot] : null;
            float ahead = home != null ? -home.back : 0f, z = home != null ? home.z : 0f;
            float inward = z > 0f ? -1f : 1f;
            var job = new JobConfig { kind = "route", text = r.text };
            foreach (var pt in r.pts) job.pts.Add(new[] { ahead + pt[0], z + inward * pt[1] });
            return job;
        }
    }
}
