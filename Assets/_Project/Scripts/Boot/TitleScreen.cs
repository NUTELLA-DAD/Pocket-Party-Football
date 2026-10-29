using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PocketFootball.Boot
{
    // The title screen (FEATURES.titleScreen, NO_TITLE_SCREEN=1 goes straight to the lobby). Kenny's logo and the
    // buttons: PLAY, POCKET CUP (FEATURES.cup: the bracket page, CupHost.cs), HOW TO PLAY, SETTINGS. Behind them the
    // bots play a game of their own (attract mode), filmed from a new angle every play (title.shots,
    // View/TitleShots.cs). That game is only for show: no lobby, no game clock, no replays, no game log, no pads
    // joining, and a new seed every time. PLAY loads the Boot scene again, so the real
    // game starts fresh (a new world, a new room code) at the lobby; the title shows once per run.
    // Mouse, keyboard (arrows, Enter, Esc) or an Xbox pad (d-pad or stick, A, B). Esc on the menu quits as before.
    public sealed class TitleScreen : MonoBehaviour
    {
        public static TitleScreen Instance { get; private set; }
        public static bool Showing => Instance != null;
        static bool done;   // PLAY was pressed: the reloaded scene goes straight to the lobby
        static string returnPage;   // a cup game ended: the reloaded scene shows the title again, on this page
        static string lobbyAway, lobbyHome;   // the teams the lobby screen picked this run (they beat PP_TEAMS)

        // (the editor may keep statics between play sessions: every session starts on the title again)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { done = false; returnPage = null; Instance = null; lobbyAway = lobbyHome = null; }

        // The next scene load shows the title again, open on this page ("cup": after a cup game).
        public static void ReturnTo(string startPage)
        {
            done = false;
            returnPage = startPage;
        }

        // The attract game leaves these off; the reloaded scene reads game.json again, so the real game has them all.
        static readonly string[] AttractOff = { "lobby", "clock", "replay", "gamelog", "gamepad" };

        const string VolumeKey = "pp.volume", HelperKey = "pp.";   // (a helper switch saves as pp.<name>: 1 on, 0 off)
        const string AwayKey = "pp.team.away", HomeKey = "pp.team.home";   // FEATURES.teamPick: the two teams' short codes
        static readonly string[] HelperNames = { "FLIGHT LINE", "LANDING RING", "TACKLE RING" };   // FlightMarks.Switches, in order
        // settings rows: 0 sound, then (FEATURES.teamPick, more than two teams) away and home, full screen, the
        // helpers, back
        int TeamRows => cfg.Feature("teamPick") && TeamPick.League(cfg).Count > 2 ? 2 : 0;
        int ScreenRow => 1 + TeamRows;
        int HelperRow => ScreenRow + 1;
        int SettingsRows => HelperRow + FlightMarks.Switches.Length + 1;
        int away, home;   // the picked teams, as league indexes (saved at once; the game after PLAY wears them)

        enum Page { Menu, HowTo, Settings, Cup }
        enum Item { Play, Cup, HowTo, Settings }

        GameConfig cfg;
        PartyHost host;
        CameraRig rig;
        Page page = Page.Menu;
        int pick;              // the button with the highlight (menu: its items in order; settings: its rows; cup: its buttons)
        float fadeT = -1f;     // PLAY pressed: the fade to black (-1: not fading)
        int escFrame = -1;
        Texture2D logo, black;
        Item[] items;          // the menu's buttons, top to bottom
        Texture2D[] buttons;   // their pictures (null: drawn as text)
        Rect[] hit;            // the menu buttons on screen (GUI space), for the mouse

        // the camera
        int shot = -1;
        float shotT, fx = float.NaN, fz;
        Phase lastPhase;
        bool phaseKnown;

        // Shown at boot when the feature is on, unless this run already pressed PLAY, or this is a tool run: the lab,
        // a photo scene, screenshots (unless -title), or batch mode (the tests).
        public static bool Requested(GameConfig cfg, CliArgs args)
        {
            if (!cfg.Feature("titleScreen") || done) return false;
            if (args.Title) return true;
            return string.IsNullOrEmpty(args.ShotsDir) && !LabMode.Requested() && PhotoScenes.Requested(args) == null && !Application.isBatchMode;
        }

        // Before the host is built: the attract game's own switches and a fresh seed, so the plays differ every boot.
        public static void PrepareAttract(GameConfig cfg, CliArgs args)
        {
            foreach (var f in AttractOff) cfg.features[f] = false;
            if (!args.Title) args.Seed = System.Environment.TickCount & 0x7fffffff;
        }

        public static TitleScreen Attach(GameObject on, GameConfig cfg, PartyHost host, CameraRig rig, string startPage = null)
        {
            var t = on.AddComponent<TitleScreen>();
            t.cfg = cfg;
            t.host = host;
            t.rig = rig;
            t.logo = Resources.Load<Texture2D>("Title/TitleArt");
            t.items = CupHost.On(cfg) ? new[] { Item.Play, Item.Cup, Item.HowTo, Item.Settings } : new[] { Item.Play, Item.HowTo, Item.Settings };
            t.buttons = new Texture2D[t.items.Length];
            t.hit = new Rect[t.items.Length];
            for (int i = 0; i < t.items.Length; i++) t.buttons[i] = Resources.Load<Texture2D>(ButtonArt[(int)t.items[i]]);
            t.black = Texture2D.whiteTexture;
            host.DevKeys = false;   // (W/A/S/D and the arrows move the highlight here, not the dev keys)
            t.away = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 0 ? cfg.teams[0].@short : null));
            t.home = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 1 ? cfg.teams[1].@short : null));
            if (t.home == t.away) t.home = TeamPick.Step(cfg, t.home, t.away, +1);
            if (returnPage != null) { startPage = returnPage; returnPage = null; }
            if (startPage == "howto") t.page = Page.HowTo;
            else if (startPage == "settings") t.page = Page.Settings;
            else if (startPage == "cup" && System.Array.IndexOf(t.items, Item.Cup) >= 0) { t.page = Page.Cup; t.cup = CupHost.Load(cfg); }
            Instance = t;
            Debug.Log("[title] title screen up (attract mode behind it)");
            return t;
        }

        static readonly string[] ButtonArt = { "Title/ButtonPlay", "Title/ButtonCup", "Title/ButtonHowToPlay", "Title/ButtonSettings" };   // by Item
        static readonly string[] ButtonText = { "PLAY", "POCKET CUP", "HOW TO PLAY", "SETTINGS" };

        void OnDestroy() { if (Instance == this) Instance = null; }

        // The saved sound level, helper switches and teams, for every boot (the title's settings page changes them).
        // PP_TEAMS=CAC,WHL (away, home) picks the teams for a run; the tests (batch mode) keep game.json's two.
        public static void ApplySavedSettings(GameConfig cfg)
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            FlightMarks.ApplySaved(cfg, n => PlayerPrefs.HasKey(HelperKey + n) ? PlayerPrefs.GetInt(HelperKey + n) != 0 : (bool?)null,
                System.Environment.GetEnvironmentVariable);
            if (lobbyAway != null) TeamPick.Apply(cfg, lobbyAway, lobbyHome);
            else if (TeamPick.ParsePair(System.Environment.GetEnvironmentVariable("PP_TEAMS"), out string a, out string h)) TeamPick.Apply(cfg, a, h);
            else if (!Application.isBatchMode) TeamPick.Apply(cfg, PlayerPrefs.GetString(AwayKey, ""), PlayerPrefs.GetString(HomeKey, ""));
            if (cfg.teams.Count >= 2) Debug.Log("[title] teams: " + cfg.teams[0].name + " at " + cfg.teams[1].name);
        }

        // The lobby screen's pick (FEATURES.lobbyScreen): saved for later runs, like the SETTINGS page, and kept for this
        // run's scene rebuild. (Batch runs save nothing.)
        public static void SaveTeams(string away, string home)
        {
            lobbyAway = away; lobbyHome = home;
            if (Application.isBatchMode) return;
            PlayerPrefs.SetString(AwayKey, away);
            PlayerPrefs.SetString(HomeKey, home);
            PlayerPrefs.Save();
        }

        // GameBootstrap quits on Esc; not while a page is open here, nor on the frame Esc closed one.
        public static bool HoldsEscape => Instance != null && (Instance.page != Page.Menu || Instance.escFrame == Time.frameCount || Instance.fadeT >= 0f);

        // ---- input ----
        void Update()
        {
            if (fadeT >= 0f)
            {
                fadeT += Time.unscaledDeltaTime;
                if (fadeT >= cfg.title.fadeS) StartGame();
                return;
            }
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame));
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame));
            bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame));
            bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame));
            bool ok = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);

            var mouse = Mouse.current;
            Vector2 mp = Vector2.zero;
            bool click = false, moved = false;
            if (mouse != null)
            {
                mp = mouse.position.ReadValue();
                mp.y = Screen.height - mp.y;   // GUI space: y goes down
                click = mouse.leftButton.wasPressedThisFrame;
                moved = mouse.delta.ReadValue().sqrMagnitude > 0f;
            }

            if (page == Page.Menu)
            {
                int n = items.Length;
                if (up) pick = (pick + n - 1) % n;
                if (down) pick = (pick + 1) % n;
                for (int i = 0; i < hit.Length; i++)
                    if ((moved || click) && hit[i].Contains(mp)) { pick = i; if (click) ok = true; }
                if (click && !Over(mp)) ok = false;
                if (ok) Choose(items[pick]);
                return;
            }

            if (back) { Open(Page.Menu); escFrame = Time.frameCount; return; }
            if (page == Page.HowTo)
            {
                if (ok || click) Open(Page.Menu);
                return;
            }
            if (page == Page.Cup)
            {
                CupInput(up || left, down || right, ok, click, moved, mp);
                return;
            }

            // settings: 0 sound, the teams, full screen, the helpers, back
            int rowsN = SettingsRows;
            if (up) pick = (pick + rowsN - 1) % rowsN;
            if (down) pick = (pick + 1) % rowsN;
            for (int i = 0; i < rows.Length; i++)
                if ((moved || click) && rows[i].Contains(mp)) pick = i;
            if (pick == 0)
            {
                bool clickRow = click && rows[0].Contains(mp);
                if (left || (clickRow && mp.x < rows[0].center.x)) Volume(-1);
                if (right || ok || (clickRow && mp.x >= rows[0].center.x)) Volume(+1);
            }
            else if (pick >= 1 && pick < ScreenRow)
            {
                bool clickRow = click && rows[pick].Contains(mp);
                if (left || (clickRow && mp.x < rows[pick].center.x)) PickTeam(pick == 1, -1);
                if (right || ok || (clickRow && mp.x >= rows[pick].center.x)) PickTeam(pick == 1, +1);
            }
            else if (pick == ScreenRow && (ok || left || right || (click && rows[pick].Contains(mp)))) Screen.fullScreen = !Screen.fullScreen;
            else if (pick >= HelperRow && pick < rowsN - 1 && (ok || left || right || (click && rows[pick].Contains(mp)))) Toggle(pick - HelperRow);
            else if (pick == rowsN - 1 && (ok || (click && rows[rowsN - 1].Contains(mp)))) Open(Page.Menu);
        }

        bool Over(Vector2 mp)
        {
            foreach (var r in hit) if (r.Contains(mp)) return true;
            return false;
        }

        void Choose(Item i)
        {
            if (i == Item.Play) { CupHost.SetLive(false); fadeT = 0f; Debug.Log("[title] PLAY"); }
            else if (i == Item.Cup) { cup = CupHost.Load(cfg); Open(Page.Cup); }
            else Open(i == Item.HowTo ? Page.HowTo : Page.Settings);
        }

        void Open(Page p)
        {
            if (p == Page.Menu)
            {
                var from = page == Page.HowTo ? Item.HowTo : page == Page.Settings ? Item.Settings : page == Page.Cup ? Item.Cup : Item.Play;
                pick = Mathf.Max(0, System.Array.IndexOf(items, from));
            }
            else pick = 0;
            confirmNew = false;
            page = p;
        }

        void Volume(int dir)
        {
            float step = Mathf.Max(0.01f, cfg.title.volumeStep);
            float v = Mathf.Clamp01(Mathf.Round((AudioListener.volume + dir * step) / step) * step);
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(VolumeKey, v);
            PlayerPrefs.Save();
        }

        // The next or last team for one side (never the other side's team), saved for the game after PLAY.
        void PickTeam(bool awaySide, int dir)
        {
            if (awaySide) away = TeamPick.Step(cfg, away, home, dir);
            else home = TeamPick.Step(cfg, home, away, dir);
            var league = TeamPick.League(cfg);
            PlayerPrefs.SetString(AwayKey, league[away].@short);
            PlayerPrefs.SetString(HomeKey, league[home].@short);
            PlayerPrefs.Save();
        }

        // One helper on or off: saved for every later game (the show game behind the title switches it right away too).
        void Toggle(int i)
        {
            string name = FlightMarks.Switches[i];
            bool on = !cfg.Feature(name);
            cfg.features[name] = on;
            PlayerPrefs.SetInt(HelperKey + name, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        // The real game: the Boot scene again, built fresh from game.json, opening on the lobby.
        void StartGame()
        {
            done = true;
            Debug.Log("[title] starting the game");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---- the camera: a new angle at every huddle and every line-up ----
        void LateUpdate()
        {
            var w = host != null ? host.World : null;
            var shots = cfg.title.shots;
            if (w == null || rig == null || shots.Count == 0) return;
            bool cut = phaseKnown && w.Phase != lastPhase && (w.Phase == Phase.Huddle || w.Phase == Phase.Set);
            phaseKnown = true;
            lastPhase = w.Phase;
            shotT += Time.deltaTime;
            if (shot < 0 || (cut && shotT >= cfg.title.minShotS))
            {
                shot = TitleShots.Next(shots.Count, shot, Random.value);
                shotT = 0f;
                fx = float.NaN;
            }
            var s = shots[shot];
            if (s.broadcast) { if (rig.Held) rig.Release(); return; }

            var carrier = LivePlay.Carrier(w);
            float tx = carrier != null ? carrier.X : w.Ball.X, tz = carrier != null ? carrier.Z : w.Ball.Z;
            if (float.IsNaN(fx)) { fx = tx; fz = tz; }
            float k = 1f - Mathf.Exp(-Time.deltaTime * cfg.title.followRate);
            fx += (tx - fx) * k;
            fz += (tz - fz) * k;
            var p = TitleShots.Pose(cfg, s, fx, fz, w.Dir);
            rig.Hold(p.position, p.lookAt, p.fovDeg);
        }

        // ---- the TV ----
        GUIStyle head, body, row, hint, small;
        readonly Rect[] rows = new Rect[8];

        void OnGUI()
        {
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            GUI.depth = -100;   // over every other overlay
            float W = Screen.width, H = Screen.height, s = H / 1080f;
            var t = cfg.title;
            head ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            body ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            row ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            hint ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
            head.fontSize = Mathf.RoundToInt(56 * s);
            body.fontSize = Mathf.RoundToInt(30 * s);
            row.fontSize = Mathf.RoundToInt(40 * s);
            hint.fontSize = Mathf.RoundToInt(22 * s);

            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, page == Page.Menu ? t.tint : Mathf.Max(t.tint, 0.55f)));
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, richText = true };
            small.fontSize = Mathf.RoundToInt(24 * s);

            if (page == Page.Menu) DrawMenu(W, H, s, t);
            else if (page == Page.HowTo) DrawHowTo(W, H, s);
            else if (page == Page.Cup) DrawCup(W, H, s);
            else DrawSettings(W, H, s);

            if (fadeT >= 0f)
            {
                // stepped, 8 a second (no smooth tweens: the house style)
                float a = Mathf.Clamp01(Mathf.Floor(fadeT * 8f) / Mathf.Max(1f, Mathf.Ceil(t.fadeS * 8f)));
                Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, a));
            }
        }

        void DrawMenu(float W, float H, float s, TitleConfig t)
        {
            float y = H * t.logoTopFrac;
            if (logo != null)
            {
                // a fourth button (POCKET CUP): the logo gives up the room, so the buttons stay clear of the hint
                float below = t.playHeightFrac + (items.Length - 1) * t.otherHeightFrac + (items.Length + 1) * t.gapFrac;
                float fit = Mathf.Max(0.2f, t.menuBottomFrac - t.logoTopFrac - below);
                float lh = H * Mathf.Min(t.logoHeightFrac, fit), lw = lh * logo.width / logo.height;
                if (lw > W * 0.94f) { lw = W * 0.94f; lh = lw * logo.height / logo.width; }
                GUI.DrawTexture(new Rect((W - lw) * 0.5f, y, lw, lh), logo);
                y += lh + H * t.gapFrac * 2f;
            }
            for (int i = 0; i < items.Length; i++)
            {
                var tex = buttons[i];
                float bh = H * (items[i] == Item.Play ? t.playHeightFrac : t.otherHeightFrac);
                float bw = tex != null ? bh * tex.width / tex.height : bh * 4f;
                var r = new Rect((W - bw) * 0.5f, y, bw, bh);
                hit[i] = r;
                y += bh + H * t.gapFrac;
                bool on = i == pick;
                if (on)
                {
                    float g = t.hoverScale;
                    r = new Rect(r.center.x - bw * g * 0.5f, r.center.y - bh * g * 0.5f, bw * g, bh * g);
                }
                GUI.color = on ? Color.white : new Color(t.dimOthers, t.dimOthers, t.dimOthers, 1f);
                if (tex != null) GUI.DrawTexture(r, tex);
                else GUI.Label(r, ButtonText[(int)items[i]], row);
                GUI.color = Color.white;
            }
            Shadowed(new Rect(0, H - 44 * s, W, 34 * s), "Mouse, arrows + Enter, or a controller", hint);
        }

        Rect Panel(float W, float H, float s, string title)
        {
            float pw = Mathf.Min(W - 64 * s, 1240 * s), ph = H * 0.82f;
            var r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
            Fill(r, new Color(0.04f, 0.09f, 0.22f, 0.94f));                                   // navy, like the buttons
            Fill(new Rect(r.x, r.y, r.width, 6 * s), new Color(1f, 0.78f, 0.12f, 1f));          // gold edges
            Fill(new Rect(r.x, r.yMax - 6 * s, r.width, 6 * s), new Color(1f, 0.78f, 0.12f, 1f));
            head.normal.textColor = new Color(1f, 0.82f, 0.2f);
            GUI.Label(new Rect(r.x, r.y + 24 * s, r.width, 70 * s), title, head);
            return r;
        }

        static readonly string[] HowTo =
        {
            "<b>1. Join.</b> Press PLAY. Everyone scans the QR code on the TV with their phone (same Wi-Fi as the TV).",
            "<b>2. Pick a side.</b> The two teams on the TV (change them with the arrows on the join screen or under SETTINGS, or play through the POCKET CUP). Bots play every spot nobody took.",
            "<b>3. Lobby.</b> Pick your offense spot and your defense spot. Any phone taps KICK OFF to start.",
            "<b>4. Huddle.</b> Call a play on your phone in secret. The TV only shows CALLED, never which play.",
            "<b>5. Offense.</b> QB: tap HIKE, then a receiver's shape to throw (tap = bullet, hold = lob). With the ball: SPRINT, JUKE, SPIN, STIFF ARM.",
            "<b>6. Defense.</b> Chase the ball and tap TACKLE. Try SWAT, SWIM or DIVE.",
            "<b>7. Lost?</b> Tap FIND ME: your player lights up on the TV.",
        };

        void DrawHowTo(float W, float H, float s)
        {
            var r = Panel(W, H, s, "HOW TO PLAY");
            float pad = 56 * s, y = r.y + 120 * s;
            body.normal.textColor = Color.white;
            foreach (var line in HowTo)
            {
                float h = body.CalcHeight(new GUIContent(line), r.width - 2 * pad);
                GUI.Label(new Rect(r.x + pad, y, r.width - 2 * pad, h), line, body);
                y += h + 14 * s;
            }
            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), "Click, Enter or (A) to go back", hint);
        }

        void DrawSettings(float W, float H, float s)
        {
            var r = Panel(W, H, s, "SETTINGS");
            var text = new string[SettingsRows];
            text[0] = "SOUND   ◄  " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%  ►";
            var league = TeamPick.League(cfg);
            if (TeamRows > 0)
            {
                text[1] = "AWAY   ◄  " + league[away].name + "  ►";
                text[2] = "HOME   ◄  " + league[home].name + "  ►";
            }
            text[ScreenRow] = "FULL SCREEN   " + (Screen.fullScreen ? "ON" : "OFF");
            for (int i = 0; i < HelperNames.Length; i++) text[HelperRow + i] = HelperNames[i] + "   " + (cfg.Feature(FlightMarks.Switches[i]) ? "ON" : "OFF");
            text[text.Length - 1] = "BACK";
            bool tight = text.Length > 6;   // (the team rows: a little less room each, so all fit above the hint)
            float rw = Mathf.Min(r.width - 120 * s, 760 * s), rh = (tight ? 62 : 70) * s, gap = (tight ? 10 : 14) * s, y = r.y + 150 * s;
            for (int i = 0; i < text.Length; i++)
            {
                var rr = new Rect((W - rw) * 0.5f, y, rw, rh);
                rows[i] = rr;
                bool on = i == pick;
                Fill(rr, on ? new Color(1f, 0.78f, 0.12f, 1f) : new Color(1f, 1f, 1f, 0.08f));
                if (i >= 1 && i < ScreenRow)
                {
                    // the team's two colours, a stripe each at the row's left end
                    var t = league[i == 1 ? away : home];
                    float sw = 18 * s, sx = rr.x + 14 * s, sy = rr.y + 10 * s, sh = rh - 20 * s;
                    Fill(new Rect(sx, sy, sw, sh), Colors.Hex(t.color));
                    Fill(new Rect(sx + sw, sy, sw, sh), Colors.Hex(t.color2));
                }
                row.normal.textColor = on ? new Color(0.04f, 0.09f, 0.22f) : Color.white;
                GUI.Label(rr, text[i], row);
                y += rh + gap;
            }
            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), "Up / Down to pick, Left / Right to change, Esc or (B) to go back", hint);
        }

        // ---- the Pocket Cup page (FEATURES.cup): the bracket, and PLAY NEXT GAME / NEW CUP / BACK under it ----
        Cup cup;                 // the saved cup (null: none yet)
        bool confirmNew;         // NEW CUP pressed once while a cup is under way: the second press starts over
        readonly Rect[] cupHit = new Rect[3];
        readonly System.Collections.Generic.Dictionary<string, Texture2D> logos = new System.Collections.Generic.Dictionary<string, Texture2D>();

        enum CupButton { Next, New, Back }

        CupButton[] CupButtons()
        {
            if (cup != null && !cup.Over && cup.Next() != null) return new[] { CupButton.Next, CupButton.New, CupButton.Back };
            return new[] { CupButton.New, CupButton.Back };
        }

        void CupInput(bool prev, bool next, bool ok, bool click, bool moved, Vector2 mp)
        {
            var b = CupButtons();
            int n = b.Length;
            if (pick >= n) pick = 0;
            if (prev) pick = (pick + n - 1) % n;
            if (next) pick = (pick + 1) % n;
            bool onButton = false;
            for (int i = 0; i < n; i++)
                if ((moved || click) && cupHit[i].Contains(mp)) { pick = i; onButton = true; }
            if (click && onButton) ok = true;
            if (!ok) return;
            var what = b[pick];
            if (what == CupButton.Back) { Open(Page.Menu); return; }
            if (what == CupButton.New)
            {
                if (cup != null && !cup.Over && !confirmNew) { confirmNew = true; return; }
                cup = CupHost.StartNew(cfg);
                confirmNew = false;
                pick = 0;   // PLAY NEXT GAME
                return;
            }
            // the cup's next game: the Boot scene again, wearing its two teams, opening on the lobby
            confirmNew = false;
            CupHost.SetLive(true);
            fadeT = 0f;
            Debug.Log("[title] POCKET CUP: next game");
        }

        TeamConfig TeamOf(string code)
        {
            int i = TeamPick.Find(cfg, code);
            return i >= 0 ? TeamPick.League(cfg)[i] : null;
        }

        Texture2D LogoOf(string code)
        {
            if (!logos.TryGetValue(code, out var tex)) { tex = Resources.Load<Texture2D>("Logos/" + code); logos[code] = tex; }
            return tex;
        }

        void DrawCup(float W, float H, float s)
        {
            var r = Panel(W, H, s, "POCKET CUP");
            var next = cup?.Next();
            float pad = 48 * s;
            // the line under the title: what's next
            string sub = cup == null ? "Every team in one knockout bracket. Win and go through; lose and you're out."
                : cup.Over ? "CHAMPIONS: " + (TeamOf(cup.Champion)?.name ?? cup.Champion).ToUpperInvariant()
                : next != null ? cup.RoundName(next.Round) + ":  " + Name(next.Away) + "  at  " + Name(next.Home) : "";
            hint.normal.textColor = Color.white;
            Shadowed(new Rect(r.x, r.y + 92 * s, r.width, 36 * s), sub, hint);

            // the bracket: a column a round, then the champion
            float top = r.y + 140 * s, bottom = r.yMax - 170 * s;
            if (cup != null) DrawBracket(new Rect(r.x + pad, top, r.width - 2 * pad, bottom - top), s, next);
            else
            {
                body.normal.textColor = Color.white;
                var teams = CupHost.Teams(cfg);
                float lw = Mathf.Min(120 * s, (r.width - 2 * pad) / Mathf.Max(1, teams.Count) - 12 * s), x = r.x + (r.width - teams.Count * (lw + 12 * s)) * 0.5f;
                for (int i = 0; i < teams.Count; i++)
                {
                    var logo = LogoOf(teams[i]);
                    var lr = new Rect(x + i * (lw + 12 * s), (top + bottom) * 0.5f - lw * 0.5f, lw, lw);
                    if (logo != null) GUI.DrawTexture(lr, logo, ScaleMode.ScaleToFit);
                    else { var t = TeamOf(teams[i]); if (t != null) Fill(lr, Colors.Hex(t.color)); }
                }
                Shadowed(new Rect(r.x, (top + bottom) * 0.5f + lw * 0.5f + 16 * s, r.width, 40 * s), teams.Count + " teams. Press NEW CUP to draw the bracket.", hint);
            }

            // the buttons, side by side
            var b = CupButtons();
            float bw = Mathf.Min(360 * s, (r.width - 2 * pad - (b.Length - 1) * 20 * s) / b.Length), bh = 70 * s;
            float bx = r.x + (r.width - (b.Length * bw + (b.Length - 1) * 20 * s)) * 0.5f, by = r.yMax - 160 * s;
            for (int i = 0; i < cupHit.Length; i++) cupHit[i] = Rect.zero;
            for (int i = 0; i < b.Length; i++)
            {
                var br = new Rect(bx + i * (bw + 20 * s), by, bw, bh);
                cupHit[i] = br;
                bool on = i == pick;
                Fill(br, on ? CupColors.Gold : new Color(1f, 1f, 1f, 0.08f));
                row.normal.textColor = on ? CupColors.NavyText : Color.white;
                string label = b[i] == CupButton.Next ? "PLAY NEXT GAME" : b[i] == CupButton.Back ? "BACK"
                    : confirmNew ? "SURE? PRESS AGAIN" : "NEW CUP";
                int was = row.fontSize;
                row.fontSize = Mathf.RoundToInt(32 * s);
                GUI.Label(br, label, row);
                row.fontSize = was;
            }
            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), "Left / Right to pick, Enter or (A) to press, Esc or (B) to go back", hint);
        }

        string Name(string code) => (TeamOf(code)?.name ?? code).ToUpperInvariant();

        // Every match as two team boxes, joined by lines to the box its winner moves into.
        void DrawBracket(Rect area, float s, CupMatch next)
        {
            int cols = cup.Rounds + 1;
            float colGap = 36 * s, colW = (area.width - colGap * (cols - 1)) / cols;
            float boxH = Mathf.Min(64 * s, area.height / cup.Size * 0.8f);
            var line = new Color(1f, 0.78f, 0.12f, 0.55f);
            float lw = Mathf.Max(2f, 3 * s);
            small.fontSize = Mathf.RoundToInt(Mathf.Min(26 * s, boxH * 0.42f));

            for (int round = 0; round < cup.Rounds; round++)
            {
                float x = area.x + round * (colW + colGap);
                int slots = cup.Size >> round;
                float slotH = area.height / slots;
                small.normal.textColor = CupColors.GoldText;
                small.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(x, area.y - 30 * s, colW, 28 * s), cup.RoundName(round), small);
                small.alignment = TextAnchor.MiddleLeft;
                for (int m = 0; m < cup.Matches(round); m++)
                {
                    string winner = cup.Winner(round, m);
                    bool isNext = next != null && next.Round == round && next.Index == m;
                    float ya = area.y + (m * 2 + 0.5f) * slotH, yb = ya + slotH;
                    for (int side = 0; side < 2; side++)
                    {
                        float cy = side == 0 ? ya : yb;
                        var box = new Rect(x, cy - boxH * 0.5f, colW, boxH);
                        string code = cup.Team(round, m, side);
                        bool bye = cup.IsBye(round, m, side);
                        TeamBox(box, code, bye ? "BYE" : "", code != null && winner != null && winner != code, code != null && winner == code && !IsByeMatch(round, m), isNext, s);
                    }
                    // the joining lines: out of each box, down the middle, on to the next round's box
                    float xr = x + colW, xm = xr + colGap * 0.5f;
                    Fill(new Rect(xr, ya - lw * 0.5f, colGap * 0.5f, lw), line);
                    Fill(new Rect(xr, yb - lw * 0.5f, colGap * 0.5f, lw), line);
                    Fill(new Rect(xm - lw * 0.5f, ya, lw, yb - ya), line);
                    Fill(new Rect(xm, (ya + yb) * 0.5f - lw * 0.5f, colGap * 0.5f, lw), line);
                }
            }
            // the champion's box, level with the final
            float cx = area.x + cup.Rounds * (colW + colGap);
            var champ = new Rect(cx, area.y + area.height * 0.5f - boxH * 0.5f, colW, boxH);
            small.normal.textColor = CupColors.GoldText;
            small.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(cx, champ.y - 34 * s, colW, 28 * s), "CHAMPION", small);
            small.alignment = TextAnchor.MiddleLeft;
            TeamBox(champ, cup.Champion, "?", false, cup.Champion != null, false, s);
        }

        bool IsByeMatch(int round, int m) => cup.IsBye(round, m, 0) || cup.IsBye(round, m, 1);

        // One team in the bracket: its colours, its logo, its seed and name. Empty: still to be decided (or a bye).
        void TeamBox(Rect box, string code, string empty, bool lost, bool won, bool next, float s)
        {
            var t = code != null ? TeamOf(code) : null;
            if (next) Fill(new Rect(box.x - 4 * s, box.y - 4 * s, box.width + 8 * s, box.height + 8 * s), CupColors.Gold);
            Fill(box, won ? new Color(0.16f, 0.13f, 0.03f, 1f) : new Color(0.02f, 0.05f, 0.13f, 1f));
            if (won) { Fill(new Rect(box.x, box.y, box.width, 3 * s), CupColors.Gold); Fill(new Rect(box.x, box.yMax - 3 * s, box.width, 3 * s), CupColors.Gold); }
            if (t == null)
            {
                small.normal.textColor = new Color(1f, 1f, 1f, 0.35f);
                GUI.Label(new Rect(box.x + 16 * s, box.y, box.width - 16 * s, box.height), empty, small);
                return;
            }
            float sw = 10 * s;
            Fill(new Rect(box.x, box.y, sw, box.height), Colors.Hex(t.color));
            Fill(new Rect(box.x + sw, box.y, sw * 0.6f, box.height), Colors.Hex(t.color2));
            float lx = box.x + sw * 1.6f + 6 * s, ls = box.height - 6 * s;
            var logo = LogoOf(code);
            if (logo != null)
            {
                if (lost) GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(lx, box.y + 3 * s, ls, ls), logo, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                lx += ls + 6 * s;
            }
            small.normal.textColor = lost ? new Color(1f, 1f, 1f, 0.35f) : won ? CupColors.GoldText : Color.white;
            GUI.Label(new Rect(lx, box.y, box.xMax - lx - 4 * s, box.height), "<size=" + Mathf.RoundToInt(small.fontSize * 0.7f) + ">" + cup.SeedOf(code) + "</size>  " + t.name.ToUpperInvariant(), small);
        }

        void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, black);
            GUI.color = Color.white;
        }

        static void Shadowed(Rect r, string text, GUIStyle st)
        {
            var was = st.normal.textColor;
            st.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            st.normal.textColor = Color.white;
            GUI.Label(r, text, st);
            st.normal.textColor = was;
        }
    }
}
