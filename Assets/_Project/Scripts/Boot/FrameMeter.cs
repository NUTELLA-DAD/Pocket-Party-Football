using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // How smoothly the TV draws. Every view.perfLogEveryS it writes one [perf] line to the log (Player.log): frames a
    // second, the slowest 1% of frames, how many took longer than view.perfSlowFrameMs, the sync and the screen's rate.
    // F3 (or -fps) shows the same numbers in the top left corner. Reads the clock only; touches nothing else.
    public sealed class FrameMeter : MonoBehaviour
    {
        GameConfig cfg;
        bool shown;
        readonly Window corner = new Window(), log = new Window();
        string line = "";
        GUIStyle style;

        // the frame times (ms) since a window last emptied
        sealed class Window { public readonly List<float> Ms = new List<float>(); public float S; }

        public static FrameMeter Attach(GameObject on, GameConfig cfg, bool shown)
        {
            var m = on.AddComponent<FrameMeter>();
            m.cfg = cfg;
            m.shown = shown;
            return m;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) shown = !shown;
            float dt = Time.unscaledDeltaTime;
            corner.Ms.Add(dt * 1000f); corner.S += dt;
            log.Ms.Add(dt * 1000f); log.S += dt;
            // the corner shows a fresh reading each second; the log gets one every perfLogEveryS
            if (corner.S >= 1f) { line = Summary(corner.Ms); corner.Ms.Clear(); corner.S = 0f; }
            if (log.S >= (cfg.view.perfLogEveryS > 0f ? cfg.view.perfLogEveryS : 10f)) { Debug.Log("[perf] " + Summary(log.Ms)); log.Ms.Clear(); log.S = 0f; }
        }

        string Summary(List<float> frames)
        {
            var sorted = new List<float>(frames);
            sorted.Sort();
            float total = 0f;
            foreach (float f in sorted) total += f;
            float avgMs = total / sorted.Count;
            float worst1 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            int slow = 0;
            foreach (float f in sorted) if (f > cfg.view.perfSlowFrameMs) slow++;
            double hz = Screen.currentResolution.refreshRateRatio.value;
            return $"{1000f / avgMs:0.0} fps, 1% low {1000f / worst1:0} fps ({worst1:0.0} ms), {slow} of {sorted.Count} frames over {cfg.view.perfSlowFrameMs:0} ms, " +
                   $"vsync {QualitySettings.vSyncCount}, target {Application.targetFrameRate}, screen {hz:0} Hz, smoothMotion {(cfg.Feature("smoothMotion") ? "on" : "off")}";
        }

        void OnGUI()
        {
            if (!shown || string.IsNullOrEmpty(line)) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            var r = new Rect(12, 8, 1100, 28);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(r.x - 4, r.y, r.width, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, line, style);
        }
    }
}
