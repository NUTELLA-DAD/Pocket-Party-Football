using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // WS-07 the lobby and two-way players (FEATURES.lobby, NO_LOBBY=1 turns it off).
    // Each person picks one offense spot and one defense spot on their team (the phone's lobby.js). Taking a bot's
    // spot is instant; taking a person's spot asks them to swap. The game waits on the lobby until a phone taps
    // KICK OFF (or K on the TV keyboard). A dropped phone keeps its seat and its spots, a bot plays for it, and the
    // same person comes back to the same spots with their saved token.
    //   game -> phone: ui {layout: "lobby", aid}
    //                  lineup {held, team, teamName, color, color2, offense[], defense[], you {o, d}, ask, line}
    //                    offense / defense: one {pos, number, who, person, me, away} per spot (team-only: own team)
    //   phone -> game: spot {side: "o" | "d", spot, aid}, swap {yes, aid}, kickoff {aid}, lobbyme {}
    // FEATURES.huddleSwitch (NO_HUDDLE_SWITCH=1 turns it off): after kickoff the same spot and swap presses work from
    // the huddle screen (huddle.js, carrying the huddle screen's aid), so a person can switch spots between plays. The
    // lineup then carries switch: true, and every huddle screen of the team is sent again with the new spots.
    public sealed class Lobby : MonoBehaviour
    {
        public static Lobby Instance { get; private set; }

        PartyHost host;
        GameConfig cfg;
        bool shots;
        int aidCounter;
        bool relabel;
        public bool KickedOff { get; private set; }

        sealed class Ask { public int From; public bool Offense; public int Spot; public float Until; }

        readonly Dictionary<int, int> teamOf = new Dictionary<int, int>();       // by slot: the team last seen
        readonly Dictionary<int, int> connOf = new Dictionary<int, int>();       // by slot: the connection last seen
        readonly HashSet<int> onField = new HashSet<int>();                       // slots whose person has a player
        readonly HashSet<int> covered = new HashSet<int>();                       // slots a bot is playing for (phone dropped)
        readonly Dictionary<int, string> aidOf = new Dictionary<int, string>();  // by slot: the lobby screen's aid
        readonly Dictionary<int, Ask> asks = new Dictionary<int, Ask>();         // by the asked person's slot
        readonly Dictionary<int, string> notes = new Dictionary<int, string>();  // by slot: a one-off line for the phone

        public static Lobby Attach(PartyHost h, GameConfig cfg, CliArgs args)
        {
            var l = h.gameObject.AddComponent<Lobby>();
            l.host = h;
            l.cfg = cfg;
            l.shots = !string.IsNullOrEmpty(args.ShotsDir);
            Instance = l;
            return l;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool On => cfg != null && cfg.Feature("lobby");

        // Switching spots from the huddle screen, between plays (FEATURES.huddleSwitch).
        public bool Switching => On && cfg.Feature("huddleSwitch") && !Holding && W.Phase == Phase.Huddle && Huddle.On(W);

        // The sim waits while this is true (PartyHost.StepOnce). Never for screenshots, photo scenes or batch runs.
        public bool Holding => On && cfg.lobby.holdUntilKickoff && !KickedOff && !shots && host.Photo == null && (HoldOverride ?? !Application.isBatchMode);

        // Tests: hold (true) or never hold (false) before kickoff. Unset: hold, except in batch mode (the test runs and
        // tools), where nobody is there to tap KICK OFF.
        public static bool? HoldOverride;

        World W => host.World;
        Roster R => host.Roster;

        // ---- every frame, after the host has read the phones ----
        void LateUpdate()
        {
            if (!On) return;
            bool changed = false;
            var fresh = new List<Seat>();
            foreach (var seat in R.Seats)
            {
                if (seat.Kind != SeatKind.Phone) continue;
                bool known = teamOf.TryGetValue(seat.Slot, out int was);
                if (known && was != seat.Team) { R.ClearSpots(seat); asks.Remove(seat.Slot); changed = true; }
                bool newTeam = !known || was != seat.Team;
                teamOf[seat.Slot] = seat.Team;

                var p = W.Find(seat.Slot);
                bool body = p != null && p.Team == seat.Team;
                // a phone that joined while this person was away took over their player (it looked like a bot):
                // the person gets a bot's player back, so their seat, team and spots are still there when they return
                if (!body && !seat.Connected && covered.Contains(seat.Slot))
                {
                    p = Restore(seat);
                    body = p != null;
                    if (body) changed = true; else covered.Remove(seat.Slot);
                }
                bool newBody = body && onField.Add(seat.Slot);
                if (!body) onField.Remove(seat.Slot);
                if (body)
                {
                    if (p.OffenseSpot != seat.OffenseSpot || p.DefenseSpot != seat.DefenseSpot)
                    {
                        p.OffenseSpot = seat.OffenseSpot; p.DefenseSpot = seat.DefenseSpot;
                        changed = true;
                    }
                    // a bot plays for a dropped phone; the person takes over again the moment they are back
                    bool human = seat.Connected || !cfg.lobby.botCoversDropped;
                    if (p.Human != human) { p.Human = human; changed = true; }
                    if (human) covered.Remove(seat.Slot); else covered.Add(seat.Slot);
                }

                int conn = seat.Connected ? seat.Conn : -1;
                bool reconnected = !connOf.TryGetValue(seat.Slot, out int hadConn) || hadConn != conn;
                connOf[seat.Slot] = conn;
                if (Holding && conn >= 0 && body && (reconnected || newTeam || newBody)) fresh.Add(seat);
            }

            float now = Time.unscaledTime;
            var expired = new List<int>();
            foreach (var kv in asks) if (kv.Value.Until <= now) expired.Add(kv.Key);
            foreach (int slot in expired)
            {
                var ask = asks[slot];
                asks.Remove(slot);
                notes[ask.From] = "No answer from " + NameOf(slot) + ".";
                changed = true;
            }

            // before kickoff everyone walks straight to their spots. In a game every line-up places them; a change in
            // the huddle (someone joined or changed team) relabels the spots at once, so the job screens are right
            if (changed && Holding) W.LineUp();
            else if (changed) relabel = true;
            if (relabel && !Holding && W.Phase == Phase.Huddle)
            {
                relabel = false;
                foreach (var q in W.Players) Formation.Place(W, q);
            }
            foreach (var seat in fresh) ShowLobby(seat);
            if (changed) SendLineups();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.kKey.wasPressedThisFrame && Holding) KickOff();
        }

        // ---- the phone's presses (PartyHost passes on the message types it does not know) ----
        public void OnMessage(Seat seat, string t, JObject m)
        {
            if (!On || seat == null) return;
            if (t == "lobbyme") { if (Holding && HasBody(seat)) ShowLobby(seat); return; }
            if (t != "spot" && t != "swap" && t != "kickoff") return;
            if (seat.Team < 0) return;
            if (!Holding)
            {
                // after kickoff: only a spot or a swap, from this phone's huddle screen
                if (t == "kickoff" || !Switching || PhoneProtocol.Str(m, "aid") != host.AidOf(seat.Slot)) return;
            }
            else if (!aidOf.TryGetValue(seat.Slot, out var aid) || PhoneProtocol.Str(m, "aid") != aid) return;
            if (t == "spot") OnSpot(seat, PhoneProtocol.Str(m, "side") == "o", PhoneProtocol.Int(m, "spot", -1));
            else if (t == "swap") OnSwap(seat, m["yes"]?.Type == JTokenType.Boolean && (bool)m["yes"]);
            else KickOff();
        }

        void OnSpot(Seat seat, bool offense, int spot)
        {
            if (spot < 0 || spot >= Formation.SpotCount(W, offense)) return;
            var me = W.Find(seat.Slot);
            if (me == null || me.Team != seat.Team) return;
            var holder = Formation.Holder(W, seat.Team, offense, spot);
            if (holder == me) return;
            var other = holder != null ? PersonOf(holder) : null;
            if (other == null)
            {
                // a bot's spot: take it; the bot moves to whatever spot is free
                if (offense) seat.OffenseSpot = spot; else seat.DefenseSpot = spot;
            }
            else if (!other.Connected) notes[seat.Slot] = other.Name + " is away. Pick another spot.";
            else
            {
                asks[other.Slot] = new Ask { From = seat.Slot, Offense = offense, Spot = spot, Until = Time.unscaledTime + cfg.lobby.swapAskS };
                notes[seat.Slot] = "Asked " + other.Name + " to swap...";
            }
            SyncAndSend();
        }

        void OnSwap(Seat seat, bool yes)
        {
            if (!asks.TryGetValue(seat.Slot, out var ask)) return;
            asks.Remove(seat.Slot);
            var from = R[ask.From];
            var a = W.Find(ask.From);
            var b = W.Find(seat.Slot);
            if (from == null || a == null || b == null || a.Team != b.Team || from.Team != seat.Team) return;
            if (!yes) { notes[from.Slot] = seat.Name + " said no."; SyncAndSend(); return; }
            if (Formation.SpotOn(W, b, ask.Offense) != ask.Spot) { notes[from.Slot] = seat.Name + " moved. Try again."; SyncAndSend(); return; }
            int mine = Formation.SpotOn(W, a, ask.Offense);
            if (ask.Offense) { from.OffenseSpot = ask.Spot; seat.OffenseSpot = mine; }
            else { from.DefenseSpot = ask.Spot; seat.DefenseSpot = mine; }
            notes[from.Slot] = seat.Name + " swapped with you.";
            SyncAndSend();
        }

        // Kickoff: the game starts. A huddle opens at once if the game had already run (the lobby was switched on
        // mid-game); a fresh game opens its first huddle on its first tick.
        public void KickOff()
        {
            if (KickedOff) return;
            KickedOff = true;
            asks.Clear();
            SendLineups();
            if (W.HuddleNumber > 0 && Huddle.On(W)) Huddle.Begin(W);
            Debug.Log("[lobby] kickoff");
        }

        // Takes a bot's player on the team for a dropped person. Other dropped people count as people here, so the
        // bot is a real one.
        Player Restore(Seat seat)
        {
            var away = new List<Player>();
            foreach (var q in W.Players) if (!q.Human && covered.Contains(q.Slot) && q.Slot != seat.Slot) { q.Human = true; away.Add(q); }
            var p = seat.Team >= 0 ? W.ReplaceBot(seat.Team, seat.Slot) : null;
            foreach (var q in away) q.Human = false;
            if (p == null) return null;
            p.Human = false;
            p.OffenseSpot = seat.OffenseSpot; p.DefenseSpot = seat.DefenseSpot;
            Debug.Log("[lobby] " + seat.Name + " is away; a bot plays for them again");
            return p;
        }

        void SyncAndSend()
        {
            foreach (var seat in R.Seats)
            {
                var p = W.Find(seat.Slot);
                if (seat.Kind == SeatKind.Phone && p != null && p.Team == seat.Team) { p.OffenseSpot = seat.OffenseSpot; p.DefenseSpot = seat.DefenseSpot; }
            }
            if (Holding) W.LineUp();
            SendLineups();
            if (Switching)
            {
                // in the huddle: relabel the spots now and send every huddle screen again, so the jobs are right
                relabel = false;
                foreach (var q in W.Players) Formation.Place(W, q);
                for (int team = 0; team < cfg.teams.Count; team++) host.RefreshHuddle(team);
            }
        }

        // The huddle screen's line-up (FEATURES.huddleSwitch), sent with the huddle. Team-only, like the huddle.
        public void SendHuddleLineup(Seat seat)
        {
            if (Switching && seat.Conn >= 0 && seat.Team >= 0 && HasBody(seat)) host.Server.Send(seat.Conn, Lineup(seat));
        }

        // ---- what the phones see ----
        void ShowLobby(Seat seat)
        {
            if (seat.Conn < 0) return;
            string aid = "lobby" + (++aidCounter);
            aidOf[seat.Slot] = aid;
            host.Server.Send(seat.Conn, Messages.Ui("lobby", aid));
            host.Server.Send(seat.Conn, Lineup(seat));
        }

        void SendLineups()
        {
            foreach (var seat in R.Seats)
                if (seat.Kind == SeatKind.Phone && seat.Connected && seat.Conn >= 0 && seat.Team >= 0) host.Server.Send(seat.Conn, Lineup(seat));
        }

        // The person on this seat's team line-ups: who stands on every offense and defense spot. Team-only, like the
        // huddle: it is sent to this seat alone.
        public string Lineup(Seat seat)
        {
            var me = W.Find(seat.Slot);
            int team = seat.Team;
            var t = cfg.teams[team];
            var o = new JObject
            {
                ["t"] = "lineup", ["held"] = Holding, ["team"] = team, ["teamName"] = t.name, ["color"] = t.color, ["color2"] = t.color2,
                ["offense"] = Side(team, true, me), ["defense"] = Side(team, false, me),
                ["you"] = new JObject { ["o"] = me != null ? Formation.SpotOn(W, me, true) : -1, ["d"] = me != null ? Formation.SpotOn(W, me, false) : -1 },
                ["people"] = R.TeamCount(team, true), ["players"] = cfg.players.perTeam,
                ["switch"] = Switching,
            };
            if (asks.TryGetValue(seat.Slot, out var ask) && me != null)
            {
                var spots = ask.Offense ? cfg.formation.offense : cfg.formation.defense;
                var a = W.Find(ask.From);
                int give = Formation.SpotOn(W, me, ask.Offense);
                int get = a != null ? Formation.SpotOn(W, a, ask.Offense) : -1;
                o["ask"] = new JObject
                {
                    ["from"] = NameOf(ask.From), ["side"] = ask.Offense ? "o" : "d",
                    ["give"] = Label(spots, give), ["get"] = Label(spots, get),
                };
            }
            if (notes.TryGetValue(seat.Slot, out var note)) { o["line"] = note; notes.Remove(seat.Slot); }
            return PhoneProtocol.Text(o);
        }

        JArray Side(int team, bool offense, Player me)
        {
            var spots = offense ? cfg.formation.offense : cfg.formation.defense;
            var who = new Player[spots.Count];
            foreach (var p in W.Players)
            {
                if (p.Team != team) continue;
                int s = Formation.SpotOn(W, p, offense);
                if (s >= 0 && s < who.Length) who[s] = p;
            }
            var a = new JArray();
            for (int i = 0; i < spots.Count; i++)
            {
                var p = who[i];
                var person = p != null ? PersonOf(p) : null;
                a.Add(new JObject
                {
                    ["pos"] = spots[i].pos, ["number"] = spots[i].number,
                    ["who"] = person != null ? person.Name : "BOT", ["person"] = person != null,
                    ["me"] = p != null && p == me, ["away"] = person != null && !person.Connected,
                });
            }
            return a;
        }

        static string Label(List<SpotConfig> spots, int i) => i >= 0 && i < spots.Count ? spots[i].pos + " #" + spots[i].number : "a free spot";

        // The phone seat that plays this player, or null for a bot.
        Seat PersonOf(Player p)
        {
            var s = R[p.Slot];
            return s != null && s.Kind == SeatKind.Phone && s.Team == p.Team ? s : null;
        }

        bool HasBody(Seat seat)
        {
            var p = W.Find(seat.Slot);
            return p != null && seat.Team >= 0 && p.Team == seat.Team;
        }

        string NameOf(int slot) => R[slot]?.Name ?? "Someone";

        // ---- the TV: both teams' people and their two spots, while the game waits ----
        GUIStyle title, body;

        void OnGUI()
        {
            if (!Holding) return;
            float s = Screen.height / 1080f;
            title ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, richText = true };
            body ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            title.fontSize = Mathf.RoundToInt(40 * s);
            body.fontSize = Mathf.RoundToInt(24 * s);
            float w = Mathf.Min(Screen.width - 80 * s, 1300 * s), h = 560 * s, x = (Screen.width - w) * 0.5f - 160 * s, y = 180 * s;
            x = Mathf.Max(20 * s, x);
            GUI.color = new Color(0.05f, 0.13f, 0.1f, 0.88f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y + 16 * s, w, 56 * s), "LOBBY: pick your offense and defense spots on your phone", title);
            float col = (w - 60 * s) / 2f;
            for (int team = 0; team < cfg.teams.Count && team < 2; team++)
            {
                var t = cfg.teams[team];
                var lines = new List<string> { "<b><size=" + Mathf.RoundToInt(32 * s) + "><color=" + t.color + ">" + t.name + "</color></size></b>   " + R.TeamCount(team, true) + " people" };
                foreach (var p in W.Players)
                {
                    if (p.Team != team) continue;
                    var person = PersonOf(p);
                    if (person == null) continue;
                    int os = Formation.SpotOn(W, p, true), ds = Formation.SpotOn(W, p, false);
                    lines.Add(person.Name + (person.Connected ? "" : " (away, a bot plays)") + ":  " + Label(cfg.formation.offense, os) + "  ·  " + Label(cfg.formation.defense, ds));
                }
                lines.Add("<color=#a8c4b4>Every other spot: a bot</color>");
                GUI.Label(new Rect(x + 20 * s + team * (col + 20 * s), y + 90 * s, col, h - 150 * s), string.Join("\n", lines), body);
            }
            GUI.Label(new Rect(x, y + h - 52 * s, w, 44 * s), "Any phone taps KICK OFF to start", title);
        }
    }
}
