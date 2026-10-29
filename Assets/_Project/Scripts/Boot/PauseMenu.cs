using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PocketFootball.Boot
{
    // The pause menu (FEATURES.pauseMenu, NO_PAUSE_MENU=1: Esc quits the game as before). Esc in the game (the lobby
    // too) stops everything and opens it: CONTINUE, SETTINGS (sound, full screen, the helpers), MAIN MENU (the title;
    // a Pocket Cup game goes back to the bracket and is played again later) and QUIT GAME. The last two ask twice.
    // Esc again (or CONTINUE) carries on where it stopped. The keyboard or the mouse only: a pad's START joins the game
    // and its buttons play, so the pads never open it. Not in the lab (P pauses there), nor in tool runs.
    // The look is the title's pages: a navy panel with gold edges, the highlighted row gold.
    public sealed class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }
        public static bool Open => Instance != null && Instance.open;

        GameConfig cfg;
        PartyHost host;
        PauseMenuModel menu;
        bool open, settings, devKeysWas;
        int settingsPick;
        float scaleWas = 1f;
        Texture2D white;

        // settings rows: sound, full screen, the helpers, back
        int HelperRow => 2;
        int SettingsRows => HelperRow + FlightMarks.Switches.Length + 1;

        public static bool Requested(GameConfig cfg, CliArgs args, bool title)
        {
            if (!cfg.Feature("pauseMenu") || title || LabMode.Requested() || Application.isBatchMode) return false;
            return string.IsNullOrEmpty(args.ShotsDir) && PhotoScenes.Requested(args) == null;
        }

        public static PauseMenu Attach(GameObject on, GameConfig cfg, PartyHost host)
        {
            var p = on.AddComponent<PauseMenu>();
            p.cfg = cfg;
            p.host = host;
            p.menu = new PauseMenuModel(cfg);
            p.white = Texture2D.whiteTexture;
            Instance = p;
            return p;
        }

        void OnDestroy()
        {
            if (open) Resume();
            if (Instance == this) Instance = null;
        }

        // ---- pausing ----
        // Time stops (the sim steps on game time, PartyHost.Update, so the clock and the play stop with it), the sound
        // stops, and the TV's dev keys rest so the menu's keys press nothing else.
        void Pause()
        {
            open = true;
            settings = false;
            menu.Reset();
            scaleWas = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (host != null) { devKeysWas = host.DevKeys; host.DevKeys = false; }
            Debug.Log("[pause] paused");
        }

        void Resume()
        {
            open = false;
            Time.timeScale = scaleWas > 0f ? scaleWas : 1f;
            AudioListener.pause = false;
            if (host != null) host.DevKeys = devKeysWas;
            Debug.Log("[pause] carrying on");
        }

        // ---- input ----
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool esc = kb.escapeKey.wasPressedThisFrame;
            if (!open)
            {
                if (esc) Pause();
                return;
            }
            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
            bool right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool ok = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;

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

            if (settings) { SettingsInput(esc, up, down, left, right, ok, click, moved, mp); return; }

            if (esc) { Resume(); return; }
            if (up) menu.Move(-1);
            if (down) menu.Move(+1);
            bool onRow = false;
            for (int i = 0; i < menu.Items.Count; i++)
                if ((moved || click) && rows[i].Contains(mp)) { menu.PickAt(i); onRow = true; }
            if (click && onRow) ok = true;
            if (!ok) return;
            var what = menu.Press();
            if (what == PauseMenuModel.Item.Continue) Resume();
            else if (what == PauseMenuModel.Item.Settings) { settings = true; settingsPick = 0; }
            else if (what == PauseMenuModel.Item.MainMenu) ToTitle();
            else if (what == PauseMenuModel.Item.Quit) { Debug.Log("[pause] QUIT GAME"); Application.Quit(0); }
        }

        void SettingsInput(bool esc, bool up, bool down, bool left, bool right, bool ok, bool click, bool moved, Vector2 mp)
        {
            int n = SettingsRows;
            if (esc) { settings = false; return; }
            if (up) settingsPick = (settingsPick + n - 1) % n;
            if (down) settingsPick = (settingsPick + 1) % n;
            for (int i = 0; i < n; i++)
                if ((moved || click) && rows[i].Contains(mp)) settingsPick = i;
            bool clickRow = click && rows[settingsPick].Contains(mp);
            if (settingsPick == 0)
            {
                if (left || (clickRow && mp.x < rows[0].center.x)) TitleScreen.StepVolume(cfg, -1);
                if (right || ok || (clickRow && mp.x >= rows[0].center.x)) TitleScreen.StepVolume(cfg, +1);
            }
            else if (settingsPick == 1 && (ok || left || right || clickRow)) Screen.fullScreen = !Screen.fullScreen;
            else if (settingsPick >= HelperRow && settingsPick < n - 1 && (ok || left || right || clickRow)) TitleScreen.ToggleHelper(cfg, settingsPick - HelperRow);
            else if (settingsPick == n - 1 && (ok || clickRow)) settings = false;
        }

        // The title again (a fresh Boot scene, like the title's own PLAY). A cup game opens the bracket page; its match
        // was never recorded, so PLAY NEXT GAME plays it again.
        void ToTitle()
        {
            Debug.Log("[pause] MAIN MENU");
            Resume();
            TitleScreen.ReturnTo(CupHost.Live != null ? "cup" : null);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---- the TV ----
        GUIStyle head, row, hint;
        readonly Rect[] rows = new Rect[8];

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -90;   // over the lobby, the pads' join screen and the post-game screen
            float W = Screen.width, H = Screen.height, s = H / 1080f;
            head ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            row ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            hint ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            head.fontSize = Mathf.RoundToInt(56 * s);
            row.fontSize = Mathf.RoundToInt(40 * s);
            hint.fontSize = Mathf.RoundToInt(22 * s);

            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, Mathf.Max(cfg.title.tint, 0.55f)));

            string[] text;
            int pick;
            if (settings)
            {
                text = new string[SettingsRows];
                text[0] = "SOUND   ◄  " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%  ►";
                text[1] = "FULL SCREEN   " + (Screen.fullScreen ? "ON" : "OFF");
                for (int i = 0; i < TitleScreen.HelperNames.Length; i++)
                    text[HelperRow + i] = TitleScreen.HelperNames[i] + "   " + (cfg.Feature(FlightMarks.Switches[i]) ? "ON" : "OFF");
                text[text.Length - 1] = "BACK";
                pick = settingsPick;
            }
            else
            {
                text = new string[menu.Items.Count];
                for (int i = 0; i < text.Length; i++) text[i] = menu.Label(i);
                pick = menu.Pick;
            }

            // the panel: as tall as its rows
            float rh = 70 * s, gap = 14 * s, top = 130 * s, bottom = 100 * s;
            float pw = Mathf.Min(W - 64 * s, 900 * s), ph = top + text.Length * (rh + gap) - gap + bottom;
            var r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
            Fill(r, CupColors.Navy);
            Fill(new Rect(r.x, r.y, r.width, 6 * s), CupColors.Gold);
            Fill(new Rect(r.x, r.yMax - 6 * s, r.width, 6 * s), CupColors.Gold);
            head.normal.textColor = CupColors.GoldText;
            GUI.Label(new Rect(r.x, r.y + 24 * s, r.width, 80 * s), settings ? "SETTINGS" : "PAUSED", head);

            float rw = Mathf.Min(r.width - 120 * s, 700 * s), y = r.y + top;
            for (int i = 0; i < rows.Length; i++) rows[i] = Rect.zero;
            for (int i = 0; i < text.Length; i++)
            {
                var rr = new Rect((W - rw) * 0.5f, y, rw, rh);
                rows[i] = rr;
                bool on = i == pick;
                Fill(rr, on ? CupColors.Gold : new Color(1f, 1f, 1f, 0.08f));
                row.normal.textColor = on ? CupColors.NavyText : Color.white;
                GUI.Label(rr, text[i], row);
                y += rh + gap;
            }
            Shadowed(new Rect(r.x + 24 * s, r.yMax - 76 * s, r.width - 48 * s, 56 * s),
                settings ? "Up / Down to pick, Left / Right to change, Esc to go back" : "Up / Down to pick, Enter or click to press, Esc to carry on", hint);
        }

        void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, white);
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
