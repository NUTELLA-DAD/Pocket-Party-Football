using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Draw-your-own plays (FEATURES.customPlays; NO_CUSTOM_PLAYS=1 turns it off). In the huddle the offense's caller
    // picks a pass play as the base (its formation, its blocking, the QB's drop) and draws a new path with a finger
    // for any of the receivers (play.receivers). LOCK IN calls the base play with those paths in place of its routes:
    // a bot on that spot runs the drawn path, the QB's reads use it, and a person on that spot sees it as his job.
    // Receivers with no drawn path keep the base play's route. A hot route at the line still beats a drawn path; an
    // audible throws the drawing away (it belonged to the old play). It stays team-private: the drawing comes in from
    // the caller's phone and only goes back out in that team's job messages (Boot/Messages.JobFor).
    // Everything here belongs to one down (World.HuddleNumber); the next huddle starts with a clean sheet.
    public sealed class CustomPlaysConfig
    {
        public int maxPoints;          // points per drawn path (the phone thins the finger's trail to this many)
        public float maxAheadYd;       // how far downfield a path may go, yards from the ball
        public float maxBackYd;        // how far behind the ball
        public float sidelineInYd;     // paths stay this far inside the sidelines
        public string name, text;      // the play's name on the team's phones, and the drawn job's line
    }

    // One drawn path from the phone: the receiver's spot and [ahead, z] points in the play frame.
    public sealed class CustomRoute
    {
        public int spot;
        public List<float[]> pts = new List<float[]>();
    }

    // One World's drawings (World.Custom). Not hashed: what it changes (where players run) is.
    public sealed class CustomState
    {
        public int HuddleNumber = -1;                                                     // the down these belong to
        public readonly SortedDictionary<int, string> Base = new SortedDictionary<int, string>();   // team -> the base play id
        public readonly SortedDictionary<int, SortedDictionary<int, JobConfig>> Jobs =
            new SortedDictionary<int, SortedDictionary<int, JobConfig>>();                          // team -> spot -> drawn route
    }

    public static class CustomPlays
    {
        public static bool On(World w) => w.Cfg.Feature("customPlays");

        // Can this person draw the team's play right now? The offense's caller, in the huddle, before LOCK IN.
        public static bool CanDraw(World w, Player p) =>
            On(w) && Huddle.On(w) && p != null && p.Human && w.Phase == Phase.Huddle && p.Team == w.Offense
            && !Huddle.IsLocked(w, p.Team) && Huddle.Caller(w, p.Team) == p;

        // A play the drawing can sit on: an offense pass play in the team's playbook right now. Not a handoff (runner),
        // not a kick, and not a designed QB run (the QB's route ends past the line, the sneak).
        public static bool IsBase(World w, int team, PlayConfig play) =>
            play != null && play.side == "offense" && play.runner < 0 && !Kicking.IsKickPlay(w, play)
            && play.jobs.Count > 0 && QbStaysBack(play.jobs[0]) && Huddle.Playbook(w, team).Contains(play);

        static bool QbStaysBack(JobConfig qb) => qb == null || qb.pts.Count == 0 || qb.pts[qb.pts.Count - 1][0] <= 0f;

        // The caller locks in a drawn play: the base play with these paths. At least one path, each on a different
        // receiver spot, each with 1..maxPoints points; points past the field's limits are pulled back inside.
        public static bool Lock(World w, int slot, string baseId, IList<CustomRoute> routes)
        {
            var p = w.Find(slot);
            if (!CanDraw(w, p) || routes == null || routes.Count == 0) return false;
            var play = Huddle.Find(w, baseId);
            if (!IsBase(w, p.Team, play)) return false;
            var c = w.Cfg.customPlays;
            var jobs = new SortedDictionary<int, JobConfig>();
            foreach (var r in routes)
            {
                if (r == null || r.pts == null || r.pts.Count == 0 || r.pts.Count > c.maxPoints) return false;
                if (!w.Cfg.play.receivers.Contains(r.spot) || r.spot >= play.jobs.Count || jobs.ContainsKey(r.spot)) return false;
                var job = new JobConfig { kind = "route", text = c.text };
                foreach (var pt in r.pts)
                {
                    if (pt == null || pt.Length <= 1 || !Finite(pt[0]) || !Finite(pt[1])) return false;
                    job.pts.Add(Inside(w, pt[0], pt[1]));
                }
                jobs[r.spot] = job;
            }
            var s = w.Custom;
            if (s.HuddleNumber != w.HuddleNumber) { s.HuddleNumber = w.HuddleNumber; s.Base.Clear(); s.Jobs.Clear(); }
            s.Base[p.Team] = play.id;   // (set before the lock: both teams locked = Set at once)
            s.Jobs[p.Team] = jobs;
            if (Huddle.Lock(w, slot, play.id)) return true;
            Clear(w, p.Team);   // (can't happen after CanDraw, but never leave a drawing behind a call that didn't land)
            return false;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static float[] Inside(World w, float ahead, float z)
        {
            var c = w.Cfg.customPlays;
            float side = w.Cfg.field.HalfWidth - c.sidelineInYd;
            return new[] { Clamp(ahead, -c.maxBackYd, c.maxAheadYd), Clamp(z, -side, side) };
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // The team's drawing is gone (an audible changed the play).
        public static void Clear(World w, int team) { w.Custom.Base.Remove(team); w.Custom.Jobs.Remove(team); }

        // Did this team lock in a drawn play this down (and is it still the call)?
        public static bool IsCustom(World w, int team)
        {
            var s = w.Custom;
            return On(w) && s.HuddleNumber == w.HuddleNumber && team >= 0 && team < w.Calls.Length
                && s.Base.TryGetValue(team, out var id) && id == w.Calls[team] && s.Jobs.ContainsKey(team);
        }

        // The team's drawn routes by spot (empty when it has none this down).
        public static SortedDictionary<int, JobConfig> Jobs(World w, int team) =>
            IsCustom(w, team) ? w.Custom.Jobs[team] : new SortedDictionary<int, JobConfig>();

        // This player's drawn route, or null. Bots.JobOfCalled asks this after the hot route.
        public static JobConfig JobOf(World w, Player p)
        {
            if (p == null || !IsCustom(w, p.Team)) return null;
            return w.Custom.Jobs[p.Team].TryGetValue(p.Spot, out var job) ? job : null;
        }
    }
}
