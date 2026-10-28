using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The TV side of a kick (WS-01, FEATURES.kicking). Over the kicker while he lines it up: what kind of kick it is
    // (KICKOFF, PUNT, FIELD GOAL 42 YDS, EXTRA POINT) and, for a person, SWIPE YOUR PHONE. For kicking.meterShowS after
    // the kick: his power as a bar filled in tenths (stepped, so it never wobbles) and his aim as a tick left or right
    // of the middle. Reads the sim, never writes it. Draws nothing with kicking off.
    public sealed class KickMeter : MonoBehaviour
    {
        World world;
        PlayersView players;
        GUIStyle text;
        string title = "";

        public static KickMeter Create(GameObject on, World w, PlayersView players)
        {
            var m = on.AddComponent<KickMeter>();
            m.world = w;
            m.players = players;
            return m;
        }

        void OnGUI()
        {
            if (world == null || players == null || !Kicking.On(world)) return;
            var cam = Camera.main;
            if (cam == null) return;
            var k = world.Kick;
            var c = world.Cfg.kicking;
            var kicker = Kicking.Kicker(world);
            bool before = kicker != null && Kicking.WaitingToKick(world, kicker);
            bool after = k.Kicked && world.Tick - k.KickTick < c.meterShowS * world.Cfg.sim.tickHz;
            if (before)
            {
                // (remembered: once the whistle blows the play is no longer a kick)
                title = Kicking.Name(world);
                int yds = Kicking.KickYards(world);
                if (yds > 0 && !k.Try) title += " " + yds + " YDS";
            }
            if ((!before && !after) || kicker == null || title.Length == 0) return;

            Vector3 sp = cam.WorldToScreenPoint(players.HeadPosition(kicker.Slot) + Vector3.up * 1.6f);
            if (sp.z <= 0f) return;
            float s = Screen.height / 1080f;
            text ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            text.fontSize = Mathf.RoundToInt(26 * s);
            text.normal.textColor = Color.white;

            string line = before && kicker.Human ? title + "   SWIPE YOUR PHONE" : title;
            float w = Mathf.Max(text.CalcSize(new GUIContent(line)).x + 32 * s, 260 * s);
            float h = 44 * s, barH = after ? 34 * s : 0f;
            float y = Screen.height - sp.y - h - barH;
            float x = Hud.Clear(new Rect(sp.x - w * 0.5f, y, w, h + barH), 8f * s).x;   // on screen, clear of the join panel
            Box(new Rect(x, y, w, h + barH), new Color(0.05f, 0.13f, 0.1f, 0.88f));
            GUI.Label(new Rect(x, y, w, h), line, text);
            if (!after) return;

            // power: ten steps from green to red; aim: a white tick, the middle is straight
            float pad = 12 * s, bw = w - pad * 2f, by = y + h, bh = barH - pad * 0.75f;
            Box(new Rect(x + pad, by, bw, bh), new Color(1f, 1f, 1f, 0.15f));
            float filled = Mathf.Floor(k.Power * 10f) / 10f;
            Box(new Rect(x + pad, by, bw * filled, bh), Color.Lerp(new Color(0.36f, 0.85f, 0.5f), new Color(0.95f, 0.3f, 0.25f), filled));
            float tick = x + pad + bw * (0.5f + k.Aim * 0.5f);
            Box(new Rect(tick - 3 * s, by - 4 * s, 6 * s, bh + 8 * s), Color.white);
            Box(new Rect(x + pad + bw * 0.5f - 1 * s, by, 2 * s, bh), new Color(0f, 0f, 0f, 0.5f));
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
