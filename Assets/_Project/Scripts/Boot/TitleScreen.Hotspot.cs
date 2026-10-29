using PocketParty.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // SETTINGS > HOTSPOT WI-FI (FEATURES.hotspotQr): the name and password of this PC's Mobile hotspot, typed once and
    // kept for every later run. With them, the join screen and the pause menu show a NO WI-FI? card (HotspotJoin.cs).
    // Up / Down or Tab: the next box or button. Enter: next box, or press the button. Esc or (B): back, nothing saved.
    public sealed partial class TitleScreen
    {
        enum HsItem { Name, Password, Save, Clear, Back }
        const int HsCount = 5;
        static readonly string[] HsControl = { "pp-hotspot-name", "pp-hotspot-pass" };
        readonly Rect[] hsRows = new Rect[HsCount];
        string hsName = "", hsPass = "", hsProblem;
        GUIStyle hsField;

        void OpenHotspot()
        {
            Open(Page.Hotspot);
            hsName = HotspotJoin.SavedName;
            hsPass = HotspotJoin.SavedPassword;
            hsProblem = null;
        }

        void CloseHotspot()
        {
            Open(Page.Settings);
            pick = HotspotRow;
            GUIUtility.keyboardControl = 0;
        }

        void HotspotInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && kb.shiftKey.isPressed)))
                || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame));
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && !kb.shiftKey.isPressed)))
                || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame));
            bool ok = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (back) { CloseHotspot(); escFrame = Time.frameCount; return; }
            if (up) pick = (pick + HsCount - 1) % HsCount;
            if (down) pick = (pick + 1) % HsCount;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var mp = mouse.position.ReadValue();
                mp.y = Screen.height - mp.y;
                for (int i = 0; i < HsCount; i++)
                    if (hsRows[i].Contains(mp)) { pick = i; if (i >= (int)HsItem.Save) ok = true; }
            }
            if (!ok) return;
            switch ((HsItem)pick)
            {
                case HsItem.Name: pick = (int)HsItem.Password; break;
                case HsItem.Password:
                case HsItem.Save:
                    string name = hsName.Trim();
                    if (name.Length == 0 && hsPass.Length == 0) { HotspotJoin.Save("", ""); CloseHotspot(); break; }
                    hsProblem = WifiQr.Problem(name, hsPass);
                    if (hsProblem != null) { pick = name.Length == 0 ? (int)HsItem.Name : (int)HsItem.Password; break; }
                    HotspotJoin.Save(name, hsPass);
                    Debug.Log("[title] hotspot saved: '" + name + "'");
                    CloseHotspot();
                    break;
                case HsItem.Clear:
                    hsName = hsPass = "";
                    hsProblem = null;
                    HotspotJoin.Save("", "");
                    pick = (int)HsItem.Name;
                    break;
                default: CloseHotspot(); break;
            }
        }

        void DrawHotspot(float W, float H, float s)
        {
            var r = Panel(W, H, s, "HOTSPOT WI-FI");
            float padX = 70 * s, y = r.y + 108 * s, w = r.width - 2 * padX;
            body.normal.textColor = Color.white;
            const string about = "No Wi-Fi router? Turn on <b>Mobile hotspot</b> on this PC (Windows Settings > Network & internet > "
                + "Mobile hotspot) and copy its network name and password here. The join screen then shows a NO WI-FI? code: "
                + "phones scan it, tap Join, then scan the game code. (A phone's hotspot works too: put this PC on it, then type "
                + "that hotspot's name and password.)";
            float ah = body.CalcHeight(new GUIContent(about), w);
            GUI.Label(new Rect(r.x + padX, y, w, ah), about, body);
            y += ah + 22 * s;

            hsField ??= new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            hsField.fontSize = Mathf.RoundToInt(36 * s);
            hsField.padding = new RectOffset(Mathf.RoundToInt(18 * s), Mathf.RoundToInt(18 * s), 0, 0);
            foreach (var st in new[] { hsField.normal, hsField.hover, hsField.focused, hsField.active }) { st.background = null; st.textColor = new Color(0.04f, 0.09f, 0.22f); }

            float rw = Mathf.Min(w, 820 * s), rx = (W - rw) * 0.5f, lw = 250 * s, rh = 62 * s, gap = 12 * s;
            string[] labels = { "NAME", "PASSWORD" };
            for (int i = 0; i < 2; i++)
            {
                var rr = new Rect(rx, y, rw, rh);
                hsRows[i] = rr;
                bool on = pick == i;
                Fill(rr, on ? new Color(1f, 0.78f, 0.12f, 1f) : new Color(1f, 1f, 1f, 0.08f));
                row.normal.textColor = on ? new Color(0.04f, 0.09f, 0.22f) : Color.white;
                row.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(rr.x + 20 * s, rr.y, lw, rh), labels[i], row);
                row.alignment = TextAnchor.MiddleCenter;
                var box = new Rect(rr.x + lw, rr.y + 7 * s, rr.width - lw - 10 * s, rh - 14 * s);
                Fill(box, Color.white);
                var was = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = new Color(0.04f, 0.09f, 0.22f);
                GUI.SetNextControlName(HsControl[i]);
                if (i == 0) hsName = GUI.TextField(box, hsName, 32, hsField);
                else hsPass = GUI.TextField(box, hsPass, WifiQr.MaxPassword, hsField);
                GUI.skin.settings.cursorColor = was;
                y += rh + gap;
            }
            // the typing box follows the highlight (Tab, arrows, a click)
            string want = pick < 2 ? HsControl[pick] : null;
            if (want != null && GUI.GetNameOfFocusedControl() != want) GUI.FocusControl(want);
            if (want == null && GUIUtility.keyboardControl != 0) GUIUtility.keyboardControl = 0;

            if (hsProblem != null)
            {
                hint.normal.textColor = new Color(1f, 0.55f, 0.5f);
                GUI.Label(new Rect(r.x, y, r.width, 40 * s), hsProblem, hint);
                hint.normal.textColor = Color.white;
            }
            y += 46 * s;
            string[] buttons = { "SAVE", "CLEAR", "BACK" };
            float bw = (rw - 2 * gap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var br = new Rect(rx + i * (bw + gap), y, bw, rh);
                hsRows[2 + i] = br;
                bool on = pick == 2 + i;
                Fill(br, on ? new Color(1f, 0.78f, 0.12f, 1f) : new Color(1f, 1f, 1f, 0.08f));
                row.normal.textColor = on ? new Color(0.04f, 0.09f, 0.22f) : Color.white;
                GUI.Label(br, buttons[i], row);
            }
            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), "Type with the keyboard. Tab or Up / Down to move, Enter to save, Esc to go back", hint);
        }
    }
}
