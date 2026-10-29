using System.Collections.Generic;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Boot
{
    // WS-14 Xbox pads (FEATURES.gamepad, NO_GAMEPAD=1 turns it off). A pad that presses START joins as a person, like
    // a phone scanning the code: it gets a seat in the roster (PAD 1, PAD 2...), LB / RB pick a team, and from then
    // on its buttons press the phone buttons of the moment (Input/GamepadSource.cs, the gamepad block in game.json).
    // An unplugged pad keeps its seat; plugged back in, it is the same person again.
    public sealed class GamepadHost : MonoBehaviour
    {
        const int BotSlotBase = 100;     // the same bot slots PartyHost uses

        public static GamepadHost Instance { get; private set; }

        PartyHost host;
        GameConfig cfg;
        readonly Dictionary<int, GamepadSource> pads = new Dictionary<int, GamepadSource>();   // by device id
        readonly Dictionary<int, float> findUntil = new Dictionary<int, float>();              // by slot
        int joined;

        // A team switch in the lobby builds the scene again (PartyHost.SwitchTeams): the pads keep their seats.
        static Dictionary<int, Seat> carried;
        static int carriedJoined;
        Dictionary<int, Seat> kept;   // the carried pads not seen yet

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCarry() { carried = null; carriedJoined = 0; }

        public void CarryPads()
        {
            carried = new Dictionary<int, Seat>();
            foreach (var kv in pads) carried[kv.Key] = kv.Value.Seat;
            carriedJoined = joined;
        }

        public static GamepadHost Attach(PartyHost h, GameConfig cfg)
        {
            if (!cfg.Feature("gamepad")) return null;
            var g = h.gameObject.AddComponent<GamepadHost>();
            g.host = h;
            g.cfg = cfg;
            if (carried != null) { g.joined = carriedJoined; g.kept = carried; carried = null; }
            h.BeforeTick += g.Feed;
            Instance = g;
            Debug.Log("[pad] gamepads on: press " + cfg.gamepad.join.ToUpperInvariant() + " to join");
            return g;
        }

        void OnDestroy()
        {
            if (host != null) host.BeforeTick -= Feed;
            if (Instance == this) Instance = null;
        }

        World W => host.World;

        void Update()
        {
            if (host.Photo != null) return;
            float now = Time.unscaledTime;
            var seen = new HashSet<int>();
            foreach (var g in Gamepad.all)
            {
                seen.Add(g.deviceId);
                var s = PadState.Read(g);
                if (!pads.TryGetValue(g.deviceId, out var pad))
                {
                    if (kept != null && kept.TryGetValue(g.deviceId, out var keptSeat))
                    {
                        // a pad from before the team switch: the same person, no START needed
                        kept.Remove(g.deviceId);
                        var back = new GamepadSource(keptSeat, cfg.gamepad, cfg.play.shapes) { DeviceId = g.deviceId };
                        back.Prime(s);
                        pads[g.deviceId] = back;
                        continue;
                    }
                    int j = System.Array.IndexOf(PadState.Names, cfg.gamepad.join);
                    if (j >= 0 && s.Has((PadButton)j)) Join(g.deviceId, s);
                    continue;
                }
                if (!pad.Seat.Connected) { pad.Seat.Connected = true; Debug.Log("[pad] " + pad.Seat.Name + " is back"); }
                pad.Feed(s, GamepadSource.LayoutFor(W, pad.Seat), now);
                while (pad.TakeCommand(out var cmd)) Command(pad, cmd);
                if (pad.StickX != 0f && host.World != null) GamepadSource.KickSteer(W, pad.Slot, pad.StickX, Time.unscaledDeltaTime);   // FEATURES.kickArc
                if (pad.TakeFind()) findUntil[pad.Slot] = now + cfg.view.findMeS;
            }
            foreach (var pad in pads.Values)
                if (!seen.Contains(pad.DeviceId) && pad.Seat.Connected)
                {
                    host.Roster.Disconnect(pad.Seat);
                    pad.OnDisconnect();
                    Debug.Log("[pad] " + pad.Seat.Name + " unplugged; the seat waits for it");
                }
        }

        void Join(int deviceId, PadState s)
        {
            var seat = GamepadSource.Join(host.Roster, cfg.gamepad, joined + 1);
            if (seat == null) { Debug.LogWarning("[pad] the game is full"); return; }
            joined++;
            var pad = new GamepadSource(seat, cfg.gamepad, cfg.play.shapes) { DeviceId = deviceId };
            pad.Prime(s);                 // (the START that joined it does nothing more)
            pads[deviceId] = pad;
            Debug.Log("[pad] " + seat.Name + " joined, slot " + seat.Slot);
            BroadcastLobby();
        }

        void Command(GamepadSource pad, string cmd)
        {
            if (cmd.StartsWith("team:", System.StringComparison.Ordinal))
            {
                if (!int.TryParse(cmd.Substring(5), out int team)) return;
                var p = GamepadSource.Pick(W, pad.Seat, team, cfg.teams.Count, BotSlotBase);
                if (p == null) { Debug.Log("[pad] " + pad.Seat.Name + ": " + (team < cfg.teams.Count ? cfg.teams[team].name : "?") + " is full of people"); return; }
                Debug.Log("[pad] " + pad.Seat.Name + " picked " + cfg.teams[team].name + " " + p.Pos + " #" + p.Number);
                BroadcastLobby();
            }
            else if (cmd == "agree")
            {
                var p = W.Find(pad.Slot);
                if (p == null || !GamepadSource.Agree(W, pad.Slot)) return;
                // the team's phones see the thumbs (team-only, like a phone's suggest); everyone sees CALLED
                foreach (var s in host.Roster.Seats)
                {
                    var q = W.Find(s.Slot);
                    if (s.Conn >= 0 && s.Connected && q != null && q.Team == p.Team && s.Team == p.Team) host.Server.Send(s.Conn, Messages.ThumbsFor(W, p.Team, q));
                }
                if (Huddle.IsLocked(W, p.Team)) Broadcast(Messages.Status(W));
            }
            else if (cmd == "kick") GamepadSource.Kick(W, pad.Slot, pad.KickPower, pad.KickAim);
            else if (cmd == "kicktap") GamepadSource.KickTap(W, pad.Slot);   // FEATURES.kickSweep: A sets the aim, then the power (kickArc: snap, power, pitch)
            else if (cmd == "kickoff" && W.Tick == 0)
                host.gameObject.SendMessage("KickOff", SendMessageOptions.DontRequireReceiver);   // the lobby (WS-07), if there is one
        }

        // Before each tick: every pad's input, next to the phones'.
        void Feed(Dictionary<int, PlayerInput> inputs)
        {
            foreach (var pad in pads.Values) inputs[pad.Slot] = pad.Poll();
        }

        void BroadcastLobby() => Broadcast(Messages.Lobby(cfg, host.Roster));

        void Broadcast(string msg)
        {
            if (host.Server == null) return;
            foreach (var s in host.Roster.Seats) if (s.Kind == SeatKind.Phone && s.Connected && s.Conn >= 0) host.Server.Send(s.Conn, msg);
        }

        // ---- the TV: how to join and pick, and a pad's FIND ME ----
        GUIStyle line, plate;

        void OnGUI()
        {
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            if (host.Photo != null) return;
            if (Lobby.Instance != null && Lobby.Instance.FullScreen) GUI.depth = -60;   // (over the join screen)
            float s = Screen.height / 1080f;
            line ??= new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.LowerLeft };
            plate ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            line.fontSize = Mathf.RoundToInt(cfg.gamepad.hintFontPx * s);
            var lines = new List<string>();
            int waiting = 0;
            foreach (var g in Gamepad.all) if (!pads.ContainsKey(g.deviceId)) waiting++;
            if (waiting > 0) lines.Add("Controller: press <b>" + cfg.gamepad.join.ToUpperInvariant() + "</b> to join");
            foreach (var pad in pads.Values)
            {
                if (!pad.Seat.Connected) lines.Add(pad.Seat.Name + ": unplugged");
                else if (pad.Seat.Team < 0) lines.Add(pad.Seat.Name + ": " + TeamKeys());
            }
            if (lines.Count > 0)
            {
                string text = string.Join("\n", lines);
                var size = line.CalcSize(new GUIContent(text));
                var r = new Rect(20 * s, Screen.height - size.y - 30 * s, size.x + 24 * s, size.y + 12 * s);
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 12 * s, r.y + 6 * s, size.x, size.y), text, line);
            }

            var cam = Camera.main;
            if (cam == null) return;
            float now = Time.unscaledTime;
            foreach (var pad in pads.Values)
            {
                if (!findUntil.TryGetValue(pad.Slot, out float until) || until <= now) continue;
                var p = W.Find(pad.Slot);
                if (p == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(host.Players.HeadPosition(p.Slot) + Vector3.up * 1.1f);
                if (sp.z <= 0) continue;
                plate.fontSize = Mathf.RoundToInt(cfg.view.plateFontPx * s);
                var size = plate.CalcSize(new GUIContent(pad.Seat.Name));
                var r = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y);
                GUI.color = Colors.Hex(cfg.teams[p.Team].color);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, pad.Seat.Name, plate);
            }
        }

        // "LB = Arrows, RB = Grains", from the pick mapping.
        string TeamKeys()
        {
            var parts = new List<string>();
            if (cfg.gamepad.layouts.TryGetValue("pick", out var map))
                foreach (var kv in map)
                    if (kv.Value.StartsWith("team:", System.StringComparison.Ordinal) && int.TryParse(kv.Value.Substring(5), out int t) && t >= 0 && t < cfg.teams.Count)
                        parts.Add(kv.Key.ToUpperInvariant() + " = " + cfg.teams[t].name);
            return string.Join(", ", parts);
        }
    }
}
