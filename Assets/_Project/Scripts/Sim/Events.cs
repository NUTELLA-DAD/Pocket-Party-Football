using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // What happened in the game (WS-00), so the moves on the TV, the replay, sound and stats can read it instead of
    // comparing one tick's state with the last. LivePlay and Downs push; the sim itself never reads it back, and it
    // is not in World.Hash, so recording events never changes a game.
    public enum EventKind
    {
        Hike,           // Slot: the QB
        Handoff,        // Slot: the runner, Other: the QB
        Throw,          // Slot: the QB, Other: the receiver it is aimed at
        Catch,          // Slot: the receiver, Other: the QB
        Incomplete,     // Slot: the QB, at the ball
        Interception,   // Slot: the defender who caught it, Other: the QB
        Juke,           // Slot: the ball carrier
        MissedTackle,   // Slot: the tackler, Other: the ball carrier
        Tackle,         // Slot: the tackler, Other: the ball carrier, Text: BIG HIT, TRIPPED or LOST BALANCE (FEATURES.hitAngle)
        Sack,           // Slot: the tackler, Other: the QB (a sack is not also a Tackle)
        Touchdown,      // Slot: the scorer, Team: the team that scored (a pick six: the defense)
        OutOfBounds,    // Slot: the ball carrier
        Whistle,        // Slot: the ball carrier (or -1), Text: the result (TACKLED, INCOMPLETE, PLAY OVER, ...)
        FirstDown,      // Team: the team with the ball, at the new spot
        Turnover,       // Team: the team that gets the ball, Text: TURNOVER or TURNOVER ON DOWNS
        Stumble,        // Slot: the ball carrier, Other: the tackler whose glancing blow knocked him off balance (FEATURES.hitAngle)
        Slip,           // Slot: the player who slipped on a slick field cutting hard (FEATURES.weather / sandField, Weather.cs)
    }

    public struct GameEvent
    {
        public int Seq;          // counts every event since the game began (readers keep the next one they want)
        public int Tick;
        public EventKind Kind;
        public int Slot, Team;   // who it is about (-1: nobody) and their team
        public int Other;        // the other player in it (-1: none)
        public float X, Z;       // where, yards
        public string Text;

        public override string ToString() => Tick + " " + Kind + " slot " + Slot + " team " + Team + (Other >= 0 ? " other " + Other : "") + (string.IsNullOrEmpty(Text) ? "" : " " + Text);
    }

    // One list per tick (ThisTick) and a log of the last events.logKeep (Since), because the TV can step the sim
    // several ticks in one frame.
    public sealed class EventLog
    {
        public readonly List<GameEvent> ThisTick = new List<GameEvent>();
        readonly List<GameEvent> log = new List<GameEvent>();
        readonly int[] totals = new int[Enum.GetValues(typeof(EventKind)).Length];

        public int Next { get; private set; }   // the Seq the next event will get

        // How many of a kind since the game began.
        public int Total(EventKind kind) => totals[(int)kind];

        // World.Step calls this first.
        public void BeginTick() => ThisTick.Clear();

        public void Push(World w, EventKind kind, int slot, int team, float x, float z, int other = -1, string text = "")
        {
            var e = new GameEvent { Seq = Next, Tick = w.Tick, Kind = kind, Slot = slot, Team = team, Other = other, X = x, Z = z, Text = text };
            Next++;
            totals[(int)kind]++;
            ThisTick.Add(e);
            log.Add(e);
            int keep = w.Cfg.events.logKeep;
            if (keep > 0 && log.Count > keep) log.RemoveRange(0, log.Count - keep);
        }

        // About a player, where they stand.
        public void Push(World w, EventKind kind, Player who, Player other = null, string text = "")
        {
            if (who == null) { Push(w, kind, -1, -1, w.Ball.X, w.Ball.Z, other != null ? other.Slot : -1, text); return; }
            Push(w, kind, who.Slot, who.Team, who.X, who.Z, other != null ? other.Slot : -1, text);
        }

        // The events from seq on that are still in the log, oldest first, added to `into` (which is returned).
        // A reader keeps `log.Next` after each read and passes it next time.
        public List<GameEvent> Since(int seq, List<GameEvent> into)
        {
            foreach (var e in log) if (e.Seq >= seq) into.Add(e);
            return into;
        }
    }

    // How many events the log keeps for readers that fall behind (0: all of them).
    public sealed class EventsConfig
    {
        public int logKeep;
    }
}
