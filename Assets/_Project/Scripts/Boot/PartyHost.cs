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
    // Dev keys: L lines everyone up again, T throws the ball downfield (lob; Shift = bullet), B makes the bots wander,
    // H starts a new huddle.
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
        Stadium stadium;
        Markings marks;
        // a photo scene (the gauntlet loop): the moment is frozen, the TV shows the game HUD only
        public PhotoState Photo { get; private set; }
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
            h.stadium = new Stadium(cfg, baseMat);
            h.marks = new Markings(cfg, baseMat);
            h.StartServer(args.Bind);
            h.FillWithBots();
            if (args.Wander) h.World.BotsWander = true;
            if (!args.LookX.HasValue) rig.SetLookAtX(h.World.LineOfScrimmageX);
            if (args.ThrowDemo.HasValue) h.World.Throw(h.World.Ball.X + h.World.Dir * args.ThrowDemo.Value, h.World.Ball.Z, true);
            for (int i = 0; i < args.WarmupTicks; i++) h.StepOnce();
            if (args.CalledDemo)
            {
                if (h.World.Phase != Phase.Huddle) Huddle.Begin(h.World);
                var book = Huddle.Playbook(h.World, h.World.Offense);
                if (book.Count > 0) h.World.Calls[h.World.Offense] = book[0].id;
            }
            string photoName = PhotoScenes.Requested(args);
            if (photoName != null) h.Photo = PhotoScenes.Setup(photoName, h.World, h.Players, rig);
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
                if (kb.hKey.wasPressedThisFrame && Huddle.On(World)) Huddle.Begin(World);
                if (kb.tKey.wasPressedThisFrame)
                {
                    if (World.Ball.State != BallState.Held) World.LineUp();
                    World.Throw(World.Ball.X + World.Dir * cfg.bots.wanderYd * 2f, World.Ball.Z, !kb.shiftKey.isPressed);
                }
            }

            float dt = World.Dt;
            int ticks = 0;
            if (Photo != null) accumulator = 0f;
            else if (lockstep) { StepOnce(); accumulator = 0f; }
            else
            {
                accumulator += Time.deltaTime;
                while (accumulator >= dt && ticks < cfg.sim.maxTicksPerFrame) { StepOnce(); accumulator -= dt; ticks++; }
                if (ticks == cfg.sim.maxTicksPerFrame) accumulator = 0f;
            }
            if (!phaseKnown || World.Phase != lastPhase || World.HuddleNumber != lastHuddle) OnPhaseChanged();
            RefreshLayouts();
            Players.Render(World, lockstep || Photo != null ? 1f : Mathf.Clamp01(accumulator / dt));
            float firstX = Photo != null ? Photo.FirstDownX : World.FirstDownX;
            if (Photo == null && !Downs.On(World)) firstX = World.LineOfScrimmageX + World.Dir * cfg.downs.firstDownYd;
            marks.SetLines(World.LineOfScrimmageX, firstX);
            stadium.SetMarkers(World.LineOfScrimmageX, firstX);
            humanFeet.Clear();
            foreach (var p in World.Players) if (p.Human) humanFeet.Add(new KeyValuePair<int, Vector3>(p.Slot, Players.FeetPosition(p.Slot)));
            marks.Rings(humanFeet);
            float now = Time.unscaledTime;
            foreach (var p in World.Players)
                if (p.Human) marks.Beam(p.Slot, Players.FeetPosition(p.Slot), Colors.Hex(cfg.teams[p.Team].color), findUntil.TryGetValue(p.Slot, out float fu) && fu > now, now);
            if (Photo == null) FollowBall(Time.deltaTime);
        }

        readonly List<KeyValuePair<int, Vector3>> humanFeet = new List<KeyValuePair<int, Vector3>>();

        // ---- the secret huddle: plays go ONLY to the team's own phones; everyone else gets check marks ----
        bool phaseKnown;
        Phase lastPhase;
        int lastHuddle;

        // The broadcast camera slides along the field with the ball, a little ahead of it in the way the offense goes;
        // on a pass it runs ahead to where the ball is coming down. It never tilts, rolls or spins.
        float camX = float.NaN;
        void FollowBall(float dt)
        {
            var b = World.Ball;
            var carrier = LivePlay.Carrier(World);
            float target;
            if (b.State == BallState.Flying) target = b.TargetX;
            else if (World.Phase == Phase.Live || World.Phase == Phase.Whistle) target = carrier != null ? carrier.X + World.Dir * cfg.camera.leadYd : b.X;
            else target = World.LineOfScrimmageX + (World.PendingTurnover ? -World.Dir : World.Dir) * cfg.camera.leadYd;
            if (World.Phase == Phase.Whistle && World.PendingTurnover) target = World.NextLos;
            float lim = cfg.field.HalfLength + cfg.field.sidelineBorderYd - cfg.camera.visibleYards * 0.5f;
            target = Mathf.Clamp(target, -lim, lim);
            if (float.IsNaN(camX)) camX = target;
            camX += (target - camX) * (1f - Mathf.Exp(-dt * cfg.camera.followRate));
            rig.SetLookAtX(camX);
        }

        void OnPhaseChanged()
        {
            // FIND ME at every snap: each person's ring flashes and a beam goes up for a moment
            if (World.Phase == Phase.Live && lastPhase != Phase.Live)
                foreach (var p in World.Players) if (p.Human) findUntil[p.Slot] = Time.unscaledTime + cfg.view.findMeS;
            phaseKnown = true;
            lastPhase = World.Phase;
            lastHuddle = World.HuddleNumber;
            foreach (var s in Roster.Seats) if (sources.TryGetValue(s.Slot, out var src)) SendPhaseTo(src);
            BroadcastStatus();
        }

        // What this phone should show now: the team's huddle, or its job in the called play.
        void SendPhaseTo(PhoneSource src)
        {
            var seat = src.Seat;
            var p = World.Find(seat.Slot);
            if (seat.Conn < 0 || p == null || seat.Team < 0) return;
            if (World.Phase == Phase.Huddle && Huddle.On(World))
            {
                Server.Send(seat.Conn, Messages.HuddleFor(World, p.Team, p));
                SendUi(src, "huddle");
            }
            else
            {
                string job = Messages.JobFor(World, p.Team, p);
                if (job != null) Server.Send(seat.Conn, job);
                SendUi(src, LayoutFor(p));
            }
        }

        // ---- the phone's buttons for this moment (M1-c; the layouts are drawn by the phone's layouts.js) ----
        readonly Dictionary<int, string> lastLayout = new Dictionary<int, string>();

        string LayoutFor(Player p)
        {
            if (!LivePlay.On(World)) return "walk";
            switch (World.Phase)
            {
                case Phase.Huddle: return "huddle";
                case Phase.Whistle: return "wait";
            }
            var carrier = LivePlay.Carrier(World);
            bool qb = p == LivePlay.Qb(World);
            if (World.Phase == Phase.Set) return qb ? "qbpre" : RoleLayout(p);
            if (carrier == p) return qb && !World.Thrown ? "qb" : "carrier";
            return RoleLayout(p);
        }

        static string RoleLayout(Player p)
        {
            switch (p.Pos)
            {
                case "OL": case "TE": return "block";
                case "WR": case "RB": return "route";
                case "DL": return "rush";
                case "LB": return "lb";
                case "CB": case "S": return "cover";
            }
            return "walk";
        }

        // Every frame: a phone whose moment changed (the ball was caught, the whistle blew) gets its new buttons.
        void RefreshLayouts()
        {
            if (World.Phase == Phase.Huddle) return;   // (the huddle screen is sent with the huddle)
            foreach (var kv in sources)
            {
                var src = kv.Value;
                var p = World.Find(kv.Key);
                if (p == null || src.Seat.Conn < 0 || src.Seat.Team < 0) continue;
                string want = LayoutFor(p);
                if (!lastLayout.TryGetValue(kv.Key, out var had) || had != want) SendUi(src, want);
            }
        }

        // The phone's line after the whistle: what happened, and what comes next.
        string WhistleLine()
        {
            string r = World.Result;
            string what = r == "TACKLED" ? "Tackled!" : r == "SACKED" ? "Sacked!" : r == "INCOMPLETE" ? "Incomplete pass." : r == "INTERCEPTED" ? "Intercepted!"
                : r == "TOUCHDOWN" ? "TOUCHDOWN!" : r == "OUT OF BOUNDS" ? "Out of bounds." : "Whistle.";
            return what + " Back to the huddle...";
        }

        // The receivers the QB can throw to, in shape order, for the "qb" layout.
        Newtonsoft.Json.Linq.JArray Receivers()
        {
            var a = new Newtonsoft.Json.Linq.JArray();
            for (int k = 1; k <= cfg.play.receivers.Count && k <= cfg.play.shapes.Count; k++)
            {
                var r = LivePlay.Receiver(World, k);
                if (r == null) continue;
                a.Add(new Newtonsoft.Json.Linq.JObject { ["shape"] = cfg.play.shapes[k - 1], ["label"] = r.Pos + " #" + r.Number });
            }
            return a;
        }

        IEnumerable<(PhoneSource src, Player p)> TeamPhones(int team)
        {
            foreach (var s in Roster.Seats)
            {
                if (s.Kind != SeatKind.Phone || !s.Connected || s.Conn < 0 || s.Team != team) continue;
                var p = World.Find(s.Slot);
                if (p != null && p.Team == team && sources.TryGetValue(s.Slot, out var src)) yield return (src, p);
            }
        }

        void BroadcastStatus()
        {
            string msg = Messages.Status(World);
            foreach (var s in Roster.Seats) if (s.Kind == SeatKind.Phone && s.Connected && s.Conn >= 0) Server.Send(s.Conn, msg);
        }

        // suggest / lock from a phone. The press must carry the huddle screen's aid.
        void OnCall(PhoneSource src, JObject m, bool lockIn)
        {
            if (PhoneProtocol.Str(m, "aid") != src.Aid) return;
            var p = World.Find(src.Seat.Slot);
            if (p == null) return;
            string id = PhoneProtocol.Str(m, "id");
            bool ok = lockIn ? Huddle.Lock(World, p.Slot, id) : Huddle.Suggest(World, p.Slot, id);
            if (!ok) return;
            foreach (var (tsrc, tp) in TeamPhones(p.Team)) Server.Send(tsrc.Seat.Conn, Messages.ThumbsFor(World, p.Team, tp));
            if (lockIn) BroadcastStatus();
        }

        // The TV's huddle line: whether each team has called, NEVER what.
        public static string TvLine(World w, GameConfig cfg)
        {
            if (!Huddle.On(w)) return "";
            int secs = Messages.Secs(w);
            if (w.Phase == Phase.Set && LivePlay.On(w)) return "SET   (the QB hikes)";
            if (w.Phase == Phase.Live) return "";
            if (w.Phase == Phase.Whistle) return w.Result + "!" + (w.Note.Length > 0 ? "   " + w.Note : "");
            if (w.Phase != Phase.Huddle) return "LINE UP   (next huddle in " + secs + ")";
            var parts = new List<string> { "HUDDLE 0:" + secs.ToString("00") };
            for (int t = 0; t < cfg.teams.Count; t++) parts.Add(cfg.teams[t].name + (Huddle.IsLocked(w, t) ? " CALLED ✓" : " ..."));
            return string.Join("     ", parts);
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
                    else if (t == "btn") src.OnButton(PhoneProtocol.Str(m, "b"), PhoneProtocol.Int(m, "d") == 1, PhoneProtocol.Str(m, "aid"),
                                                      PhoneProtocol.Str(m, "tgt"), m["lob"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)m["lob"]);
                    else if (t == "pick") OnPick(src, PhoneProtocol.Int(m, "team", -1));
                    else if (t == "suggest") OnCall(src, m, false);
                    else if (t == "lock") OnCall(src, m, true);
                }
            }
        }

        void OnJoin(int conn, JObject m)
        {
            string code = PhoneProtocol.Str(m, "code").Trim().ToUpperInvariant();
            if (code != RoomCode) { Server.Send(conn, PhoneProtocol.Err("Wrong room code. Check the TV.")); return; }
            var seat = Roster.JoinPhone(conn, PhoneProtocol.Str(m, "name"), PhoneProtocol.Str(m, "token"), PhoneProtocol.Str(m, "pid"), out bool rejoined);
            if (seat == null) { Server.Send(conn, PhoneProtocol.Err("The game is full (" + Roster.Capacity + " people).")); return; }
            if (!sources.TryGetValue(seat.Slot, out var src)) { src = new PhoneSource(seat) { Shapes = cfg.play.shapes }; sources[seat.Slot] = src; }
            byConn[conn] = src;
            lastMe.Remove(seat.Slot);
            Server.Send(conn, Messages.Joined(seat, RoomCode, cfg, Roster));
            var mine = World.Find(seat.Slot);
            Server.Send(conn, Messages.Status(World));
            if (seat.Team >= 0 && mine != null)
            {
                Server.Send(conn, Messages.Picked(seat, mine, cfg));
                SendPhaseTo(src);
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
            // the whole team's huddle screens refresh (the caller may have changed); this phone gets its own too
            foreach (var (tsrc, _) in TeamPhones(team)) SendPhaseTo(tsrc);
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
            lastLayout[src.Seat.Slot] = layout;
            if (src.Seat.Conn >= 0) Server.Send(src.Seat.Conn, Messages.Ui(layout, src.Aid, layout == "qb" ? Receivers() : null, layout == "wait" && World.Phase == Phase.Whistle ? WhistleLine() : null));
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

        GUIStyle panelText, plate, banner;

        void OnGUI()
        {
            float s = Screen.height / 1080f;
            panelText ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            plate ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            panelText.fontSize = Mathf.RoundToInt(26 * s);
            var cam = rig != null ? rig.Cam : Camera.main;

            // the scoreboard bar, top centre (real numbers come with M1-e)
            if (Photo != null) Hud.Scoreboard(cfg, Photo.Score, Photo.Quarter, Photo.Clock, Photo.Down, s);
            else Hud.Scoreboard(cfg, World.Score, "1ST", "", Downs.On(World) ? Downs.DownText(World) : "", s);

            // the people: a cyan triangle over your player; shape badges over receivers (photo scenes for now)
            if (cam != null)
                foreach (var p in World.Players)
                {
                    Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * 0.55f);
                    if (sp.z <= 0) continue;
                    if (p.Human && !Hud.EdgePortrait(cam, Players.HeadPosition(p.Slot), Colors.Hex(cfg.teams[p.Team].color), p.Number, s))
                        Hud.Pointer(new Vector2(sp.x, Screen.height - sp.y), s);
                    if (Photo == null && LivePlay.On(World) && (World.Phase == Phase.Set || World.Phase == Phase.Live) && !World.Thrown && p.Team == World.Offense)
                    {
                        int k = cfg.play.receivers.IndexOf(p.Spot);
                        if (k >= 0) Hud.ShapeBadge(new Vector2(sp.x, Screen.height - sp.y - 4 * s), k, s);
                    }
                    if (Photo != null && Photo.Badges.TryGetValue(p.Slot, out var badge)) Hud.Badge(new Vector2(sp.x, Screen.height - sp.y - 4 * s), badge, p.Spot == 3 ? new Color(0.36f, 0.85f, 0.5f) : new Color(1f, 0.82f, 0.25f), s);
                }
            if (Photo != null) return;

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

            // the huddle banner, top centre: check marks only
            string tv = TvLine(World, cfg);
            if (tv.Length > 0)
            {
                banner ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                banner.fontSize = Mathf.RoundToInt(34 * s);
                var bsize = banner.CalcSize(new GUIContent(tv));
                var br = new Rect((Screen.width - bsize.x) * 0.5f - 20 * s, 104 * s, bsize.x + 40 * s, bsize.y + 12 * s);
                GUI.color = new Color(0.05f, 0.13f, 0.1f, 0.85f);
                GUI.DrawTexture(br, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(br, tv, banner);
            }

            // name plates over people (bots wear their numbers); FIND ME doubles the plate; OUT past the lines
            if (cam == null) return;
            float now = Time.unscaledTime;
            foreach (var p in World.Players)
            {
                var seat = p.Human ? Roster[p.Slot] : null;
                if (seat == null && !p.OutOfBounds) continue;
                Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * 1.1f);
                if (sp.z <= 0) continue;
                bool find = findUntil.TryGetValue(p.Slot, out float until) && until > now;
                string label = seat != null ? seat.Name + (seat.Connected ? "" : " ...") : p.Number.ToString();
                if (p.OutOfBounds) label += " OUT";
                if (p.BotHelp) label += " (BOT)";   // a bot plays for a person who put the phone down
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
