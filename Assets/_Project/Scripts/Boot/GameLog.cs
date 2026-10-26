using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine;

namespace PocketFootball.Boot
{
    // WS-15: a text log of the game next to the build (Builds\Windows\logs\game-<date>-<time>.txt; in the editor
    // .logs\games\), for the playtest night. tools/playtest-report.ps1 turns it into a one-page summary.
    // PartyHost calls Drive once a frame. FEATURES.gamelog (NO_GAMELOG=1 turns it off). Nothing here writes to the sim.
    public static class GameLog
    {
        static GameLogWriter writer;
        static StreamWriter file;
        static World world;      // the game being written (a new World, e.g. the Boot scene loaded again, starts a new file)
        static float startedAt;
        static bool failed;

        public static string Path { get; private set; }

        public static void Drive(PartyHost host)
        {
            if (host == null || host.World == null || host.Photo != null || failed) return;
            if (GameBootstrap.Instance != null && GameBootstrap.Instance.Lab != null) return;   // the practice lab is drills, not a game
            if (!host.World.Cfg.Feature("gamelog")) return;
            if (world != host.World) Open(host);
            if (writer == null) return;
            writer.Observe(host.World, host.Roster, Time.unscaledTime - startedAt);
        }

        static void Open(PartyHost host)
        {
            OnQuit();     // (a new game in the same run: finish the old file)
            world = host.World;
            try
            {
#if UNITY_EDITOR
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), ".logs", "games");
#else
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "logs");
#endif
                Directory.CreateDirectory(dir);
                var now = DateTime.Now;
                Path = System.IO.Path.Combine(dir, "game-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
                // others may read it while the game runs (the report); AutoFlush: a crash keeps every line written so far
                file = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                startedAt = Time.unscaledTime;
                var room = host.Roster;
                writer = new GameLogWriter(file, host.World.Cfg, s => room[s]?.Name);
                int seed = GameBootstrap.Instance != null && GameBootstrap.Instance.Args != null ? GameBootstrap.Instance.Args.Seed : 0;
                writer.Header(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), seed, Application.version, host.Url);
                Application.logMessageReceivedThreaded += OnUnityLog;
                Application.quitting += OnQuit;
                Debug.Log("[log] game log: " + Path);
            }
            catch (Exception e)
            {
                failed = true;     // no log is better than a game that stops: say so once
                writer = null;
                Debug.LogWarning("[log] cannot write the game log: " + e.Message);
            }
        }

        static void OnUnityLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            var w = writer;
            if (w == null) return;
            string first = stack == null ? "" : stack.Split('\n')[0];
            w.Error(type.ToString().ToUpperInvariant(), message + (first.Length > 0 ? "  at " + first : ""));
        }

        static void OnQuit()
        {
            if (writer != null && world != null) writer.End(world, Time.unscaledTime - startedAt);
            Close();
        }

        static void Close()
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            Application.quitting -= OnQuit;
            var f = file;
            writer = null;
            file = null;
            world = null;
            try { f?.Dispose(); } catch (Exception) { }
        }
    }

    // The log itself: no Unity in here, so a test can run a bot game into it. One line per thing that happened:
    //   <tick> TAB <seconds since the log began> TAB <WHAT> TAB key=value TAB key=value ...
    // WHAT is every Sim/Events.cs kind (HIKE, THROW, CATCH, SACK, WHISTLE, ...), plus
    //   JOIN / TEAM / LEAVE / BACK / GONE   a phone joined, picked a team, lost its connection, came back, was removed
    //   PLAY      one per whistle: the play's number, the calls, the result and the yards (the sim's own numbers)
    //   BOT       a bot took over for a person who put the phone down (once per person per play)
    //   ERROR     an error or exception in the game's own log
    //   LOST      the event log moved on before we read it (should never happen)
    //   END       the game was closed: final score and totals
    // Lines starting with # are notes for a person reading the file.
    public sealed class GameLogWriter
    {
        readonly TextWriter to;
        readonly GameConfig cfg;
        readonly Func<int, string> nameOf;
        readonly object gate = new object();       // Unity's log arrives on other threads too
        readonly SortedDictionary<int, SeatSeen> seats = new SortedDictionary<int, SeatSeen>();
        readonly HashSet<int> botThisPlay = new HashSet<int>();
        readonly List<GameEvent> batch = new List<GameEvent>();
        readonly StringBuilder sb = new StringBuilder();
        int nextSeq, lastYards, lastTick, playsLogged;
        float lastSeconds;

        sealed class SeatSeen { public string Name; public int Team; public bool Connected; }

        public GameLogWriter(TextWriter to, GameConfig cfg, Func<int, string> nameOf)
        {
            this.to = to;
            this.cfg = cfg;
            this.nameOf = nameOf ?? (s => null);
        }

        public int PlaysLogged => playsLogged;

        public void Header(string started, int seed, string version, string url)
        {
            Raw("# Pocket Party Football game log");
            Raw("# started " + started + ", seed " + seed + ", version " + version + (string.IsNullOrEmpty(url) ? "" : ", phones join at " + url));
            Raw("# columns: tick (" + cfg.sim.tickHz + " a second), seconds since the log began, what happened, then key=value");
            Raw("# teams: " + string.Join(", ", TeamNames()));
            Raw("# tools/playtest-report.ps1 turns this file into a one-page summary");
        }

        // After the sim stepped (once a frame is enough: it reads the events it has not seen yet).
        public void Observe(World w, Roster roster, float seconds)
        {
            lock (gate) { lastTick = w.Tick; lastSeconds = seconds; }
            if (roster != null) Seats(roster, w.Tick, seconds);

            batch.Clear();
            w.Events.Since(nextSeq, batch);
            if (batch.Count > 0 && batch[0].Seq > nextSeq) Line(w.Tick, seconds, "LOST", "events=" + (batch[0].Seq - nextSeq));
            else if (batch.Count == 0 && w.Events.Next > nextSeq) Line(w.Tick, seconds, "LOST", "events=" + (w.Events.Next - nextSeq));
            nextSeq = w.Events.Next;
            foreach (var e in batch)
            {
                Event(w, e, seconds);
                if (e.Kind == EventKind.Hike) botThisPlay.Clear();
                if (e.Kind == EventKind.Whistle) Play(w, e, seconds);
            }

            if (w.Phase != Phase.Live) return;
            foreach (var p in w.Players)
            {
                if (!p.Human || !p.BotHelp || botThisPlay.Contains(p.Slot)) continue;
                botThisPlay.Add(p.Slot);
                Line(w.Tick, seconds, "BOT", "slot=" + p.Slot, "name=" + Name(w, p.Slot), "team=" + TeamName(p.Team), "pos=" + p.Pos);
            }
        }

        public void Error(string kind, string message)
        {
            int tick; float seconds;
            lock (gate) { tick = lastTick; seconds = lastSeconds; }
            Line(tick, seconds, "ERROR", "kind=" + kind, "message=" + Clean(message));
        }

        public void End(World w, float seconds)
        {
            var parts = new List<string> { "plays=" + w.PlaysRun, "yards=" + w.YardsGained };
            for (int t = 0; t < w.Score.Length; t++) parts.Add("score_" + TeamName(t) + "=" + w.Score[t]);
            foreach (var kv in w.Results) parts.Add("result_" + kv.Key.Replace(' ', '_') + "=" + kv.Value);
            Line(w.Tick, seconds, "END", parts.ToArray());
        }

        void Seats(Roster roster, int tick, float seconds)
        {
            foreach (var s in roster.Seats)
            {
                if (s.Kind != SeatKind.Phone) continue;
                if (!seats.TryGetValue(s.Slot, out var seen))
                {
                    seats[s.Slot] = new SeatSeen { Name = s.Name, Team = s.Team, Connected = s.Connected };
                    Line(tick, seconds, "JOIN", "slot=" + s.Slot, "name=" + Clean(s.Name), "team=" + TeamName(s.Team));
                    continue;
                }
                if (seen.Team != s.Team) { seen.Team = s.Team; Line(tick, seconds, "TEAM", "slot=" + s.Slot, "name=" + Clean(s.Name), "team=" + TeamName(s.Team)); }
                if (seen.Connected != s.Connected)
                {
                    seen.Connected = s.Connected;
                    Line(tick, seconds, s.Connected ? "BACK" : "LEAVE", "slot=" + s.Slot, "name=" + Clean(s.Name));
                }
            }
            List<int> gone = null;
            foreach (var kv in seats) if (roster[kv.Key] == null) (gone ??= new List<int>()).Add(kv.Key);
            if (gone == null) return;
            foreach (int slot in gone)
            {
                Line(tick, seconds, "GONE", "slot=" + slot, "name=" + Clean(seats[slot].Name));
                seats.Remove(slot);
            }
        }

        void Event(World w, GameEvent e, float seconds)
        {
            var parts = new List<string>();
            if (e.Slot >= 0) { parts.Add("slot=" + e.Slot); parts.Add("name=" + Name(w, e.Slot)); }
            if (e.Team >= 0) parts.Add("team=" + TeamName(e.Team));
            if (e.Other >= 0) { parts.Add("other=" + e.Other); parts.Add("other_name=" + Name(w, e.Other)); }
            parts.Add("x=" + e.X.ToString("0.0", CultureInfo.InvariantCulture));
            parts.Add("z=" + e.Z.ToString("0.0", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(e.Text)) parts.Add("text=" + Clean(e.Text));
            Line(e.Tick, seconds, ToWord(e.Kind), parts.ToArray());
        }

        // One line a play, at its whistle. The yards are the sim's own (World.YardsGained went up by them), so the
        // report adds up to the same numbers as the bot-game tests.
        void Play(World w, GameEvent whistle, float seconds)
        {
            int yards = w.YardsGained - lastYards;
            lastYards = w.YardsGained;
            playsLogged++;
            int off = w.Offense, def = w.Defense;     // (a turnover swaps them after the whistle, not at it)
            int people = 0;
            foreach (var p in w.Players) if (p.Human) people++;
            Line(whistle.Tick, seconds, "PLAY",
                "n=" + w.PlaysRun,
                "offense=" + TeamName(off),
                "call=" + Call(w, off),
                "defense_call=" + Call(w, def),
                "result=" + Clean(whistle.Text),
                "yards=" + yards,
                "by=" + (whistle.Slot >= 0 ? Name(w, whistle.Slot) : ""),
                "people=" + people,
                "note=" + Clean(w.Note),
                "score=" + string.Join("-", Array.ConvertAll(w.Score, s => s.ToString(CultureInfo.InvariantCulture))));
        }

        static string Call(World w, int team) => team >= 0 && team < w.Calls.Length && w.Calls[team] != null ? w.Calls[team] : "";

        string Name(World w, int slot)
        {
            string n = nameOf(slot);
            if (!string.IsNullOrEmpty(n)) return Clean(n);
            var p = w.Find(slot);
            return p == null ? "#" + slot : "bot " + p.Pos + " " + TeamShort(p.Team);
        }

        string TeamName(int team) => team >= 0 && team < cfg.teams.Count ? cfg.teams[team].name : "none";
        string TeamShort(int team) => team >= 0 && team < cfg.teams.Count ? cfg.teams[team].@short : "";

        IEnumerable<string> TeamNames() { for (int t = 0; t < cfg.teams.Count; t++) yield return TeamName(t); }

        // HIKE, THROW, MISSED_TACKLE, OUT_OF_BOUNDS, FIRST_DOWN, ...
        public static string ToWord(EventKind k)
        {
            string s = k.ToString();
            var b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) b.Append('_');
                b.Append(char.ToUpperInvariant(s[i]));
            }
            return b.ToString();
        }

        // tabs and line breaks would split a line
        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        void Line(int tick, float seconds, string what, params string[] parts)
        {
            lock (gate)
            {
                sb.Clear();
                sb.Append(tick).Append('\t').Append(seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append('\t').Append(what);
                foreach (var p in parts) sb.Append('\t').Append(p);
                Write(sb.ToString());
            }
        }

        void Raw(string text) { lock (gate) Write(text); }

        void Write(string text)
        {
            try { to.WriteLine(text); }
            catch (Exception) { }    // never log from here: an error about the log would land back in the log
        }
    }
}
