using System.Collections.Generic;
using System.IO;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Boot
{
    // The first-drive tutorial (FEATURES.tutorial, NO_TUTORIAL=1 hides it): the title's TUTORIAL button (or -tutorial).
    // One phone, four short drills from Config/tutorial.json (throw a pass, run with the ball, make a tackle, one real
    // 11-on-11 play), each with big words on the TV saying which buttons to press, then YOU'RE READY and back to the
    // title. The drills are the practice lab's (Sim/Lab.cs: the game's own rules); Sim/Tutorial.cs knows the goals.
    // The TV shows the join code until a phone joins (ENTER: play with the keyboard instead: WASD, SPACE hike, 1-4
    // throw, F tackle, E juke). TAB skips a step. Esc is the pause menu as in a game (MAIN MENU leaves).
    // No lobby, game clock, replays, stats or game log in it: Prepare switches them off before the world is built.
    public sealed class TutorialMode : MonoBehaviour
    {
        public static TutorialMode Instance { get; private set; }
        static bool pending;   // TUTORIAL was pressed on the title: the reloaded scene runs the tutorial

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pending = false; Instance = null; }

        static readonly string[] Off = { "lobby", "clock", "replay", "stats", "gamelog", "gamepad" };

        public static bool On(GameConfig cfg) => cfg.Feature("tutorial");

        // The title's TUTORIAL button: the next scene load runs it.
        public static void Request() => pending = true;

        public static bool Requested(GameConfig cfg)
        {
            if (!On(cfg)) return false;
            if (pending) return true;
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-tutorial") return true;
            return false;
        }

        // Before the host is built (like the title's attract game): the switches a tutorial plays without.
        public static void Prepare(GameConfig cfg)
        {
            pending = false;
            foreach (var f in Off) cfg.features[f] = false;
        }

        public Tutorial Tut { get; private set; }
        GameConfig game;
        PartyHost host;
        int synced = -1;
        bool hike, tackle, juke, leaving;
        int throwTo;
        static readonly Key[] ThrowKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        public static TutorialMode Create(GameObject on, GameConfig game, PartyHost host, string configDir)
        {
            var m = on.AddComponent<TutorialMode>();
            Instance = m;
            m.game = game;
            m.host = host;
            string path = Path.Combine(configDir, "tutorial.json");
            var tc = Tutorial.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}");
            m.Tut = new Tutorial(host.World, tc);
            if (!m.Tut.Ready) { Debug.LogWarning("[tutorial] no steps in " + path); return m; }
            host.DevKeys = false;
            host.BeforeTick += m.Feed;
            host.AfterTick += m.Tut.Lab.AfterTick;
            host.AfterTick += m.Tut.AfterTick;
            m.Tut.Lab.YouSlot = m.PreferredSlot();
            m.Tut.Setup();
            Debug.Log("[tutorial] up: " + tc.steps.Count + " steps");
            return m;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // The first phone that has joined plays; with none, the keyboard.
        int PreferredSlot()
        {
            foreach (var s in host.Roster.Seats) if (s.Kind == SeatKind.Phone && s.Connected) return s.Slot;
            return Tut.C.lab.keyboardSlot;
        }

        bool HasPhone => PreferredSlot() != Tut.C.lab.keyboardSlot;

        void Update()
        {
            if (Tut == null || !Tut.Ready || leaving || PauseMenu.Open) return;
            var kb = Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) Tut.Skip();
                if (kb.spaceKey.wasPressedThisFrame) hike = true;
                if (kb.fKey.wasPressedThisFrame) tackle = true;
                if (kb.eKey.wasPressedThisFrame) juke = true;
                for (int k = 0; k < ThrowKeys.Length; k++) if (kb[ThrowKeys[k]].wasPressedThisFrame) throwTo = k + 1;
            }

            // a phone came or went: the step starts again with the right person in it
            var lab = Tut.Lab;
            int want = PreferredSlot();
            var me = lab.You >= 0 ? host.World.Find(lab.You) : null;
            bool lost = lab.You >= 0 && (me == null || !me.Human);
            if (want != lab.YouSlot || lost) { lab.YouSlot = want; lab.Again(); }
            if (!Tut.Started && (HasPhone || enter)) { Tut.Begin(); Debug.Log("[tutorial] started with " + (HasPhone ? "a phone" : "the keyboard")); }
            else if (Tut.ReadyToLeave || (Tut.Finished && enter)) Leave();
            SyncPhone();
        }

        // Back to the title (a fresh Boot scene, like the pause menu's MAIN MENU).
        void Leave()
        {
            leaving = true;
            Debug.Log("[tutorial] done: back to the title");
            TitleScreen.ReturnTo(null);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // The phone is told its team and number after every setup (the steps move it around).
        void SyncPhone()
        {
            var lab = Tut.Lab;
            if (synced == lab.Starts) return;
            synced = lab.Starts;
            var seat = host.Roster[lab.You];
            var p = lab.You >= 0 ? host.World.Find(lab.You) : null;
            if (seat == null || p == null || seat.Conn < 0) return;
            seat.Team = p.Team;
            host.Server.Send(seat.Conn, Messages.Picked(seat, p, game));
        }

        // Before each tick: the keyboard plays "you" too (added to the phone's own input).
        void Feed(Dictionary<int, PlayerInput> inputs)
        {
            var lab = Tut.Lab;
            if (lab.You < 0) return;
            inputs.TryGetValue(lab.You, out var i);
            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                if (x != 0f || z != 0f)
                {
                    float len = Mathf.Sqrt(x * x + z * z);
                    i.MoveX = x / len; i.MoveZ = z / len;
                }
                i.Sprint |= kb.shiftKey.isPressed;
            }
            i.Hike |= hike; i.Tackle |= tackle; i.Juke |= juke;
            if (throwTo > 0) i.Throw = throwTo;
            hike = tackle = juke = false;
            throwTo = 0;
            inputs[lab.You] = i;
        }

        // ---- the TV ----
        GUIStyle head, body, hint, big;
        Texture2D white;

        void OnGUI()
        {
            if (Tut == null || !Tut.Ready || PauseMenu.Open) return;
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            GUI.depth = -80;
            float W = Screen.width, H = Screen.height, s = H / 1080f;
            white ??= Texture2D.whiteTexture;
            head ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            body ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true, wordWrap = true };
            hint ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            head.fontSize = Mathf.RoundToInt(50 * s);
            body.fontSize = Mathf.RoundToInt(32 * s);
            hint.fontSize = Mathf.RoundToInt(22 * s);
            big.fontSize = Mathf.RoundToInt(120 * s);
            body.normal.textColor = Color.white;

            if (!Tut.Started) { DrawIntro(W, H, s); return; }
            if (Tut.Finished) { DrawFinish(W, H, s); return; }
            DrawStep(W, H, s);
            if (Tut.Done) UiFont.Toon(new Rect(0, H * 0.36f, W, 160 * s), Tut.Title, big, CupColors.GoldText, UiFont.On ? 5f * s : 0f);
        }

        // Before a phone joins: the join code big in the middle, the intro words beside it.
        void DrawIntro(float W, float H, float s)
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.55f));
            float qr = 420 * s, pad = 36 * s, textW = 640 * s;
            float pw = pad + qr + pad + textW + pad, ph = pad + 90 * s + qr + 230 * s + pad;
            var r = Panel(new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph), s);
            UiFont.Toon(new Rect(r.x, r.y + pad, r.width, 70 * s), Tut.Title, head, CupColors.GoldText, UiFont.On ? 3f * s : 0f);
            var q = new Rect(r.x + pad, r.y + pad + 90 * s, qr, qr);
            Fill(new Rect(q.x - 10 * s, q.y - 10 * s, qr + 20 * s, qr + 20 * s), Color.white);
            if (host.Qr != null) GUI.DrawTexture(q, host.Qr);
            GUI.Label(new Rect(q.x, q.yMax + 20 * s, qr, 210 * s), host.JoinText(s), body);
            GUI.Label(new Rect(q.xMax + pad, q.y, textW, qr), Tut.Text, body);
        }

        // A step: its title and words in a band across the top, the progress under them.
        void DrawStep(float W, float H, float s)
        {
            float pw = Mathf.Min(W - 64 * s, 1500 * s), pad = 28 * s;
            float th = body.CalcHeight(new GUIContent(Tut.Text), pw - 2 * pad);
            var r = Panel(new Rect((W - pw) * 0.5f, 24 * s, pw, pad + 64 * s + th + 44 * s + pad), s);
            UiFont.Toon(new Rect(r.x, r.y + pad, r.width, 60 * s), Tut.Current.title, head, CupColors.GoldText, UiFont.On ? 3f * s : 0f);
            GUI.Label(new Rect(r.x + pad, r.y + pad + 64 * s, r.width - 2 * pad, th), Tut.Text, body);
            Shadowed(new Rect(r.x, r.yMax - pad - 34 * s, r.width, 34 * s), "TAB skips this step   ·   Esc for the menu", hint);
        }

        void DrawFinish(float W, float H, float s)
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.55f));
            float pw = Mathf.Min(W - 64 * s, 1100 * s), pad = 40 * s;
            float th = body.CalcHeight(new GUIContent(Tut.Text), pw - 2 * pad);
            float ph = pad + 150 * s + th + 70 * s + pad;
            var r = Panel(new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph), s);
            UiFont.Toon(new Rect(r.x, r.y + pad, r.width, 130 * s), Tut.Title, big, CupColors.GoldText, UiFont.On ? 5f * s : 0f);
            GUI.Label(new Rect(r.x + pad, r.y + pad + 150 * s, r.width - 2 * pad, th), Tut.Text, body);
            Shadowed(new Rect(r.x, r.yMax - pad - 40 * s, r.width, 40 * s), "Back to the title in a moment (or press ENTER)", hint);
        }

        // A navy panel with gold edges, like the title's pages.
        Rect Panel(Rect r, float s)
        {
            Fill(r, CupColors.Navy);
            Fill(new Rect(r.x, r.y, r.width, 6 * s), CupColors.Gold);
            Fill(new Rect(r.x, r.yMax - 6 * s, r.width, 6 * s), CupColors.Gold);
            return r;
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
