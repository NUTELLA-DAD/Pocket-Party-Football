using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PocketFootball.Boot
{
    // The Couch Cup between games (FEATURES.cup, NO_CUP=1 hides it; the bracket itself is Sim/Cup.cs). The title's
    // COUCH CUP page draws the bracket and starts each game. The cup is kept in PlayerPrefs, so it survives the scene
    // reloads between games and quitting the game: pp.cup is the bracket, pp.cup.live is 1 while the game the TV is
    // about to play (or playing) is the cup's next one. That game wears the two cup teams (Apply, at boot). When the
    // clock ends it, the winner goes into the bracket (Drive, from PostGameHost) and the post-game screen gets a gold
    // strip: who goes through, and Enter / (A) / a click for the bracket again.
    public static class CupHost
    {
        const string CupKey = "pp.cup", LiveKey = "pp.cup.live";

        public static CupMatch Live { get; private set; }   // the cup game being played this boot, else null
        static bool recorded;
        static string result;   // the gold strip's text, once the game is over

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Live = null; recorded = false; result = null; }

        public static bool On(GameConfig cfg) => cfg.Feature("cup") && cfg.Feature("teamPick") && Teams(cfg).Count >= 2;

        // The league's teams that play in a cup (the first cup.maxTeams), as short codes.
        public static List<string> Teams(GameConfig cfg)
        {
            var codes = new List<string>();
            int max = cfg.cup.maxTeams > 0 ? cfg.cup.maxTeams : int.MaxValue;
            foreach (var t in TeamPick.League(cfg)) if (codes.Count < max && !codes.Contains(t.@short)) codes.Add(t.@short);
            return codes;
        }

        // The saved cup, or null when there is none (or a team in it has left the league).
        public static Cup Load(GameConfig cfg)
        {
            var cup = Cup.Load(PlayerPrefs.GetString(CupKey, ""));
            if (cup == null) return null;
            foreach (var s in cup.Seeds) if (TeamPick.Find(cfg, s) < 0) return null;
            // teams added to the league since the draw fill the byes while those games are still to play
            bool added = false;
            foreach (var code in Teams(cfg))
                if (cup.SeedOf(code) == 0 && cup.AddTeam(code)) { added = true; Debug.Log("[cup] " + code + " joins the Couch Cup in a bye"); }
            if (added) Save(cup);
            return cup;
        }

        static void Save(Cup cup)
        {
            PlayerPrefs.SetString(CupKey, cup != null ? cup.Save() : "");
            PlayerPrefs.Save();
        }

        // A fresh draw of every team, saved over any cup there was.
        public static Cup StartNew(GameConfig cfg)
        {
            var cup = Cup.Draw(Teams(cfg), System.Environment.TickCount & 0x7fffffff, cfg.cup.randomDraw);
            Save(cup);
            Debug.Log("[cup] new Couch Cup: " + string.Join(", ", cup.Seeds));
            return cup;
        }

        // The next boot plays the cup's next game (true), or a plain game (false: the title's PLAY).
        public static void SetLive(bool on)
        {
            PlayerPrefs.SetInt(LiveKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        // At boot, before anything is built: a cup game wears its two teams. The title (or a tool run) never is one.
        public static void Apply(GameConfig cfg, bool title)
        {
            Live = null; recorded = false; result = null;
            if (title || Application.isBatchMode || !On(cfg) || PlayerPrefs.GetInt(LiveKey, 0) != 1) return;
            var cup = Load(cfg);
            var m = cup?.Next();
            if (m == null) { SetLive(false); return; }
            TeamPick.Apply(cfg, m.Away, m.Home);
            if (cfg.teams.Count < 2 || cfg.teams[0].@short != m.Away || cfg.teams[1].@short != m.Home) return;
            Live = m;
            cfg.overtime.mustWin = true;   // FEATURES.overtime: a cup game needs a winner, so overtime plays on until somebody scores
            Debug.Log("[cup] " + cup.RoundName(m.Round) + ": " + m.Away + " at " + m.Home);
        }

        // From PostGameHost, every frame: once the clock ends a cup game, its winner goes into the bracket (once).
        public static void Drive(PartyHost h, PostGame g)
        {
            if (Live == null || h.World == null) return;
            var w = h.World;
            if (!recorded && Clock.IsOver(w))
            {
                recorded = true;
                var cup = Load(w.Cfg);
                var m = cup?.Next();
                if (m != null && m.Round == Live.Round && m.Index == Live.Index)
                {
                    string winner = cup.Record(w.Score[0], w.Score[1]);
                    Save(cup);
                    SetLive(false);
                    var t = w.Cfg.teams[winner == Live.Home ? 1 : 0];
                    string name = t.name.ToUpperInvariant();
                    result = cup.Over ? name + " WIN THE COUCH CUP!"
                        : name + " GO THROUGH TO THE " + cup.RoundName(m.Round + 1);
                    if (w.Score[0] == w.Score[1]) result += "  (a tie: the higher seed goes through)";
                    Debug.Log("[cup] " + result);
                }
            }
            if (result == null || g == null || !g.Showing) return;
            var strip = h.GetComponent<CupStrip>();
            if (strip == null) strip = h.gameObject.AddComponent<CupStrip>();
            strip.Text = result;
        }

        // Back to the title's bracket page (a fresh scene: the post-game screen goes).
        public static void ToBracket()
        {
            TitleScreen.ReturnTo("cup");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // The gold strip under the post-game screen of a cup game: the result, and the way back to the bracket. A season
    // game (SeasonHost) uses it too, with its own Heading, Hint and Press.
    public sealed class CupStrip : MonoBehaviour
    {
        public string Text;
        public string Heading = "COUCH CUP";
        public string Hint = "Enter, (A) or click: see the bracket";
        public System.Action Press;   // what Enter / (A) / a click does (null: the bracket)
        GUIStyle big, small;
        Rect hit;

        void Update()
        {
            if (PauseMenu.Open) return;   // (Enter and clicks press the pause menu's buttons)
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            bool ok = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var mp = mouse.position.ReadValue();
                if (hit.Contains(new Vector2(mp.x, Screen.height - mp.y))) ok = true;
            }
            if (!ok) return;
            if (Press != null) Press();
            else CupHost.ToBracket();
        }

        void OnGUI()
        {
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            if (string.IsNullOrEmpty(Text)) return;
            GUI.depth = -20;   // over the post-game screen
            float W = Screen.width, H = Screen.height, s = H / 1080f;
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            big.fontSize = Mathf.RoundToInt(40 * s);
            small.fontSize = Mathf.RoundToInt(24 * s);
            var r = new Rect(0, H - 150 * s, W, 150 * s);
            hit = r;
            Fill(r, CupColors.Navy);
            Fill(new Rect(r.x, r.y, r.width, 6 * s), CupColors.Gold);
            big.normal.textColor = CupColors.GoldText;
            GUI.Label(new Rect(r.x, r.y + 14 * s, r.width, 70 * s), Heading + "  ·  " + Text, big);
            small.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x, r.y + 86 * s, r.width, 40 * s), Hint, small);
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    // The title's colours (navy panels, gold edges), shared by the bracket and the strip.
    public static class CupColors
    {
        public static readonly Color Navy = new Color(0.04f, 0.09f, 0.22f, 0.94f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.12f, 1f);
        public static readonly Color GoldText = new Color(1f, 0.82f, 0.2f);
        public static readonly Color NavyText = new Color(0.04f, 0.09f, 0.22f);
    }
}
