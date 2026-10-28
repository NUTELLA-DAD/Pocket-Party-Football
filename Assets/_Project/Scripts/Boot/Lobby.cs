using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PocketFootball.Sim;
using PocketFootball.View;
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
    // lineup then carries switch: true, and once a spot changes every huddle screen of that team is sent again.
    // FEATURES.lobbyScreen (NO_LOBBY_SCREEN=1: the old box and the small join panel): while the game waits, the TV shows
    // the join screen: a big QR code and room code, and a card for each side with its emblem and its people. With
    // FEATURES.teamPick and more than two teams in the league, the cards' arrows (or the keyboard) pick the teams too;
    // teamApplyS after the last press the stadium is built again with them (PartyHost.SwitchTeams; the phones stay).
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
            l.shots = !string.IsNullOrEmpty(args.ShotsDir) && !args.Lobby;   // (-lobby: a screenshot of the join screen)
            for (int side = 0; side < 2; side++)
                l.shown[side] = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > side ? cfg.teams[side].@short : null));
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
            if (!FullScreen || !CanPick || switching) return;
            if (applyFrame >= 0)
            {
                // (one frame later: the "bringing out the teams" card is on the TV while the stadium is built)
                if (Time.frameCount > applyFrame + 1)
                {
                    switching = true;
                    var league = TeamPick.League(cfg);
                    host.SwitchTeams(league[shown[0]].@short, league[shown[1]].@short);
                }
                return;
            }
            PickInput(kb);
            if (pickedAt >= 0f && Time.unscaledTime - pickedAt >= cfg.lobby.teamApplyS) { pickedAt = -1f; applyFrame = Time.frameCount; }
        }

        // ---- picking the teams on the TV (FEATURES.lobbyScreen + teamPick) ----
        public bool FullScreen => Holding && cfg.Feature("lobbyScreen");
        // (not in a Pocket Cup game: the bracket picked its two teams, CupHost.cs)
        bool CanPick => cfg.Feature("teamPick") && TeamPick.League(cfg).Count > 2 && cfg.teams.Count >= 2 && CupHost.Live == null;
        bool Waiting => pickedAt >= 0f || applyFrame >= 0 || switching;   // a new team is on its way: no kickoff yet

        readonly int[] shown = new int[2];      // the league team on each card (away, home); differs from cfg.teams while one waits
        int focus;                              // the card the keyboard changes: 0 away, 1 home
        float pickedAt = -1f;                   // the last press (-1: nothing waiting)
        int applyFrame = -1;                    // the frame the waiting pick was taken (-1: none)
        bool switching;
        readonly Rect[] arrows = new Rect[4];   // away ◄ ►, home ◄ ► (GUI space), for the mouse
        readonly Rect[] cards = new Rect[2];

        void PickInput(Keyboard kb)
        {
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame
                    || kb.sKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) focus = 1 - focus;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Change(focus, -1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Change(focus, +1);
            }
            var mouse = Mouse.current;
            if (mouse == null) return;
            var mp = mouse.position.ReadValue();
            mp.y = Screen.height - mp.y;   // GUI space: y goes down
            if (mouse.delta.ReadValue().sqrMagnitude > 0f)
                for (int side = 0; side < 2; side++) if (cards[side].Contains(mp)) focus = side;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            for (int i = 0; i < arrows.Length; i++)
                if (arrows[i].Contains(mp)) { focus = i / 2; Change(i / 2, i % 2 == 0 ? -1 : +1); }
        }

        // The next or last team on one card (never the other card's team). Back to the two playing now: nothing waits.
        void Change(int side, int dir)
        {
            var league = TeamPick.League(cfg);
            shown[side] = TeamPick.Step(cfg, shown[side], shown[1 - side], dir);
            bool same = league[shown[0]] == cfg.teams[0] && league[shown[1]] == cfg.teams[1];
            pickedAt = same ? -1f : Time.unscaledTime;
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
            if (KickedOff || Waiting) return;
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
            var moved = new HashSet<int>();   // teams where someone's spots changed
            foreach (var seat in R.Seats)
            {
                var p = W.Find(seat.Slot);
                if (seat.Kind != SeatKind.Phone || p == null || p.Team != seat.Team) continue;
                if (p.OffenseSpot != seat.OffenseSpot || p.DefenseSpot != seat.DefenseSpot) moved.Add(p.Team);
                p.OffenseSpot = seat.OffenseSpot; p.DefenseSpot = seat.DefenseSpot;
            }
            if (Holding) W.LineUp();
            SendLineups();
            if (Switching && moved.Count > 0)
            {
                // in the huddle: relabel the spots now and send that team's huddle screens again, so the jobs are right.
                // Only when a spot changed: a new huddle screen has a new aid, and an open swap question must still answer
                relabel = false;
                foreach (var q in W.Players) Formation.Place(W, q);
                foreach (int team in moved) host.RefreshHuddle(team);
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
            if (cfg.Feature("lobbyScreen")) { JoinScreen(); return; }
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
                foreach (var line in PeopleLines(team)) lines.Add(line);
                lines.Add("<color=#a8c4b4>Every other spot: a bot</color>");
                GUI.Label(new Rect(x + 20 * s + team * (col + 20 * s), y + 90 * s, col, h - 150 * s), string.Join("\n", lines), body);
            }
            GUI.Label(new Rect(x, y + h - 52 * s, w, 44 * s), "Any phone taps KICK OFF to start", title);
        }

        // Each person on the team: their name and their two spots.
        List<string> PeopleLines(int team)
        {
            var lines = new List<string>();
            foreach (var p in W.Players)
            {
                if (p.Team != team) continue;
                var person = PersonOf(p);
                if (person == null) continue;
                int os = Formation.SpotOn(W, p, true), ds = Formation.SpotOn(W, p, false);
                lines.Add(person.Name + (person.Connected ? "" : " (away, a bot plays)") + ":  " + Label(cfg.formation.offense, os) + "  ·  " + Label(cfg.formation.defense, ds));
            }
            return lines;
        }

        // ---- FEATURES.lobbyScreen: the join screen, in the title screen's navy and gold ----
        static readonly Color Navy = new Color(0.04f, 0.09f, 0.22f, 0.95f), NavyInk = new Color(0.04f, 0.09f, 0.22f, 1f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.12f, 1f), GoldText = new Color(1f, 0.82f, 0.2f, 1f), Muted = new Color(0.66f, 0.72f, 0.89f, 1f);
        readonly Dictionary<string, Texture2D> emblems = new Dictionary<string, Texture2D>();
        GUIStyle big, mid, small, list, arrow;

        // The team card picture: Resources/Emblems/<SHORT>.png (the emblem with the team name under it, from
        // tools/teams/wordmarks.py), else null (the card shows the team mark and the name).
        Texture2D Emblem(TeamConfig t)
        {
            if (!emblems.TryGetValue(t.@short, out var tex)) emblems[t.@short] = tex = Resources.Load<Texture2D>("Emblems/" + t.@short);
            return tex;
        }

        void JoinScreen()
        {
            GUI.depth = -50;   // over the game's own overlays
            float sw = Screen.width, sh = Screen.height, s = sh / 1080f;
            var lc = cfg.lobby;
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            mid ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true, wordWrap = true };
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            list ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true, wordWrap = false, clipping = TextClipping.Clip };
            arrow ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            Box(new Rect(0, 0, sw, sh), new Color(0f, 0f, 0f, lc.tint), 0f);
            if (applyFrame >= 0 || switching)
            {
                // the stadium is being built again with the new teams (a second or two)
                var league = TeamPick.League(cfg);
                Box(new Rect(0, 0, sw, sh), new Color(0f, 0f, 0f, 0.85f), 0f);
                Text(new Rect(0, sh * 0.42f, sw, 90 * s), "BRINGING OUT THE " + league[shown[0]].name + " AND THE " + league[shown[1]].name + "...", big, 52 * s, GoldText);
                Text(new Rect(0, sh * 0.42f + 90 * s, sw, 50 * s), "Phones stay connected.", small, 28 * s, Color.white);
                return;
            }

            float margin = 56 * s, top = 132 * s, bottom = 150 * s;
            Text(new Rect(0, 26 * s, sw, 80 * s), "JOIN THE GAME", big, 64 * s, GoldText);
            Text(new Rect(0, 92 * s, sw, 36 * s), "Scan the code with your phone (same Wi-Fi as the TV), then pick your side", small, 26 * s, Color.white);

            // the QR card, left
            float qr = Mathf.Min(sh * lc.qrFrac, (sh - top - bottom) - 300 * s), pad = 28 * s;
            var qc = new Rect(margin, top + 10 * s, qr + 2 * pad, sh - top - bottom);
            Card(qc, s, false);
            var qrRect = new Rect(qc.x + pad, qc.y + pad, qr, qr);
            Box(new Rect(qrRect.x - 8 * s, qrRect.y - 8 * s, qr + 16 * s, qr + 16 * s), Color.white, 14 * s);
            if (host.Qr != null) GUI.DrawTexture(qrRect, host.Qr);
            float y = qrRect.yMax + 20 * s;
            if (host.ServerError != null)
                Text(new Rect(qc.x + 12 * s, y, qc.width - 24 * s, 160 * s), "Phone server failed:\n" + host.ServerError, mid, 24 * s, new Color(1f, 0.5f, 0.5f));
            else
            {
                Text(new Rect(qc.x, y, qc.width, 44 * s), "SCAN TO JOIN", big, 36 * s, GoldText);
                Text(new Rect(qc.x, y + 44 * s, qc.width, 32 * s), "or open <b>" + host.Url.Replace("http://", "") + "</b>", small, 24 * s, Color.white);
                Text(new Rect(qc.x, y + 86 * s, qc.width, 30 * s), "ROOM CODE", small, 22 * s, Muted);
                var code = new Rect(qc.center.x - 130 * s, y + 118 * s, 260 * s, 84 * s);
                Box(code, Gold, 42 * s);
                Box(new Rect(code.x + 5 * s, code.y + 5 * s, code.width - 10 * s, code.height - 10 * s), NavyInk, 37 * s);
                Text(code, host.RoomCode, big, 60 * s, Color.white);
            }

            // the two team cards, right, VS between them
            float areaX = qc.xMax + 44 * s, areaW = sw - margin - areaX, vs = 110 * s;
            float cw = (areaW - vs) * 0.5f;
            bool pick = CanPick;
            for (int side = 0; side < 2 && side < cfg.teams.Count; side++)
            {
                var r = new Rect(areaX + side * (cw + vs), qc.y, cw, qc.height);
                cards[side] = r;
                TeamCard(side, r, s, pick);
            }
            // (level with the middle of the emblems)
            float vsY = qc.y + 5 * s + 60 * s + 22 * s + Mathf.Min(sh * lc.emblemFrac, qc.height * 0.45f) * 0.5f;
            Text(new Rect(areaX + cw, vsY - 60 * s, vs, 120 * s), "<i>VS</i>", big, 64 * s, GoldText, true);

            // kick off, bottom
            var ko = new Rect(sw * 0.5f - 430 * s, sh - bottom + 34 * s, 860 * s, 70 * s);
            Box(ko, Gold, 35 * s);
            Text(ko, "ANY PHONE TAPS  <b>KICK OFF</b>  TO START", big, 34 * s, NavyInk);
            string keys = pick ? "TV keyboard: ◄ ► change team  ·  ▲ ▼ away or home  ·  K kick off      Mouse: click the arrows" : "TV keyboard: K kick off";
            small.fontSize = Mathf.RoundToInt(23 * s);
            float kw = small.CalcSize(new GUIContent(keys)).x + 40 * s;
            var kr = new Rect((sw - kw) * 0.5f, ko.yMax + 6 * s, kw, 34 * s);
            Box(kr, new Color(0f, 0f, 0f, 0.7f), 17 * s);   // (a dark strip: readable over the field)
            Text(kr, keys, small, 23 * s, Color.white);
        }

        void TeamCard(int side, Rect r, float s, bool pick)
        {
            var lc = cfg.lobby;
            var league = TeamPick.League(cfg);
            var t = pick ? league[shown[side]] : cfg.teams[side];
            bool waiting = t != cfg.teams[side];
            bool focused = pick && side == focus;
            Color tc = Colors.Hex(t.color), tc2 = Colors.Hex(t.color2);
            Card(r, s, focused);

            // the band: AWAY / HOME in the team's colours, the head count at the right
            float bh = 60 * s, inset = focused ? 8 * s : 5 * s;
            var band = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, bh);
            Box(band, tc, 16 * s);
            Box(new Rect(band.x, band.yMax - 16 * s, band.width, 16 * s), tc, 0f);
            Box(new Rect(band.x, band.yMax, band.width, 6 * s), tc2, 0f);
            big.alignment = TextAnchor.MiddleLeft;
            Text(new Rect(band.x + 20 * s, band.y, band.width * 0.5f, bh), side == 0 ? "AWAY" : "HOME", big, 30 * s, Color.white, true);
            big.alignment = TextAnchor.MiddleRight;
            int people = R.TeamCount(side, true);
            Text(new Rect(band.center.x, band.y, band.width * 0.5f - 20 * s, bh), people + (people == 1 ? " PERSON" : " PEOPLE"), big, 24 * s, Color.white, true);
            big.alignment = TextAnchor.MiddleCenter;

            // the emblem, with the arrows either side when the teams can change
            float aw = pick ? 66 * s : 0f, eh = Mathf.Min(Screen.height * lc.emblemFrac, r.height * 0.45f);
            var er = new Rect(r.x + 20 * s + aw, band.yMax + 22 * s, r.width - 40 * s - 2 * aw, eh);
            var tex = Emblem(t);
            if (tex != null) GUI.DrawTexture(er, tex, ScaleMode.ScaleToFit);
            else
            {
                var mark = TeamMarks.For(t);
                if (mark != null) GUI.DrawTexture(new Rect(er.x, er.y, er.width, er.height * 0.72f), mark, ScaleMode.ScaleToFit);
                Text(new Rect(er.x, er.y + er.height * 0.72f, er.width, er.height * 0.28f), t.name, big, 48 * s, tc, true);
            }
            if (pick)
                for (int k = 0; k < 2; k++)
                {
                    var a = new Rect(k == 0 ? r.x + 16 * s : r.xMax - 16 * s - aw, er.center.y - aw * 0.5f, aw, aw);
                    arrows[side * 2 + k] = a;
                    Box(a, focused ? Gold : new Color(1f, 1f, 1f, 0.16f), aw * 0.5f);
                    Text(a, k == 0 ? "◄" : "►", arrow, 34 * s, focused ? NavyInk : Color.white);
                }
            else { arrows[side * 2] = Rect.zero; arrows[side * 2 + 1] = Rect.zero; }

            // under the emblem: the new team on its way, or who plays
            float y = er.yMax + 14 * s, x = r.x + 30 * s, w = r.width - 60 * s;
            if (waiting)
            {
                Text(new Rect(x, y, w, 40 * s), "NEW TEAM: THE " + t.name, big, 28 * s, GoldText);
                Text(new Rect(x, y + 40 * s, w, 34 * s), "Coming out in a moment...", small, 22 * s, Color.white);
                return;
            }
            Box(new Rect(x, y, w, 3 * s), new Color(tc2.r, tc2.g, tc2.b, 0.8f), 0f);
            y += 14 * s;
            var lines = PeopleLines(side);
            int rows = Mathf.Max(1, lc.listRows);
            float lh = 34 * s;
            if (lines.Count == 0)
                Text(new Rect(x, y, w, 64 * s), "Nobody yet. Bots play every spot.\nPick this side on your phone!", small, 26 * s, new Color(0.88f, 0.91f, 1f));
            for (int i = 0; i < lines.Count && i < rows; i++)
            {
                bool more = i == rows - 1 && lines.Count > rows;
                Text(new Rect(x, y + i * lh, w, lh), more ? "+ " + (lines.Count - rows + 1) + " more" : lines[i], list, 22 * s, more ? Muted : Color.white, true);
            }
            if (lines.Count > 0 && lines.Count < rows)
                Text(new Rect(x, y + lines.Count * lh, w, lh), "Every other spot: a bot", list, 20 * s, Muted, true);
        }

        // A navy card with a gold rim (thicker on the card the keyboard changes), like the title screen's panels.
        void Card(Rect r, float s, bool lit)
        {
            Box(new Rect(r.x, r.y + 8 * s, r.width, r.height), new Color(0f, 0f, 0f, 0.45f), 22 * s);   // drop shadow
            Box(r, Navy, 22 * s);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Gold, lit ? 8 * s : 4 * s, 22 * s);
        }

        // A filled box, rounded by radius (0: square).
        static void Box(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        static void Text(Rect r, string text, GUIStyle st, float size, Color c, bool shadow = false)
        {
            st.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            if (shadow)
            {
                st.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            }
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }
    }
}
