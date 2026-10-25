using System.Collections.Generic;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Boot
{
    // Runs the game: the phone server, the roster, the 30 Hz sim and the players' view.
    // Both teams start full of bots on their formation spots; a phone that picks a team takes over a bot's spot.
    // Dev keys: L lines everyone up again, T throws the ball downfield (lob; Shift = bullet), B makes the bots wander.
    public sealed class PartyHost : MonoBehaviour
    {
        const int BotSlotBase = 100;     // bots' slots in the sim (phones use the roster's slots 0..maxPlayers-1)

        // Tests: listen here instead of the config's address and port (port 0 = any free port).
        public static IPAddress BindOverride;
        public static int PortOverride = -1;

        public static PartyHost Instance { get; private set; }
        public World World { get; private set; }
        public Roster Roster { get; private set; }
        public PhoneServer Server { get; private set; }
        public string RoomCode { get; private set; }
        public string Url { get; private set; }
        public string ServerError { get; private set; }
        public PlayersView Players { get; private set; }

        GameConfig cfg;
        CameraRig rig;
        bool lockstep;               // screenshots: exactly one sim tick per frame, so pictures are repeatable
        float accumulator;
        readonly Dictionary<int, PhoneSource> sources = new Dictionary<int, PhoneSource>();   // by slot
        readonly Dictionary<int, PhoneSource> byConn = new Dictionary<int, PhoneSource>();
        readonly Dictionary<int, PlayerInput> inputs = new Dictionary<int, PlayerInput>();
        readonly Dictionary<int, float> findUntil = new Dictionary<int, float>();
        readonly Dictionary<int, string> lastMe = new Dictionary<int, string>();
        float nextMeAt;
        Texture2D qrTex;
        int aidCounter;

        public static PartyHost Create(GameObject on, GameConfig cfg, Material baseMat, CameraRig rig, CliArgs args)
        {
            var h = on.AddComponent<PartyHost>();
            Instance = h;
            h.cfg = cfg;
            h.rig = rig;
            h.lockstep = !string.IsNullOrEmpty(args.ShotsDir);
            h.World = new World(cfg, args.Seed);
            h.Roster = new Roster(cfg.net.maxPlayers);
            h.Players = new PlayersView(cfg, baseMat);
            h.StartServer(args.Bind);
            h.FillWithBots();
            if (args.Wander) h.World.BotsWander = true;
            if (!args.LookX.HasValue) rig.SetLookAtX(h.World.LineOfScrimmageX);
            if (args.ThrowDemo.HasValue) h.World.Throw(h.World.Ball.X + cfg.formation.attackDir * args.ThrowDemo.Value, h.World.Ball.Z, true);
            for (int i = 0; i < args.WarmupTicks; i++) h.StepOnce();
            return h;
        }

        // Every formation spot nobody has is a bot.
        void FillWithBots()
        {
            for (int team = 0; team < cfg.teams.Count; team++)
                while (World.TeamCount(team) < cfg.players.perTeam)
                    if (World.AddPlayer(FreeBotSlot(), team, false) == null) break;
        }

        int FreeBotSlot()
        {
            int slot = BotSlotBase;
            while (World.Find(slot) != null) slot++;
            return slot;
        }

        void StartServer(string bindArg)
        {
            var n = cfg.net;
            RoomCode = Roster.NewRoomCode(n.codeLetters, n.codeLength, new System.Random());
            IPAddress bind = BindOverride
                ?? (!string.IsNullOrEmpty(bindArg) ? IPAddress.Parse(bindArg)
                : (!string.IsNullOrEmpty(n.bindAddress) ? IPAddress.Parse(n.bindAddress) : IPAddress.Any));
            int port = PortOverride >= 0 ? PortOverride : n.port;
            Server = new PhoneServer(Path.Combine(Application.streamingAssetsPath, "phone"));
            if (!Server.Start(bind, port, n.portTries)) { ServerError = Server.Error; Debug.LogWarning("[net] phone server failed: " + ServerError); }
            string host = bind.Equals(IPAddress.Loopback) ? "127.0.0.1" : LanAddress.Best();
            Url = "http://" + host + ":" + Server.Port;
            qrTex = MakeQr(Url + "/join/" + RoomCode);
            Debug.Log("[net] phones join at " + Url + "/join/" + RoomCode + " (bound " + bind + ")");
        }

        void OnDestroy()
        {
            Server?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            PumpNet();
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame) World.BotsWander = !World.BotsWander;
                if (kb.lKey.wasPressedThisFrame) World.LineUp();
                if (kb.tKey.wasPressedThisFrame)
                {
                    if (World.Ball.State != BallState.Held) World.LineUp();
                    World.Throw(World.Ball.X + cfg.formation.attackDir * cfg.bots.wanderYd * 2f, World.Ball.Z, !kb.shiftKey.isPressed);
                }
            }

            float dt = World.Dt;
            int ticks = 0;
            if (lockstep) { StepOnce(); accumulator = 0f; }
            else
            {
                accumulator += Time.deltaTime;
                while (accumulator >= dt && ticks < cfg.sim.maxTicksPerFrame) { StepOnce(); accumulator -= dt; ticks++; }
                if (ticks == cfg.sim.maxTicksPerFrame) accumulator = 0f;
            }
            Players.Render(World, lockstep ? 1f : Mathf.Clamp01(accumulator / dt));
        }

        void StepOnce()
        {
            float now = Time.unscaledTime;
            inputs.Clear();
            foreach (var kv in sources)
            {
                var src = kv.Value;
                src.ExpireStick(now, cfg.net.stickStaleS);
                inputs[kv.Key] = src.Poll();
                if (src.TakeFind()) findUntil[kv.Key] = now + cfg.view.findMeS;
            }
            Players.BeforeStep(World);
            World.Step(inputs);
            SendMe(now);
        }

        // Each phone's own numbers (stamina, out of bounds), at most meSendHz and only when they change.
        void SendMe(float now)
        {
            if (now < nextMeAt || Server == null) return;
            nextMeAt = now + 1f / cfg.view.meSendHz;
            foreach (var s in Roster.Seats)
            {
                if (!s.Connected || s.Conn < 0) continue;
                var p = World.Find(s.Slot);
                if (p == null) continue;
                string msg = Messages.Me(p);
                if (lastMe.TryGetValue(s.Slot, out var was) && was == msg) continue;
                lastMe[s.Slot] = msg;
                Server.Send(s.Conn, msg);
            }
        }

        // ---- phones ----
        void PumpNet()
        {
            if (Server == null) return;
            while (Server.TryDequeue(out var e))
            {
                if (e.Kind == NetEventKind.Close)
                {
                    if (byConn.TryGetValue(e.Conn, out var ps))
                    {
                        byConn.Remove(e.Conn);
                        if (ps.Seat.Conn == e.Conn) { Roster.Disconnect(ps.Seat); ps.OnDisconnect(); }
                    }
                    continue;
                }
                if (e.Kind != NetEventKind.Message) continue;
                var m = PhoneProtocol.Parse(e.Text);
                if (m == null) continue;
                string t = PhoneProtocol.Type(m);
                if (t == "join") OnJoin(e.Conn, m);
                else if (t == "ping") Server.Send(e.Conn, PhoneProtocol.Pong(PhoneProtocol.Int(m, "c")));
                else if (byConn.TryGetValue(e.Conn, out var src))
                {
                    if (t == "stick") src.OnStick(PhoneProtocol.Float(m, "x", -1f, 1f), PhoneProtocol.Float(m, "y", -1f, 1f), Time.unscaledTime);
                    else if (t == "btn") src.OnButton(PhoneProtocol.Str(m, "b"), PhoneProtocol.Int(m, "d") == 1, PhoneProtocol.Str(m, "aid"));
                    else if (t == "pick") OnPick(src, PhoneProtocol.Int(m, "team", -1));
                }
            }
        }

        void OnJoin(int conn, JObject m)
        {
            string code = PhoneProtocol.Str(m, "code").Trim().ToUpperInvariant();
            if (code != RoomCode) { Server.Send(conn, PhoneProtocol.Err("Wrong room code. Check the TV.")); return; }
            var seat = Roster.JoinPhone(conn, PhoneProtocol.Str(m, "name"), PhoneProtocol.Str(m, "token"), PhoneProtocol.Str(m, "pid"), out bool rejoined);
            if (seat == null) { Server.Send(conn, PhoneProtocol.Err("The game is full (" + Roster.Capacity + " people).")); return; }
            if (!sources.TryGetValue(seat.Slot, out var src)) { src = new PhoneSource(seat); sources[seat.Slot] = src; }
            byConn[conn] = src;
            lastMe.Remove(seat.Slot);
            Server.Send(conn, Messages.Joined(seat, RoomCode, cfg, Roster));
            var mine = World.Find(seat.Slot);
            if (seat.Team >= 0 && mine != null)
            {
                Server.Send(conn, Messages.Picked(seat, mine, cfg));
                SendUi(src, "walk");
            }
            else SendUi(src, "pick");
            Debug.Log("[net] " + (rejoined ? "rejoined " : "joined ") + seat.Name + " slot " + seat.Slot + " team " + seat.Team);
            BroadcastLobby();
        }

        void OnPick(PhoneSource src, int team)
        {
            if (team < 0 || team >= cfg.teams.Count) return;
            var seat = src.Seat;
            var p = World.Find(seat.Slot);
            if (p == null || p.Team != team)
            {
                // take a bot's spot on the new team; the spot left behind goes back to a bot
                bool had = p != null;
                int oldTeam = had ? p.Team : -1, oldSpot = had ? p.Spot : -1;
                float ox = had ? p.X : 0f, oz = had ? p.Z : 0f;
                var taken = World.ReplaceBot(team, seat.Slot);
                if (taken == null)
                {
                    Server.Send(seat.Conn, PhoneProtocol.Err(cfg.teams[team].name + " is full of people. Pick the other side."));
                    return;
                }
                p = taken;
                if (had) BotBack(oldTeam, oldSpot, ox, oz);
            }
            seat.Team = team;
            Server.Send(seat.Conn, Messages.Picked(seat, p, cfg));
            SendUi(src, "walk");
            Debug.Log("[net] " + seat.Name + " picked " + cfg.teams[team].name + " " + p.Pos + " #" + p.Number);
            BroadcastLobby();
        }

        // A person left a spot (changed team): a bot takes it, standing where they stood.
        void BotBack(int team, int spot, float x, float z)
        {
            var bot = World.AddPlayer(FreeBotSlot(), team, false, spot);
            if (bot != null) { bot.X = x; bot.Z = z; }
        }

        void SendUi(PhoneSource src, string layout)
        {
            src.Aid = layout + (++aidCounter);
            if (src.Seat.Conn >= 0) Server.Send(src.Seat.Conn, Messages.Ui(layout, src.Aid));
        }

        void BroadcastLobby()
        {
            string msg = Messages.Lobby(cfg, Roster);
            foreach (var s in Roster.Seats) if (s.Kind == SeatKind.Phone && s.Connected) Server.Send(s.Conn, msg);
        }

        // ---- the TV ----
        static Texture2D MakeQr(string text)
        {
            var qr = QrCode.EncodeText(text, QrEcc.Medium);
            const int quiet = 4;
            int n = qr.Size + quiet * 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool dark = qr.GetModule(x - quiet, qr.Size - 1 - (y - quiet));   // texture rows go up
                    px[y * n + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        GUIStyle panelText, plate;

        void OnGUI()
        {
            float s = Screen.height / 1080f;
            panelText ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            plate ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            panelText.fontSize = Mathf.RoundToInt(26 * s);

            // join panel, top right
            float qr = cfg.view.joinPanelQrPx * s, pad = 16 * s, w = qr + 2 * pad, x = Screen.width - w - pad, y = pad;
            float textH = 210 * s;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, w, qr + textH + 2 * pad), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (qrTex != null) GUI.DrawTexture(new Rect(x + pad, y + pad, qr, qr), qrTex);
            string text = ServerError != null
                ? "<color=#ff8080>Phone server failed:\n" + ServerError + "</color>"
                : "Scan to join, or open\n<b>" + Url.Replace("http://", "") + "</b>\nCode <b><size=" + Mathf.RoundToInt(44 * s) + ">" + RoomCode + "</size></b>\n" + TeamLine();
            GUI.Label(new Rect(x + pad, y + pad + qr + 8 * s, qr, textH), text, panelText);

            // name plates over people, numbers over bots; FIND ME doubles the plate; OUT past the lines
            var cam = rig != null ? rig.Cam : Camera.main;
            if (cam == null) return;
            float now = Time.unscaledTime;
            foreach (var p in World.Players)
            {
                var seat = p.Human ? Roster[p.Slot] : null;
                Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * 0.4f);
                if (sp.z <= 0) continue;
                bool find = findUntil.TryGetValue(p.Slot, out float until) && until > now;
                string label = seat != null ? seat.Name + (seat.Connected ? "" : " ...") : p.Number.ToString();
                if (p.OutOfBounds) label += " OUT";
                plate.fontSize = Mathf.RoundToInt(cfg.view.plateFontPx * s * (find ? 2f : 1f));
                var size = plate.CalcSize(new GUIContent(label));
                var r = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y);
                var team = p.Team >= 0 && p.Team < cfg.teams.Count ? cfg.teams[p.Team] : null;
                GUI.color = team != null ? Colors.Hex(team.color) : Color.grey;
                if (p.Human) GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, label, plate);
            }
        }

        string TeamLine()
        {
            var parts = new List<string>();
            for (int t = 0; t < cfg.teams.Count; t++) parts.Add(cfg.teams[t].@short + " " + Roster.TeamCount(t, true));
            return "People: " + string.Join("  ·  ", parts);
        }
    }
}
