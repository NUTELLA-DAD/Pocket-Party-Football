using UnityEngine;

namespace PocketFootball.View
{
    // The game's own lettering (FEATURES.toonHud, NO_TOON_HUD=1: Unity's built-in font and the old flat scoreboard).
    // Pocket Block is Resources/Fonts/PocketBlock.ttf, drawn letter by letter in tools/font/make_font.py: chunky
    // cut-corner capitals with a small forward lean, lowercase as small capitals. Every overlay on the TV writes with it:
    // Use() at the top of an OnGUI puts it in the shared skin, so every label style that names no font of its own
    // picks it up. Toon() writes a line the toon way: a dark ink edge all round and a drop under it.
    public static class UiFont
    {
        public const string Path = "Fonts/PocketBlock";
        public static readonly Color Ink = new Color(0.024f, 0.05f, 0.17f, 1f);   // #060d2b, the phone's --edge

        static Font font;
        static bool loaded, on = true;

        // Set once at boot from FEATURES.toonHud.
        public static void Configure(bool toon) => on = toon;

        public static bool On => on && Font != null;

        public static Font Font
        {
            get
            {
                if (!loaded) { loaded = true; font = Resources.Load<Font>(Path); }
                return font;
            }
        }

        // At the top of an OnGUI: the skin's font is our own (the skin is shared, so this is cheap after the first).
        public static void Use()
        {
            if (!On || GUI.skin.font == font) return;
            GUI.skin.font = font;
        }

        // A line with a dark edge all round (8 stamps `edge` px out) and a drop `edge` px further down, then the fill.
        // Plain text only: a <color> tag would colour the edge too.
        public static void Toon(Rect r, string text, GUIStyle st, Color fill, float edge)
        {
            if (string.IsNullOrEmpty(text)) return;
            var keep = st.normal.textColor;
            if (edge > 0f)
            {
                st.normal.textColor = Ink;
                GUI.Label(new Rect(r.x, r.y + edge * 1.8f, r.width, r.height), text, st);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 0.25f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * edge, r.y + Mathf.Sin(a) * edge, r.width, r.height), text, st);
                }
            }
            st.normal.textColor = fill;
            GUI.Label(r, text, st);
            st.normal.textColor = keep;
        }
    }
}
