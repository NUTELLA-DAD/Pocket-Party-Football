using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // FEATURES.teamPick (NO_TEAM_PICK=1 keeps game.json's two): which two teams play. The league is game.json's
    // "teams" (the default match: away first, home second) and then its "moreTeams". The game itself only ever sees
    // two teams, so picking replaces cfg.teams with the chosen pair before anything is built (TitleScreen's SETTINGS
    // page saves the pick; the Boot scene reloads on PLAY and applies it). Teams are named by their short code.
    public static class TeamPick
    {
        // Every team that can play, in order: the two from "teams", then "moreTeams". Fixed at load, so it stays the
        // same after a pick has replaced cfg.teams.
        public static List<TeamConfig> League(GameConfig cfg)
        {
            if (cfg.league == null)
            {
                cfg.league = new List<TeamConfig>(cfg.teams);
                if (cfg.moreTeams != null) foreach (var t in cfg.moreTeams) if (t != null) cfg.league.Add(t);
            }
            return cfg.league;
        }

        // Where a team's short code sits in the league (-1: none).
        public static int Find(GameConfig cfg, string shortCode)
        {
            if (string.IsNullOrEmpty(shortCode)) return -1;
            var league = League(cfg);
            for (int i = 0; i < league.Count; i++)
                if (string.Equals(league[i].@short, shortCode, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // The next team for one side, stepping dir (+1 / -1) through the league and skipping the other side's team.
        public static int Step(GameConfig cfg, int current, int other, int dir)
        {
            int n = League(cfg).Count;
            if (n < 2) return current;
            int i = current;
            for (int k = 0; k < n; k++)
            {
                i = ((i + dir) % n + n) % n;
                if (i != other) return i;
            }
            return current;
        }

        // Play away (teams[0]) against home (teams[1]), both short codes. Unknown codes, the same team twice, or the
        // switch off: game.json's own two. Returns true when the match changed.
        public static bool Apply(GameConfig cfg, string away, string home)
        {
            var league = League(cfg);
            if (!cfg.Feature("teamPick")) return false;
            int a = Find(cfg, away), h = Find(cfg, home);
            if (a < 0) a = 0;
            if (h < 0) h = 1;
            if (a == h || league.Count < 2) return false;
            if (cfg.teams.Count == 2 && cfg.teams[0] == league[a] && cfg.teams[1] == league[h]) return false;
            cfg.teams = new List<TeamConfig> { league[a], league[h] };
            return true;
        }

        // "CAC,WHL" (away, home), e.g. from PP_TEAMS: the two codes, or false when it isn't two codes.
        public static bool ParsePair(string text, out string away, out string home)
        {
            away = home = null;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(',');
            if (parts.Length != 2) return false;
            away = parts[0].Trim();
            home = parts[1].Trim();
            return away.Length > 0 && home.Length > 0;
        }
    }
}
