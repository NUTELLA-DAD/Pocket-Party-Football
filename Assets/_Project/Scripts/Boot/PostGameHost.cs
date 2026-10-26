using System;
using Newtonsoft.Json.Linq;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // The stats, the post-game screen and the phones' final card (WS-12, FEATURES.stats). PartyHost calls Drive with
    // one line at the end of its Update. The game is over when the game clock (WS-02, Sim/Clock.cs) says so; F8 on
    // the TV's keyboard shows or hides the screen at any time (a preview, for trying it without playing a whole game).
    // Once it shows, every phone gets `postgame` every stats.phoneResendS (so a phone back from a screen lock gets it).
    public static class PostGameHost
    {
        public static void Drive(PartyHost h)
        {
            var w = h.World;
            if (w == null || !Stats.On(w.Cfg)) return;
            var kb = Keyboard.current;
            var was = h.GetComponent<PostGame>();
            bool preview = was != null && was.Preview;
            if (kb != null && h.DevKeys && kb.f8Key.wasPressedThisFrame) preview = !preview;
            var g = PostGame.Drive(h.gameObject, w, h.Players, preview || GameOver(w), s => h.Roster[s]?.Name);
            if (g == null) return;
            g.Preview = preview;
            if (!g.Showing) { g.NextPhoneAt = 0f; return; }
            float now = Time.unscaledTime;
            if (h.Server == null || now < g.NextPhoneAt) return;
            g.NextPhoneAt = now + Mathf.Max(0.25f, w.Cfg.stats.phoneResendS);
            foreach (var seat in h.Roster.Seats)
                if (seat.Kind == SeatKind.Phone && seat.Connected && seat.Conn >= 0) h.Server.Send(seat.Conn, Card(w, g.Stats, seat.Slot, s => h.Roster[s]?.Name));
        }

        static bool GameOver(World w) => Clock.IsOver(w);   // WS-02: after the last quarter

        // postgame (public totals, plus this phone's own line in `mine`): the final score, the winner, the player of
        // the game, each team's totals, and this person's numbers. Nothing secret: no play ids or calls.
        public static string Card(World w, Stats stats, int slot, Func<int, string> names)
        {
            var cfg = w.Cfg;
            var teams = new JArray();
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                var tot = stats.Team(t);
                teams.Add(new JObject
                {
                    ["name"] = cfg.teams[t].name, ["short"] = cfg.teams[t].@short, ["color"] = cfg.teams[t].color, ["score"] = w.Score[t],
                    ["yds"] = tot.Yards, ["tds"] = tot.Tds, ["tackles"] = tot.Tackles, ["picks"] = tot.Picks,
                });
            }
            var o = new JObject { ["t"] = "postgame", ["teams"] = teams, ["winner"] = Winner(w), ["mvp"] = PostGame.MvpLine(w, stats, names) };
            var p = w.Find(slot);
            if (p != null)
            {
                var l = stats.Of(p);
                o["mine"] = new JObject
                {
                    ["team"] = l.Team, ["number"] = l.Number, ["pos"] = l.Pos, ["yds"] = l.Yards, ["rushYd"] = l.RushYd, ["recYd"] = l.RecYd,
                    ["catches"] = l.Catches, ["tackles"] = l.Tackles, ["sacks"] = l.Sacks, ["picks"] = l.Picks, ["tds"] = l.Tds,
                    ["points"] = l.Points, ["line"] = Stats.Summary(l),
                };
            }
            return PhoneProtocol.Text(o);
        }

        // The team with more points, -1 for a tie.
        static int Winner(World w)
        {
            int best = -1, most = int.MinValue; bool tie = false;
            for (int t = 0; t < w.Score.Length; t++)
            {
                if (w.Score[t] > most) { best = t; most = w.Score[t]; tie = false; }
                else if (w.Score[t] == most) tie = true;
            }
            return tie ? -1 : best;
        }
    }
}
