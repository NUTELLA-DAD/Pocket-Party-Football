using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PocketParty.Net
{
    public enum SeatKind { Phone, Bot }

    public sealed class Seat
    {
        public int Slot;
        public string Name;
        public string Token;
        public string Pid;
        public SeatKind Kind;
        public bool Connected;
        public int Conn = -1;        // phone WebSocket connection id
        public int Team = -1;        // -1 until the player picks (the game decides what a team is)
    }

    // Who is in the game. Slots are never removed during a session: a dropped phone keeps its slot and team,
    // and gets them back by sending its saved token.
    public sealed class Roster
    {
        const int MaxNameLength = 12;
        readonly Seat[] seats;

        public Roster(int maxPlayers) { seats = new Seat[maxPlayers]; }

        public int Capacity => seats.Length;
        public Seat this[int slot] => slot >= 0 && slot < seats.Length ? seats[slot] : null;

        public IEnumerable<Seat> Seats { get { foreach (var s in seats) if (s != null) yield return s; } }

        public int Count { get { int n = 0; foreach (var s in seats) if (s != null) n++; return n; } }

        public int TeamCount(int team, bool humansOnly)
        {
            int n = 0;
            foreach (var s in seats) if (s != null && s.Team == team && (!humansOnly || s.Kind == SeatKind.Phone)) n++;
            return n;
        }

        public Seat FindByToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            foreach (var s in seats) if (s != null && s.Token == token) return s;
            return null;
        }

        public Seat FindByConn(int conn)
        {
            foreach (var s in seats) if (s != null && s.Kind == SeatKind.Phone && s.Conn == conn) return s;
            return null;
        }

        // Returns the seat, or null when the game is full. rejoined = true when the token matched.
        public Seat JoinPhone(int conn, string name, string token, string pid, out bool rejoined)
        {
            var old = FindByToken(token);
            if (old != null && old.Kind == SeatKind.Phone)
            {
                rejoined = true;
                old.Conn = conn; old.Connected = true;
                if (!string.IsNullOrWhiteSpace(name)) old.Name = CleanName(name, old.Slot);
                return old;
            }
            rejoined = false;
            var s = NewSeat(SeatKind.Phone);
            if (s == null) return null;
            s.Conn = conn; s.Pid = pid; s.Name = CleanName(name, s.Slot);
            return s;
        }

        public Seat AddBot(int team)
        {
            var s = NewSeat(SeatKind.Bot);
            if (s != null) { s.Name = "BOT " + (s.Slot + 1); s.Team = team; }
            return s;
        }

        public void Remove(Seat s)
        {
            if (s != null && s.Slot >= 0 && s.Slot < seats.Length && seats[s.Slot] == s) seats[s.Slot] = null;
        }

        public void Disconnect(Seat s)
        {
            if (s == null) return;
            s.Connected = false;
            s.Conn = -1;
        }

        Seat NewSeat(SeatKind kind)
        {
            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i] != null) continue;
                var s = new Seat { Slot = i, Kind = kind, Connected = true, Token = NewToken() };
                seats[i] = s;
                return s;
            }
            return null;
        }

        public static string CleanName(string name, int slot)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'') sb.Append(char.ToUpperInvariant(c));
                if (sb.Length >= MaxNameLength) break;
            }
            string n = sb.ToString().Trim();
            return n.Length > 0 ? n : "PLAYER " + (slot + 1);
        }

        static string NewToken()
        {
            var b = new byte[12];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_');
        }

        // A room code without look-alike letters (the alphabet comes from config: no O, I).
        public static string NewRoomCode(string letters, int length, Random rnd)
        {
            var c = new char[length];
            for (int i = 0; i < length; i++) c[i] = letters[rnd.Next(letters.Length)];
            return new string(c);
        }
    }
}
