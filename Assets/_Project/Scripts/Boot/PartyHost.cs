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
        // The practice lab (LabMode) plugs in here: its own keys instead of the dev keys, the keyboard player's input
        // before each tick, and the drill's bookkeeping after it.
        public bool DevKeys = true;
        public System.Action<Dictionary<int, PlayerInput>> BeforeTick;
        public System.Action AfterTick;

        GameConfig cfg;
        CameraRig rig;
        Stadium stadium;
        Markings marks;
        // a photo scene (the gauntlet loop): the moment is frozen, the TV shows the game HUD only
        public PhotoState Photo { get; private set; }
        bool lockstep;               // screenshots: exactly one sim tick per frame, so pictures are repeatable
        float accumulator;
        readonly Dictionary<int, PhoneSource> sources = new Dictionary<int, PhoneSource>();   // by slot
        public PhoneSource SourceOf(int slot) => sources.TryGetValue(slot, out var s) ? s : null;
        readonly Dictionary<int, PhoneSource> byConn = new Dictionary<int, PhoneSource>();
        readonly Dictionary<int, PlayerInput> inputs = new Dictionary<int, PlayerInput>();
        readonly Dictionary<int, float> findUntil = new Dictionary<int, float>();
        readonly Dictionary<int, string> lastMe = new Dictionary<int, string>();
        float nextMeAt;
        Texture2D qrTex;
        public Texture2D Qr => qrTex;   // the join code picture (the lobby screen draws it big)
        int aidCounter;

        // FEATURES.lobbyScreen: the TV picks new teams in the lobby. The Boot scene is built again with them (every
        // model, the stadium, the field paint), but the phone server, the room code and the roster carry over, so the
        // phones stay connected and keep their side (away or home) and their spots.
        sealed class Carry { public PhoneServer Server; public Roster Roster; public string RoomCode, Url, ServerError; }
        static Carry carry;
        bool keepServer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCarry() { carry?.Server?.Dispose(); carry = null; }

        public static PartyHost Create(GameObject on, GameConfig cfg, Material baseMat, CameraRig rig, CliArgs args)
        {
            var h = on.AddComponent<PartyHost>();
            Instance = h;
            h.cfg = cfg;
            h.rig = rig;
            h.lockstep = !string.IsNullOrEmpty(args.ShotsDir);
            h.World = new World(cfg, args.Seed);
            var kept = carry;
            carry = null;
            h.Roster = kept != null ? kept.Roster : new Roster(cfg.net.maxPlayers);
            h.Players = new PlayersView(cfg, baseMat);
            h.stadium = new Stadium(cfg, baseMat);
            h.marks = new Markings(cfg, baseMat);
            if (kept != null) h.TakeServer(kept);
            else h.StartServer(args.Bind);
            h.FillWithBots();
            if (kept != null) h.SeatCarriedPeople();
            GamepadHost.Attach(h, cfg);   // WS-14: Xbox pads join as players (FEATURES.gamepad)
            if (args.Wander) h.World.BotsWander = true;
            if (!args.LookX.HasValue) rig.SetLookAtX(h.World.LineOfScrimmageX);
            h.holdCamera = args.LookX.HasValue;
            if (args.ThrowDemo.HasValue) h.World.Throw(h.World.Ball.X + h.World.Dir * args.ThrowDemo.Value, h.World.Ball.Z, true);
            if (args.FieldGoalDemo && Kicking.On(h.World))
            {
                var fw = h.World;
                fw.Kick.Opened = true;   // (no opening kickoff)
                fw.LineOfScrimmageX = fw.Dir * (cfg.field.GoalLineX - 25f);
                fw.Down = cfg.downs.downsToGain;
                fw.FirstDownX = fw.LineOfScrimmageX + fw.Dir * cfg.downs.firstDownYd;
            }
            for (int i = 0; i < args.WarmupTicks; i++) h.StepOnce();
            if (args.CalledDemo)
            {
                if (h.World.Phase != Phase.Huddle) Huddle.Begin(h.World);
                var book = Huddle.Playbook(h.World, h.World.Offense);
                if (book.Count > 0) h.World.Calls[h.World.Offense] = book[0].id;
            }
            string photoName = PhotoScenes.Requested(args);
            if (photoName != null) h.Photo = PhotoScenes.Setup(photoName, h.World, h.Players, rig);
            on.AddComponent<ReceivingHud>().Host = h;   // WS-05: CALL FOR IT pulse on the TV and the QB's phone
            Lobby.Attach(h, cfg, args);   // WS-07: the lobby and two-way players (FEATURES.lobby)
            KickPhones.Attach(h);   // WS-01: the kick meter on the kicker's phone and on the TV
            ThrowAimView.Attach(h, baseMat);   // the throw stick's live arc and landing spot on the TV
            DriveBlockView.Attach(h, baseMat);   // FEATURES.driveBlock: the shove arrows and the open lane on the TV
            FlightView.Attach(h, baseMat);   // the lab's helpers in the game: the pass's flight, landing and catch rings, tackle reach
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
            if (!keepServer) Server?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ---- new teams from the lobby (FEATURES.lobbyScreen) ----

        // Play away against home (short codes, saved like the title's SETTINGS page) and build the Boot scene again with
        // them. The phones stay connected through it: the next PartyHost takes over this one's server and roster.
        public void SwitchTeams(string away, string home)
        {
            TitleScreen.SaveTeams(away, home);
            carry = new Carry { Server = Server, Roster = Roster, RoomCode = RoomCode, Url = Url, ServerError = ServerError };
            keepServer = true;
            GamepadHost.Instance?.CarryPads();
            Debug.Log("[lobby] new teams: " + away + " at " + home + "; building the stadium again (the phones stay)");
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void TakeServer(Carry kept)
        {
            Server = kept.Server;
            RoomCode = kept.RoomCode;
            Url = kept.Url;
            ServerError = kept.ServerError;
            if (Url != null) qrTex = MakeQr(Url + "/join/" + RoomCode);
        }

        // After a team switch: every person back on their side (a bot's spot; the lobby puts them on their own spots
        // again), and every connected phone told the new teams. Messages that came in meanwhile wait in the server.
        void SeatCarriedPeople()
        {
            foreach (var seat in Roster.Seats)
            {
                if (seat.Kind != SeatKind.Phone) continue;
                if (seat.Team >= cfg.teams.Count) seat.Team = -1;
                if (seat.Team >= 0 && World.ReplaceBot(seat.Team, seat.Slot) == null) seat.Team = -1;
                if (seat.Pid == "pad") continue;   // (a pad's seat: GamepadHost takes it back)
                var src = new PhoneSource(seat) { Shapes = cfg.play.shapes };
                sources[seat.Slot] = src;
                if (!seat.Connected || seat.Conn < 0) continue;
                byConn[seat.Conn] = src;
                Server.Send(seat.Conn, Messages.Joined(seat, RoomCode, cfg, Roster));
                Server.Send(seat.Conn, Messages.Status(World));
                var mine = World.Find(seat.Slot);
                if (seat.Team >= 0 && mine != null) Server.Send(seat.Conn, Messages.Picked(seat, mine, cfg));
                else SendUi(src, "pick");
            }
            BroadcastLobby();
        }

        void Update()
        {
            PumpNet();
            var kb = Keyboard.current;
            if (kb != null && DevKeys)
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
            CarrierMovesView.Apply(World, Players);   // WS-04: spin, stiff arm, dive, fumbles on the TV
            if (Photo == null && !holdCamera) FollowBall(Time.deltaTime);   // (-look-x holds the camera where it was put)
            var replay = Replay.Drive(gameObject, cfg, World, Players, rig, s => Roster[s]?.Name);   // WS-08: the instant replay and the result card
            // the lines on the grass: the replayed play's while a replay is on, else the live game's
            var lines = replay != null && replay.Showing ? replay.Shown : World;
            float firstX = Photo != null ? Photo.FirstDownX : lines.FirstDownX;
            if (Photo == null && !Downs.On(World)) firstX = lines.LineOfScrimmageX + lines.Dir * cfg.downs.firstDownYd;
            marks.SetLines(lines.LineOfScrimmageX, firstX);
            stadium.SetMarkers(lines.LineOfScrimmageX, firstX);
            Kicking.Wind(World, out float windX, out float windZ);   // FEATURES.wind: the flags on the posts
            stadium.SetWind(windX, windZ, cfg.kicking.windMaxYdS, (int)(Time.unscaledTime * cfg.anim.fps));
            stadium.SetScore(Photo != null ? Photo.Score : World.Score);   // the big scoreboard over the end (stadium bowl)
            // the rings and FIND ME glows under people's players follow the live figures, so none while a replay is on
            humanFeet.Clear();
            if (!Replay.OnScreen)
                foreach (var p in World.Players) if (p.Human) humanFeet.Add(new KeyValuePair<int, Vector3>(p.Slot, Players.FeetPosition(p.Slot)));
            marks.Rings(humanFeet);
            float now = Time.unscaledTime;
            foreach (var p in World.Players)
                if (p.Human) marks.Beam(p.Slot, Players.FeetPosition(p.Slot), Colors.Hex(cfg.teams[p.Team].color), !Replay.OnScreen && findUntil.TryGetValue(p.Slot, out float fu) && fu > now, now);
            AudibleHost.Drive(this);   // WS-13: each team's audible / hot route screen, to that team's phones only
            PostGameHost.Drive(this);   // WS-12: the stats, score celebrations, the post-game screen and the phones' final card
            GameLog.Drive(this);   // WS-15: the game log next to the build (tools/playtest-report.ps1 reads it)
            Audio.Drive(gameObject, cfg, World);   // WS-09: whistle, hits, crowd, huddle tick, score sting
        }

        readonly List<KeyValuePair<int, Vector3>> humanFeet = new List<KeyValuePair<int, Vector3>>();

        // ---- the secret huddle: plays go ONLY to the team's own phones; everyone else gets check marks ----
        bool phaseKnown;
        Phase lastPhase;
        int lastHuddle;

        // The broadcast camera slides along the field with the ball, a little ahead of it in the way the offense goes;
        // on a pass it runs ahead to where the ball is coming down. It never rolls or spins (it tilts only with snapZoom).
        float camX = float.NaN;
        bool holdCamera;
        float camZoom;

        // FEATURES.snapZoom: close in for the snap, then (camera.live.delayS into the play) back and up so the whole play
        // fits; it holds through the whistle and comes back in once the next play is set.
        float SnapZoom(float dt)
        {
            if (!cfg.Feature("snapZoom")) return camZoom = 0f;
            var live = cfg.camera.live;
            float want = World.Phase == Phase.Live ? (World.PlayTicks >= live.delayS * cfg.sim.tickHz ? 1f : 0f)
                : World.Phase == Phase.Whistle ? camZoom : 0f;
            camZoom += (want - camZoom) * (1f - Mathf.Exp(-dt * live.rate));
            return camZoom;
        }

        void FollowBall(float dt)
        {
            var b = World.Ball;
            var carrier = LivePlay.Carrier(World);
            float target;
            // FEATURES.smoothMotion: follow where the carrier and ball are drawn this frame, not the last tick (30 a second),
            // so the camera glides with the players instead of chasing a target that jumps every other frame
            bool smooth = cfg.Feature("smoothMotion");
            float carrierX = carrier == null ? 0f : smooth ? Players.ShownX(carrier) : carrier.X;
            float ballX = smooth ? Players.BallShownX(World) : b.X;
            if (b.State == BallState.Flying) target = b.TargetX;
            else if (World.Phase == Phase.Live || World.Phase == Phase.Whistle) target = carrier != null ? carrierX + World.Dir * cfg.camera.leadYd : ballX;
            else target = World.LineOfScrimmageX + (World.PendingTurnover ? -World.Dir : World.Dir) * cfg.camera.leadYd;
            if (World.Phase == Phase.Whistle && World.PendingTurnover) target = World.NextLos;
            float lim = cfg.field.HalfLength + cfg.field.sidelineBorderYd - cfg.camera.visibleYards * 0.5f;
            target = Mathf.Clamp(target, -lim, lim);
            if (float.IsNaN(camX)) camX = target;
            camX += (target - camX) * (1f - Mathf.Exp(-dt * cfg.camera.followRate));
            rig.SetZoom(SnapZoom(dt));
            KickCam(dt);
            rig.SetLookAtX(camX);
        }

        // FEATURES.kickCam: from the field goal's line-up to the whistle (and kicking.cam.holdAfterS into it, so the
        // TV sees it go through or miss) the camera eases round behind the kicker, looking down the field at the
        // posts; then it eases back to the sideline. Not while a replay is on.
        float kickCamWeight, kickCamUntil;
        bool kickCamSeen;
        Vector3 kickCamPos, kickCamLook;
        void KickCam(float dt)
        {
            var kc = cfg.kicking.cam;
            var k = World.Kick;
            bool fg = cfg.Feature("kickCam") && Kicking.Mode(World) == KickKind.FieldGoal && k.Placed && !Replay.OnScreen;
            bool want = false;
            if (fg && (World.Phase == Phase.Set || World.Phase == Phase.Live))
            {
                float m = cfg.field.yardM, dir = World.Dir;
                kickCamPos = new Vector3((k.HoldX - dir * kc.backYd) * m, kc.heightM, (k.HoldZ + dir * kc.sideYd) * m);
                // the nearest thing (the kicker's feet where he lines up) and the highest (the flags on the uprights),
                // and the look tilted lookShare of the way up between them, so both are in the picture
                var kc2 = cfg.kicking;
                var feet = new Vector3((k.HoldX - dir * kc2.kickerBackYd) * m, 0f, (k.HoldZ + dir * kc2.kickerSideYd) * m);
                var st = cfg.stadium;
                var flags = new Vector3(dir * cfg.field.HalfLength * m, st.postCrossbarM + st.postUprightM + st.flagHeightM, 0f);
                var look = Vector3.Slerp((feet - kickCamPos).normalized, (flags - kickCamPos).normalized, kc.lookShare);
                kickCamLook = kickCamPos + look * (flags - kickCamPos).magnitude;
                kickCamUntil = Time.unscaledTime + kc.holdAfterS;
                want = true;
            }
            else if (fg && World.Phase == Phase.Whistle && Time.unscaledTime < kickCamUntil) want = true;
            if (!kickCamSeen) kickCamWeight = want ? 1f : 0f;   // (the first frame: already there, like camX)
            kickCamSeen = true;
            kickCamWeight += ((want ? 1f : 0f) - kickCamWeight) * (1f - Mathf.Exp(-dt * kc.rate));
            if (kickCamWeight < 0.001f) kickCamWeight = 0f;
            rig.SetKickView(kickCamWeight, kickCamPos, kickCamLook, kc.fovDeg);
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
                if (Lobby.Instance != null) Lobby.Instance.SendHuddleLineup(seat);   // FEATURES.huddleSwitch: the spots to switch to
            }
            else
            {
                string job = Messages.JobFor(World, p.Team, p);
                if (job != null) Server.Send(seat.Conn, job);
                SendUi(src, LayoutFor(p));
            }
        }

        // The team's huddle screens again (FEATURES.huddleSwitch: someone switched spots, so the jobs and caller changed).
        public void RefreshHuddle(int team)
        {
            foreach (var (src, _) in TeamPhones(team)) SendPhaseTo(src);
        }

        // The aid of the screen this seat's phone shows now (a press must carry it).
        public string AidOf(int slot) => sources.TryGetValue(slot, out var src) ? src.Aid : null;

        // ---- the phone's buttons for this moment (M1-c; the layouts are drawn by the phone's layouts.js) ----
        readonly Dictionary<int, string> lastLayout = new Dictionary<int, string>();

        string LayoutFor(Player p) => PhoneLayouts.For(World, p);

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

        // The TV's huddle line: whether each team has called, NEVER what. (WS-02: it lives in View/Scoreboard.cs now)
        public static string TvLine(World w, GameConfig cfg) => Scoreboard.TvLine(w);

        public void StepOnce()
        {
            if (Lobby.Instance != null && Lobby.Instance.Holding) return;   // WS-07: the game waits on the lobby until kickoff
            float now = Time.unscaledTime;
            inputs.Clear();
            foreach (var kv in sources)
            {
                var src = kv.Value;
                src.ExpireStick(now, cfg.net.stickStaleS);
                inputs[kv.Key] = src.Poll();
                if (src.TakeFind()) findUntil[kv.Key] = now + cfg.view.findMeS;
            }
            BeforeTick?.Invoke(inputs);
            Players.BeforeStep(World);
            World.Step(inputs);
            AfterTick?.Invoke();
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
                    else if (t == "aim") src.OnAim(PhoneProtocol.Float(m, "x", -1f, 1f), PhoneProtocol.Float(m, "y", -1f, 1f), PhoneProtocol.Float(m, "arc", 0f, 1f),
                                                   PhoneProtocol.Int(m, "cancel") == 1, PhoneProtocol.Str(m, "aid"));
                    else if (t == "throw") src.OnThrowStick(PhoneProtocol.Float(m, "x", -1f, 1f), PhoneProtocol.Float(m, "y", -1f, 1f), PhoneProtocol.Float(m, "arc", 0f, 1f),
                                                            PhoneProtocol.Str(m, "aid"));
                    else if (t == "kick") KickPhones.OnKick(this, src, m);   // WS-01
                    else if (t == "kickaim") KickPhones.OnAim(this, src, m);   // FEATURES.kickSweep: the first tap
                    else if (t == "audible" || t == "hot") AudibleHost.OnMessage(this, src, m);   // WS-13
                    else if (t == "drive") src.OnDrive(PhoneProtocol.Float(m, "x", -1f, 1f), PhoneProtocol.Float(m, "y", -1f, 1f), PhoneProtocol.Str(m, "aid"));   // FEATURES.driveBlock
                    else if (Lobby.Instance != null) Lobby.Instance.OnMessage(src.Seat, t, m);   // WS-07: spot, swap, kickoff, lobbyme
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
            if (src.Seat.Conn >= 0) Server.Send(src.Seat.Conn, Messages.Ui(layout, src.Aid, layout == "qb" ? Receivers() : null, layout == "wait" && World.Phase == Phase.Whistle ? WhistleLine() : null,
                                                                               layout == "qb" && ThrowStick.On(World), layout == "block" && DriveBlock.On(World)));
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

        GUIStyle panelText, plate, banner, titleStyle;

        void OnGUI()
        {
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            Hud.JoinPanel = Rect.zero;   // (set again below while the join panel is on)
            if (TitleScreen.Showing) return;   // the title screen covers the TV (the bots' show game has no HUD)
            if (Lobby.Instance != null && Lobby.Instance.FullScreen) return;   // FEATURES.lobbyScreen: the join screen covers the TV
            float s = Screen.height / 1080f;
            panelText ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            plate ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            panelText.fontSize = Mathf.RoundToInt(26 * s);
            var cam = rig != null ? rig.Cam : Camera.main;

            // the scoreboard bar, top centre (real numbers come with M1-e)
            if (Photo != null) { if (Photo.Hud) Hud.Scoreboard(cfg, Photo.Score, Photo.Quarter, Photo.Clock, Photo.Down, s); }
            else Scoreboard.Draw(World, s);   // WS-02
            if (Photo != null && Photo.Titles.Count > 0)
            {
                titleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.BoldAndItalic };
                titleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.11f);
                foreach (var t in Photo.Titles)
                {
                    titleStyle.normal.textColor = Colors.Hex(t.color);
                    var r = new Rect(Screen.width * t.x - Screen.width * 0.25f, Screen.height * 0.03f, Screen.width * 0.5f, Screen.height * 0.16f);
                    // (stamped a few times a hair apart: chunky poster letters, not a thin italic)
                    float d = Mathf.Max(1f, Screen.height * 0.003f);
                    for (int i = -2; i <= 2; i++)
                        for (int j = -1; j <= 1; j++) GUI.Label(new Rect(r.x + i * d, r.y + j * d, r.width, r.height), t.text, titleStyle);
                }
            }

            // the people: a small triangle over each person's player in his phone's colour (the team colour the
            // controller is painted in; no name, so it hides nothing), flashing white while FIND ME is on; shape badges
            // over receivers (photo scenes for now). None during a replay: they would hang over the live players, who are hidden.
            float now = Time.unscaledTime;
            if (cam != null && !Replay.OnScreen)
                foreach (var p in World.Players)
                {
                    Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * 0.55f);
                    if (sp.z <= 0) continue;
                    if (p.Human)
                    {
                        var tc = Colors.Hex(cfg.teams[p.Team].color);
                        var seat = Photo == null ? Roster[p.Slot] : null;
                        bool find = seat != null && findUntil.TryGetValue(p.Slot, out float fu) && fu > now;
                        if (!Hud.EdgePortrait(cam, Players.HeadPosition(p.Slot), tc, p.Number, s))
                            Hud.Pointer(new Vector2(sp.x, Screen.height - sp.y), tc, p.BotHelp || (seat != null && !seat.Connected), s,
                                find && ((int)(now * 8f)) % 2 == 0);
                    }
                    if (Photo == null && LivePlay.On(World) && (World.Phase == Phase.Set || World.Phase == Phase.Live) && !World.Thrown && p.Team == World.Offense)
                    {
                        int k = cfg.play.receivers.IndexOf(p.Spot);
                        if (k >= 0) Hud.ShapeBadge(new Vector2(sp.x, Screen.height - sp.y - 4 * s), k, s);
                    }
                    if (Photo != null && Photo.Badges.TryGetValue(p.Slot, out var badge)) Hud.Badge(new Vector2(sp.x, Screen.height - sp.y - 4 * s), badge, p.Spot == 3 ? new Color(0.36f, 0.85f, 0.5f) : new Color(1f, 0.82f, 0.25f), s);
                }
            if (Photo != null) return;

            // join panel, top right (FEATURES.pauseQr: in the pause menu instead, Esc shows it)
            if (!PauseMenuModel.QrInMenu(cfg, PauseMenu.Instance != null))
            {
                float qr = cfg.view.joinPanelQrPx * s, pad = 16 * s, w = qr + 2 * pad, x = Screen.width - w - pad, y = pad;
                float textH = 210 * s;
                Hud.JoinPanel = new Rect(x, y, w, qr + textH + 2 * pad);   // the kick meter keeps clear of it
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(Hud.JoinPanel, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (qrTex != null) GUI.DrawTexture(new Rect(x + pad, y + pad, qr, qr), qrTex);
                GUI.Label(new Rect(x + pad, y + pad + qr + 8 * s, qr, textH), JoinText(s), panelText);
            }

            // the huddle banner, top centre: check marks only
            string tv = TvLine(World, cfg);
            if (tv.Length > 0 && cfg.Feature("toonHud") && UiFont.On)
                ToonHud.Banner(tv, s, Downs.On(World) && !Clock.IsOver(World));   // FEATURES.toonHud: a navy and gold pill under the scoreboard
            else if (tv.Length > 0)
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

            // a small OUT (with the number) over a man past the lines. FIND ME is the triangle flash above and the small glow
            // under his feet (Markings.Beam); FEATURES.nameBars (off by default, Kenny: too big): a slim bar with each
            // person's name over his player, see-through during a live play.
            if (cam == null) return;
            foreach (var p in World.Players)
            {
                var seat = p.Human ? Roster[p.Slot] : null;
                if (!p.OutOfBounds)
                {
                    if (seat != null && cfg.Feature("nameBars")) NameBar(p, seat, cam, s);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * 1.1f);
                if (sp.z <= 0) continue;
                string label = p.Number + " OUT";
                plate.fontSize = Mathf.RoundToInt(cfg.view.plateFontPx * s);
                var size = plate.CalcSize(new GUIContent(label));
                var r = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y);
                GUI.Label(r, label, plate);
            }
        }

        GUIStyle bar;

        // FEATURES.nameBars: the person's name in a slim team-colour bar over his player's head.
        void NameBar(Player p, Seat seat, Camera cam, float s)
        {
            var rc = cfg.reactions;
            Vector3 sp = cam.WorldToScreenPoint(Players.HeadPosition(p.Slot) + Vector3.up * rc.barAboveM);
            if (sp.z <= 0) return;
            bar ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            bar.fontSize = Mathf.RoundToInt(rc.barFontPx * s);
            string label = Reactions.BarLabel(seat.Name, seat.Connected, p.BotHelp);
            var size = bar.CalcSize(new GUIContent(label));
            var r = new Rect(sp.x - size.x * 0.5f - 6 * s, Screen.height - sp.y - size.y, size.x + 12 * s, size.y);
            float a = Reactions.BarAlpha(cfg, World.Phase);
            var team = p.Team >= 0 && p.Team < cfg.teams.Count ? cfg.teams[p.Team] : null;
            var c = team != null ? Colors.Hex(team.color) : Color.grey;
            GUI.color = new Color(c.r, c.g, c.b, a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Min(1f, a + 0.3f));
            GUI.Label(r, label, bar);
            GUI.color = Color.white;
        }

        // the words under the QR code: the address, the room code and who joined (or why the phone server failed)
        public string JoinText(float s) => ServerError != null
            ? "<color=#ff8080>Phone server failed:\n" + ServerError + "</color>"
            : "Scan to join, or open\n<b>" + Url.Replace("http://", "") + "</b>\nCode <b><size=" + Mathf.RoundToInt(44 * s) + ">" + RoomCode + "</size></b>\n<size=" + Mathf.RoundToInt(21 * s) + ">" + TeamLine() + "</size>";

        string TeamLine()
        {
            var parts = new List<string>();
            for (int t = 0; t < cfg.teams.Count; t++) parts.Add(cfg.teams[t].@short + " " + Roster.TeamCount(t, true));
            return string.Join(" · ", parts) + " joined";   // (short and a bit smaller: one line under the code)
        }
    }
}
