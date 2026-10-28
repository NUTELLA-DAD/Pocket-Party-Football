using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PocketFootball.Boot
{
    // The title screen (FEATURES.titleScreen, NO_TITLE_SCREEN=1 goes straight to the lobby). Kenny's logo and three
    // buttons: PLAY, HOW TO PLAY, SETTINGS. Behind them the bots play a game of their own (attract mode), filmed from
    // a new angle every play (title.shots, View/TitleShots.cs). That game is only for show: no lobby, no game clock,
    // no replays, no game log, no pads joining, and a new seed every time. PLAY loads the Boot scene again, so the real
    // game starts fresh (a new world, a new room code) at the lobby; the title shows once per run.
    // Mouse, keyboard (arrows, Enter, Esc) or an Xbox pad (d-pad or stick, A, B). Esc on the menu quits as before.
    public sealed class TitleScreen : MonoBehaviour
    {
        public static TitleScreen Instance { get; private set; }
        public static bool Showing => Instance != null;
        static bool done;   // PLAY was pressed: the reloaded scene goes straight to the lobby

        // (the editor may keep statics between play sessions: every session starts on the title again)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { done = false; Instance = null; }

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

        enum Page { Menu, HowTo, Settings }

        GameConfig cfg;
        PartyHost host;
        CameraRig rig;
        Page page = Page.Menu;
        int pick;              // the button with the highlight (menu: 0 PLAY, 1 HOW TO PLAY, 2 SETTINGS; settings: its rows)
        float fadeT = -1f;     // PLAY pressed: the fade to black (-1: not fading)
        int escFrame = -1;
        Texture2D logo, black;
        Texture2D[] buttons;
        readonly Rect[] hit = new Rect[3];   // the menu buttons on screen (GUI space), for the mouse

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
            t.buttons = new[] { Resources.Load<Texture2D>("Title/ButtonPlay"), Resources.Load<Texture2D>("Title/ButtonHowToPlay"), Resources.Load<Texture2D>("Title/ButtonSettings") };
            t.black = Texture2D.whiteTexture;
            host.DevKeys = false;   // (W/A/S/D and the arrows move the highlight here, not the dev keys)
            t.away = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 0 ? cfg.teams[0].@short : null));
            t.home = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 1 ? cfg.teams[1].@short : null));
            if (t.home == t.away) t.home = TeamPick.Step(cfg, t.home, t.away, +1);
            if (startPage == "howto") t.page = Page.HowTo;
            else if (startPage == "settings") t.page = Page.Settings;
            Instance = t;
            Debug.Log("[title] title screen up (attract mode behind it)");
            return t;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // The saved sound level, helper switches and teams, for every boot (the title's settings page changes them).
        // PP_TEAMS=SAG,WHL (away, home) picks the teams for a run; the tests (batch mode) keep game.json's two.
        public static void ApplySavedSettings(GameConfig cfg)
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            FlightMarks.ApplySaved(cfg, n => PlayerPrefs.HasKey(HelperKey + n) ? PlayerPrefs.GetInt(HelperKey + n) != 0 : (bool?)null,
                System.Environment.GetEnvironmentVariable);
            if (TeamPick.ParsePair(System.Environment.GetEnvironmentVariable("PP_TEAMS"), out string a, out string h)) TeamPick.Apply(cfg, a, h);
            else if (!Application.isBatchMode) TeamPick.Apply(cfg, PlayerPrefs.GetString(AwayKey, ""), PlayerPrefs.GetString(HomeKey, ""));
            if (cfg.teams.Count >= 2) Debug.Log("[title] teams: " + cfg.teams[0].name + " at " + cfg.teams[1].name);
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
                if (up) pick = (pick + 2) % 3;
                if (down) pick = (pick + 1) % 3;
                for (int i = 0; i < hit.Length; i++)
                    if ((moved || click) && hit[i].Contains(mp)) { pick = i; if (click) ok = true; }
                if (click && !Over(mp)) ok = false;
                if (ok) Choose(pick);
                return;
            }

            if (back) { Open(Page.Menu); escFrame = Time.frameCount; return; }
            if (page == Page.HowTo)
            {
                if (ok || click) Open(Page.Menu);
                return;
            }

            // settings: 0 sound, the teams, full screen, the helpers, back
            int n = SettingsRows;
            if (up) pick = (pick + n - 1) % n;
            if (down) pick = (pick + 1) % n;
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
            else if (pick >= HelperRow && pick < n - 1 && (ok || left || right || (click && rows[pick].Contains(mp)))) Toggle(pick - HelperRow);
            else if (pick == n - 1 && (ok || (click && rows[n - 1].Contains(mp)))) Open(Page.Menu);
        }

        bool Over(Vector2 mp)
        {
            foreach (var r in hit) if (r.Contains(mp)) return true;
            return false;
        }

        void Choose(int i)
        {
            if (i == 0) { fadeT = 0f; Debug.Log("[title] PLAY"); }
            else Open(i == 1 ? Page.HowTo : Page.Settings);
        }

        void Open(Page p)
        {
            pick = p == Page.Menu ? (page == Page.HowTo ? 1 : page == Page.Settings ? 2 : 0) : 0;
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
        GUIStyle head, body, row, hint;
        readonly Rect[] rows = new Rect[8];

        void OnGUI()
        {
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

            if (page == Page.Menu) DrawMenu(W, H, s, t);
            else if (page == Page.HowTo) DrawHowTo(W, H, s);
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
                float lh = H * t.logoHeightFrac, lw = lh * logo.width / logo.height;
                if (lw > W * 0.94f) { lw = W * 0.94f; lh = lw * logo.height / logo.width; }
                GUI.DrawTexture(new Rect((W - lw) * 0.5f, y, lw, lh), logo);
                y += lh + H * t.gapFrac * 2f;
            }
            for (int i = 0; i < 3; i++)
            {
                var tex = buttons[i];
                float bh = H * (i == 0 ? t.playHeightFrac : t.otherHeightFrac);
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
                else GUI.Label(r, i == 0 ? "PLAY" : i == 1 ? "HOW TO PLAY" : "SETTINGS", row);
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
            "<b>2. Pick a side.</b> Arrows or Grains. Bots play every spot nobody took.",
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
