using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The TV's scoreboard (WS-02): the bar at the top centre (score, quarter and clock, down and distance), the banner
    // line under it (the huddle's check marks, the whistle's result, HALFTIME, FINAL) and the winner banner at the end.
    // Reads the sim, never writes it. With FEATURES.clock off the bar shows "1ST" and no clock, as before.
    public static class Scoreboard
    {
        static readonly string[] QuarterNames = { "1ST", "2ND", "3RD", "4TH" };

        public static string QuarterText(World w)
        {
            if (!Clock.On(w)) return QuarterNames[0];
            if (Clock.IsOver(w)) return Clock.InOvertime(w) ? "F/OT" : "FINAL";
            if (Clock.IsHalftime(w)) return "HALF";
            int ot = Clock.OvertimePeriod(w);
            if (ot > 0) return ot == 1 ? "OT" : "OT" + ot;   // FEATURES.overtime
            int q = w.GameClock.Quarter;
            return q >= 1 && q <= QuarterNames.Length ? QuarterNames[q - 1] : q + "TH";
        }

        // "3:59" (empty with the clock off or once the game is over).
        public static string ClockText(World w)
        {
            if (!Clock.On(w) || Clock.IsOver(w) || Clock.IsHalftime(w) || Clock.IsOvertimeBreak(w)) return "";
            int secs = w.GameClock.Started ? Clock.SecondsLeft(w) : Mathf.RoundToInt(w.Cfg.clock.quarterS);
            return (secs / 60) + ":" + (secs % 60).ToString("00");
        }

        // "ARROWS WIN 21-14", "ARROWS WIN 24-21 IN OT" or "TIE GAME 14-14".
        public static string WinnerText(World w)
        {
            var teams = w.Cfg.teams;
            int win = Clock.Winner(w);
            string score = w.Score.Length >= 2 ? "  " + Mathf.Max(w.Score[0], w.Score[1]) + "-" + Mathf.Min(w.Score[0], w.Score[1]) : "";
            return (win >= 0 && win < teams.Count ? teams[win].name + " WIN" : "TIE GAME") + score + (win >= 0 && Clock.InOvertime(w) ? " IN OT" : "");
        }

        // The TV's banner line (and the phones' ON THE TV strip): whether each team has called, NEVER what.
        public static string TvLine(World w)
        {
            if (Clock.IsOver(w)) return "FINAL   " + WinnerText(w);
            if (Clock.IsHalftime(w)) return "HALFTIME   (the teams switch ends)";
            if (Clock.IsOvertimeBreak(w)) return OvertimeLine(w);
            if (!Huddle.On(w)) return "";
            int hz = w.Cfg.sim.tickHz;
            int secs = (w.PhaseTicks + hz - 1) / hz;
            if (w.Phase == Phase.Set && LivePlay.On(w)) return "SET   (the QB hikes)";
            if (w.Phase == Phase.Live && w.Kick.Returned) return Kicking.Name(w) + " RETURN";   // (a run-back uses the interception rules, but it is no interception)
            if (w.Phase == Phase.Live) return w.Intercepted ? "INTERCEPTED!" : "";
            if (w.Phase == Phase.Whistle) return w.Result + "!" + (w.Note.Length > 0 ? "   " + w.Note : "");
            if (w.Phase != Phase.Huddle) return "LINE UP   (next huddle in " + secs + ")";
            var parts = new List<string> { "HUDDLE 0:" + secs.ToString("00") };
            for (int t = 0; t < w.Cfg.teams.Count; t++) parts.Add(w.Cfg.teams[t].name + (Huddle.IsLocked(w, t) ? " CALLED ✓" : " ..."));
            if (Clock.InOvertime(w) && w.GameClock.OtStage == OtStage.SuddenDeath) parts.Add("OT: NEXT SCORE WINS");
            return string.Join("     ", parts);
        }

        // FEATURES.overtime: the break before overtime, the toss and the rule.
        public static string OvertimeLine(World w)
        {
            var teams = w.Cfg.teams;
            int first = w.GameClock.OtFirst;
            string toss = first >= 0 && first < teams.Count ? teams[first].name + " WIN THE TOSS AND RECEIVE" : "";
            return "OVERTIME   " + toss + "   (both teams get the ball, then next score wins)";
        }

        // The bar at the top centre, and at the end of the game the winner banner. s = screen scale (height / 1080).
        public static void Draw(World w, float s)
        {
            Hud.Scoreboard(w.Cfg, w.Score, QuarterText(w), ClockText(w), Downs.On(w) && !Clock.IsOver(w) ? Downs.DownText(w) : "", s);
            if (Clock.IsOver(w)) WinnerBanner(w, s);
        }

        static GUIStyle big;

        // A wide band across the middle of the TV in the winner's colours (grey for a tie).
        static void WinnerBanner(World w, float s)
        {
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big.fontStyle = UiFont.On ? FontStyle.Normal : FontStyle.Bold;   // (Pocket Block is heavy already)
            big.fontSize = Mathf.RoundToInt(96 * s);
            big.normal.textColor = Color.white;
            int win = Clock.Winner(w);
            var team = win >= 0 && win < w.Cfg.teams.Count ? w.Cfg.teams[win] : null;
            Color fill = team != null ? Colors.Hex(team.color) : new Color(0.25f, 0.25f, 0.28f);
            Color edge = team != null ? Colors.Hex(team.color2) : Color.white;
            float h = 180 * s, y = (Screen.height - h) * 0.5f;
            GUI.color = edge;
            GUI.DrawTexture(new Rect(0, y - 8 * s, Screen.width, h + 16 * s), Texture2D.whiteTexture);
            GUI.color = new Color(fill.r, fill.g, fill.b, 0.95f);
            GUI.DrawTexture(new Rect(0, y, Screen.width, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string text = WinnerText(w);
            if (team != null)
            {
                var mark = TeamMarks.For(team);
                float m = h - 40 * s;
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - big.CalcSize(new GUIContent(text)).x * 0.5f - m - 30 * s, y + 20 * s, m, m), mark, ScaleMode.ScaleToFit);
            }
            if (UiFont.On) UiFont.Toon(new Rect(0, y, Screen.width, h), text, big, Color.white, 5f * s);
            else GUI.Label(new Rect(0, y, Screen.width, h), text, big);
        }
    }
}
