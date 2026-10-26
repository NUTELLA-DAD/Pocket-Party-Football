using System.Collections.Generic;
using System.IO;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Boot
{
    // The practice lab: the normal game with one drill on the field instead of a whole game (Config/lab.json).
    // Started with -lab (Lab.bat) or the environment PP_LAB=1; -lab-drill <id> picks the first drill and
    // -lab-warmup <ticks> runs it that far before the first frame (screenshots).
    // You play with the keyboard, or with a phone: the first phone that joins plays the drill (the keyboard still works).
    // Keys: WASD / arrows move, SHIFT sprint, SPACE hike, 1-5 throw (hold CTRL: lob), F tackle, E juke, G block;
    // TAB next drill (SHIFT+TAB back), R again, V hitboxes, P pause, N one tick while paused, [ ] slower / faster.
    public sealed class LabMode : MonoBehaviour
    {
        public static LabMode Instance { get; private set; }
        public Lab Lab { get; private set; }
        public LabOverlay Overlay { get; private set; }
        public bool Paused { get; private set; }
        public float Speed => cfg.speeds.Count > 0 ? cfg.speeds[speedIndex] : 1f;

        LabConfig cfg;
        GameConfig game;
        PartyHost host;
        int speedIndex;
        int synced = -1;
        // presses wait here until the next tick takes them (a frame can run no tick, or several)
        bool hike, tackle, juke;
        int throwTo;
        bool throwLob;
        static readonly Key[] ThrowKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

        public static bool Requested()
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-lab") return true;
            return System.Environment.GetEnvironmentVariable("PP_LAB") == "1";
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        public static LabMode Create(GameObject on, GameConfig game, Material baseMat, PartyHost host, string configDir)
        {
            var m = on.AddComponent<LabMode>();
            Instance = m;
            m.game = game;
            m.host = host;
            string path = Path.Combine(configDir, "lab.json");
            m.cfg = PocketFootball.Sim.Lab.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}");
            if (m.cfg.drills.Count == 0) { Debug.LogWarning("[lab] no drills in " + path); return m; }
            m.Lab = new PocketFootball.Sim.Lab(host.World, m.cfg);
            m.Overlay = new LabOverlay(game, baseMat);
            host.DevKeys = false;
            host.BeforeTick += m.Feed;
            host.AfterTick += m.Lab.AfterTick;
            int first = m.Lab.FindDrill(Arg("-lab-drill") ?? "");
            m.Lab.YouSlot = m.PreferredSlot();
            m.Lab.Pick(first < 0 ? 0 : first);
            if (int.TryParse(Arg("-lab-warmup") ?? "", out int warm)) for (int i = 0; i < warm; i++) host.StepOnce();
            Debug.Log("[lab] practice lab up: " + m.cfg.drills.Count + " drills, first " + m.Lab.Drill.id);
            return m;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        // The first phone that has joined plays the drill; with none, the keyboard.
        int PreferredSlot()
        {
            foreach (var s in host.Roster.Seats) if (s.Kind == SeatKind.Phone && s.Connected) return s.Slot;
            return cfg.keyboardSlot;
        }

        void Update()
        {
            if (Lab == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) Lab.Next(kb.shiftKey.isPressed ? -1 : 1);
                if (kb.rKey.wasPressedThisFrame) Lab.Again();
                if (kb.vKey.wasPressedThisFrame) Overlay.Shown = !Overlay.Shown;
                if (kb.pKey.wasPressedThisFrame) Paused = !Paused;
                if (kb.nKey.wasPressedThisFrame && Paused) host.StepOnce();
                if (kb.leftBracketKey.wasPressedThisFrame) speedIndex = Mathf.Min(speedIndex + 1, Mathf.Max(0, cfg.speeds.Count - 1));
                if (kb.rightBracketKey.wasPressedThisFrame) speedIndex = Mathf.Max(speedIndex - 1, 0);
                if (kb.spaceKey.wasPressedThisFrame) hike = true;
                if (kb.fKey.wasPressedThisFrame) tackle = true;
                if (kb.eKey.wasPressedThisFrame) juke = true;
                for (int k = 0; k < ThrowKeys.Length; k++)
                    if (kb[ThrowKeys[k]].wasPressedThisFrame) { throwTo = k + 1; throwLob = kb.ctrlKey.isPressed; }
            }
            Time.timeScale = Paused ? 0f : Speed;

            // a phone came or went (or picked a team on its own): the drill starts again with the right person in it
            int want = PreferredSlot();
            var me = Lab.You >= 0 ? host.World.Find(Lab.You) : null;
            bool lost = Lab.You >= 0 && (me == null || !me.Human);
            if (want != Lab.YouSlot || lost) { Lab.YouSlot = want; Lab.Again(); }
            SyncPhone();
        }

        void LateUpdate()
        {
            if (Overlay != null) Overlay.Draw(host.World, host.Players.FeetPosition);
        }

        // The phone playing the lab is told its team and number after every setup (the drills move it around).
        void SyncPhone()
        {
            if (synced == Lab.Starts) return;
            synced = Lab.Starts;
            var seat = host.Roster[Lab.You];
            var p = Lab.You >= 0 ? host.World.Find(Lab.You) : null;
            if (seat == null || p == null || seat.Conn < 0) return;
            seat.Team = p.Team;
            host.Server.Send(seat.Conn, Messages.Picked(seat, p, game));
        }

        // Before each tick: the keyboard plays "you" (added to a phone's own input when a phone plays the drill).
        void Feed(Dictionary<int, PlayerInput> inputs)
        {
            if (Lab == null || Lab.You < 0) return;
            inputs.TryGetValue(Lab.You, out var i);
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
                i.Block |= kb.gKey.isPressed;
            }
            i.Hike |= hike; i.Tackle |= tackle; i.Juke |= juke;
            if (throwTo > 0) { i.Throw = throwTo; i.Lob = throwLob; }
            hike = tackle = juke = false;
            throwTo = 0;
            inputs[Lab.You] = i;
        }

        // ---- the panel, top left ----
        GUIStyle title, body, small;

        void OnGUI()
        {
            if (Lab == null) return;
            float s = Screen.height / 1080f;
            title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, richText = true };
            body ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            small ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            title.fontSize = Mathf.RoundToInt(30 * s);
            body.fontSize = Mathf.RoundToInt(21 * s);
            small.fontSize = Mathf.RoundToInt(18 * s);

            var w = host.World;
            var d = Lab.Drill;
            float pad = 14 * s, x = 16 * s, y = 16 * s, width = 560 * s, inner = width - 2 * pad;
            var lines = new List<(string text, GUIStyle style)>
            {
                ("PRACTICE LAB   <size=" + Mathf.RoundToInt(20 * s) + ">drill " + (Lab.DrillIndex + 1) + " of " + cfg.drills.Count + "</size>", title),
                ("<b><color=#ffd84a>" + d.name + "</color></b>" + (Lab.Rep > 0 ? "   (rep " + (Lab.Rep + 1) + ")" : ""), body),
                (d.help, body),
                ("<color=#9fe0ff>Move</color> WASD or arrows   <color=#9fe0ff>Sprint</color> SHIFT   <color=#9fe0ff>Hike</color> SPACE\n" +
                 "<color=#9fe0ff>Throw</color> 1-5 (hold CTRL = lob)   <color=#9fe0ff>Tackle</color> F   <color=#9fe0ff>Juke</color> E   <color=#9fe0ff>Block</color> G (hold)\n" +
                 "<color=#9fe0ff>Next drill</color> TAB   <color=#9fe0ff>Again</color> R   <color=#9fe0ff>Hitboxes</color> V (" + (Overlay.Shown ? "on" : "off") + ")\n" +
                 "<color=#9fe0ff>Pause</color> P" + (Paused ? " <b>(PAUSED, N = one step)</b>" : "") + "   <color=#9fe0ff>Speed</color> [ ]  <b>" + SpeedText() + "</b>", small),
                (PhoneLine(), small),
                (NowLine(w), body),
            };
            if (Lab.Log.Count > 0) lines.Add(("<color=#c8c8c8>" + string.Join("\n", Lab.Log) + "</color>", small));

            float h = pad;
            foreach (var l in lines) h += l.style.CalcHeight(new GUIContent(l.text), inner) + 6 * s;
            GUI.color = new Color(0f, 0f, 0f, 0.74f);
            GUI.DrawTexture(new Rect(x, y, width, h + pad), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float at = y + pad;
            foreach (var l in lines)
            {
                float lh = l.style.CalcHeight(new GUIContent(l.text), inner);
                GUI.Label(new Rect(x + pad, at, inner, lh), l.text, l.style);
                at += lh + 6 * s;
            }
            if (Overlay.Shown) Legend(x, y + h + pad + 8 * s, width, s);
        }

        string SpeedText() => Speed >= 1f ? "normal" : Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";

        string PhoneLine()
        {
            var seat = Lab.You >= 0 ? host.Roster[Lab.You] : null;
            if (seat != null) return "<color=#7dff9a>" + seat.Name + "'s phone is playing this drill</color> (the keyboard works too)";
            return "Playing with the keyboard. Scan the QR to play the drill with your phone.";
        }

        string NowLine(World w)
        {
            string phase = w.Phase == Phase.Set ? "Waiting for the snap" : w.Phase == Phase.Live ? "Live " + Lab.F1(w.PlayTicks / (float)game.sim.tickHz) + " s"
                : w.Phase == Phase.Whistle ? "Whistle: <b>" + w.Result + "</b>" : "";
            var parts = new List<string> { phase };
            var me = Lab.You >= 0 ? w.Find(Lab.You) : null;
            if (me != null)
            {
                float sp = Mathf.Sqrt(me.VX * me.VX + me.VZ * me.VZ);
                parts.Add("you " + Lab.F1(sp) + " yd/s, stamina " + Lab.Pct(me.Stamina));
            }
            if (Lab.Drill.throwAtS >= 0f && w.Phase == Phase.Live && !w.Thrown) parts.Add("next throw: " + Lab.ThrowKind());
            string line = string.Join("   ·   ", parts);
            var k = LivePlay.Carrier(w);
            var t = Lab.NearestTackler();
            if (k != null && t != null && w.Phase == Phase.Live)
            {
                float dist = LivePlay.Dist(t, k), reach = Lab.TackleReach(w, t);
                line += "\nNearest tackler " + Lab.Who(t) + ": " + Lab.F1(dist) + " yd (reach " + Lab.F1(reach) + "), odds " + Lab.Pct(Lab.TackleOdds(w, t, k))
                    + (dist <= reach ? "  <color=#ff7b7b><b>IN REACH</b></color>" : "");
            }
            return line;
        }

        void Legend(float x, float y, float width, float s)
        {
            string text = "<color=#ffffff>●</color> body   <color=#f2382f>●</color> tackle reach   <color=#4089ff>●</color> block reach   " +
                          "<color=#ffe626>●</color> catch circle   <color=#ff8c1a>●</color> pass aimed here";
            float h = small.CalcHeight(new GUIContent(text), width - 28 * s);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, width, h + 12 * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 14 * s, y + 6 * s, width - 28 * s, h), text, small);
        }
    }
}
