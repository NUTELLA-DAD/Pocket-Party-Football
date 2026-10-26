using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // The bot coach v2 (WS-06), behind FEATURES.botcoach (NO_BOTCOACH=1: the old pick, a seeded random play).
    // It reads the situation as a few tags and picks a play by weight, seeded:
    //   distance  short (togo <= shortYd), long (togo >= longYd), else medium; goal (the line to gain is the goal line)
    //   down      late (down >= lateDown: 3rd and 4th), fourth (the last down)
    //   field     redzone (yardsToGoal <= redZoneYd), fgrange (yardsToGoal <= fieldGoalRangeYd),
    //             backedup (own goal line within backedUpYd)
    //   score     behind / ahead (by behindByPts / aheadByPts or more)
    // coach.offense[tag] and coach.defense[tag] give play ids a weight; a play's weight is the sum over the tags that
    // apply (otherWeight when none of them name it), so 3rd and long in the red zone adds three rows together.
    // Only plays in the team's playbook right now are picked, so PUNT and FIELD GOAL (FEATURES.kicking) can carry big
    // weights under fourth / fgrange / goal and still only come up on 4th down or a try. A negative weight takes a play
    // down in that situation (no deep shots in the red zone); a play's total never goes below zero.
    // A suggestion from a person still beats the coach (Huddle.BotCoach looks at thumbs-ups first).
    public static class BotCoach
    {
        public static bool On(World w) => w.Cfg.Feature("botcoach");

        // The play the coach calls for this team, or null (the coach is off: Huddle falls back to its random pick).
        public static string Call(World w, int team)
        {
            if (!On(w)) return null;
            var c = w.Cfg.coach;
            var book = Huddle.Playbook(w, team);
            if (book.Count == 0) return null;
            var rows = team == w.Offense ? c.offense : c.defense;
            var tags = Tags(w, team);
            float total = 0f;
            var weights = new float[book.Count];
            for (int i = 0; i < book.Count; i++)
            {
                float wt = 0f;
                bool named = false;
                foreach (var tag in tags)
                {
                    if (rows == null || !rows.TryGetValue(tag, out var row) || row == null) continue;
                    if (row.TryGetValue(book[i].id, out float v)) { wt += v; named = true; }
                }
                if (!named) wt = c.otherWeight;
                if (wt < 0f) wt = 0f;
                weights[i] = wt;
                total += wt;
            }
            if (total <= 0f) return book[w.Rng.Below(book.Count)].id;
            float pick = w.Rng.Value() * total;
            for (int i = 0; i < book.Count; i++)
            {
                pick -= weights[i];
                if (pick < 0f) return book[i].id;
            }
            return book[book.Count - 1].id;
        }

        // The situation, as the tags the coach's tables are keyed by (always one distance tag, in this order).
        public static List<string> Tags(World w, int team)
        {
            var c = w.Cfg.coach;
            var tags = new List<string>();
            float togo = ToGo(w);
            tags.Add(togo <= c.shortYd ? "short" : togo >= c.longYd ? "long" : "medium");
            if (w.FirstDownX * w.Dir >= w.Cfg.field.GoalLineX) tags.Add("goal");
            if (w.Down >= c.lateDown) tags.Add("late");
            if (w.Down >= w.Cfg.downs.downsToGain) tags.Add("fourth");
            if (YardsToGoal(w) <= c.redZoneYd) tags.Add("redzone");
            if (YardsToGoal(w) <= c.fieldGoalRangeYd) tags.Add("fgrange");
            if (YardsFromOwnGoal(w) <= c.backedUpYd) tags.Add("backedup");
            // the score from this team's side
            int other = team == 0 ? 1 : 0;
            int lead = other < w.Score.Length && team < w.Score.Length ? w.Score[team] - w.Score[other] : 0;
            if (c.behindByPts > 0 && -lead >= c.behindByPts) tags.Add("behind");
            if (c.aheadByPts > 0 && lead >= c.aheadByPts) tags.Add("ahead");
            return tags;
        }

        // Yards to the line to gain, the offense's goal line, and back to the offense's own goal line.
        public static float ToGo(World w) => (w.FirstDownX - w.LineOfScrimmageX) * w.Dir;
        public static float YardsToGoal(World w) => w.Cfg.field.GoalLineX - w.LineOfScrimmageX * w.Dir;
        public static float YardsFromOwnGoal(World w) => w.Cfg.field.GoalLineX + w.LineOfScrimmageX * w.Dir;
    }

    // game.json "coach" (WS-06): the situation thresholds and, per side, tag -> play id -> weight.
    public sealed class CoachConfig
    {
        public float shortYd, longYd, redZoneYd, fieldGoalRangeYd, backedUpYd, otherWeight;
        public int lateDown, behindByPts, aheadByPts;
        public SortedDictionary<string, SortedDictionary<string, float>> offense = new SortedDictionary<string, SortedDictionary<string, float>>(System.StringComparer.Ordinal);
        public SortedDictionary<string, SortedDictionary<string, float>> defense = new SortedDictionary<string, SortedDictionary<string, float>>(System.StringComparer.Ordinal);
    }
}
