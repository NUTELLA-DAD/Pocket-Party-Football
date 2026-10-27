using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // The practice lab's free camera (FEATURES.labFreeCam, NO_LAB_FREE_CAM=1 turns it off). The lab starts on the TV
    // camera; the first drag, wheel turn or preset key takes over from it, and C hands back to the TV camera.
    // Mouse: drag = turn around, right-drag (or middle-drag) = slide, wheel = closer / further.
    // Keys: F1-F5 the presets in game.json (camera.free.presets), , and . a longer / wider lens, C = back to the TV camera.
    // -lab-cam <n> starts on preset n (1 = F1), for screenshots. It only moves the camera; the sim never sees it.
    public sealed class LabCamera : MonoBehaviour
    {
        public bool Free { get; private set; }
        public string PresetName { get; private set; } = "";
        public float FovDeg => Free ? orbit.fovDeg : cfg.camera.fovDeg;

        GameConfig cfg;
        CameraRig rig;
        OrbitCamera orbit;
        int pending = -1;   // -lab-cam: taken once the TV camera has settled on the drill
        static readonly Key[] PresetKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8 };

        public static LabCamera Attach(GameObject on, GameConfig cfg, CameraRig rig, string startPreset)
        {
            if (!cfg.Feature("labFreeCam") || rig == null) return null;
            var c = on.AddComponent<LabCamera>();
            c.cfg = cfg;
            c.rig = rig;
            if (int.TryParse(startPreset ?? "", out int n)) c.pending = n - 1;
            return c;
        }

        FreeCameraConfig Fc => cfg.camera.free;

        // Take over from the TV camera exactly where it is, so nothing jumps.
        void TakeOver()
        {
            if (Free) return;
            var t = rig.Cam.transform;
            orbit = OrbitCamera.From(t.position, t.rotation, rig.LookTarget, rig.Cam.fieldOfView);
            Free = true;
            PresetName = "";
        }

        public void UsePreset(int i)
        {
            if (i < 0 || i >= Fc.presets.Count) return;
            // around the point the camera looks at now (the TV camera's when it is in charge)
            var target = Free ? orbit.target : rig.LookTarget;
            orbit = OrbitCamera.Preset(Fc.presets[i], target, rig.Cam.aspect, cfg.field.yardM);
            Free = true;
            PresetName = Fc.presets[i].name;
            Push();
        }

        public void BackToTv()
        {
            Free = false;
            PresetName = "";
            rig.Release();
        }

        void Update()
        {
            if (pending >= 0 && Time.frameCount > 2) { UsePreset(pending); pending = -1; }
            var kb = Keyboard.current;
            if (kb != null)
            {
                for (int k = 0; k < PresetKeys.Length; k++) if (kb[PresetKeys[k]].wasPressedThisFrame) UsePreset(k);
                if (kb.cKey.wasPressedThisFrame) BackToTv();
                if (kb.commaKey.wasPressedThisFrame) StepLens(-1);
                if (kb.periodKey.wasPressedThisFrame) StepLens(1);
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 d = mouse.delta.ReadValue();
            float wheel = mouse.scroll.ReadValue().y;
            bool turning = mouse.leftButton.isPressed, sliding = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            if (d.sqrMagnitude > 0f && (turning || sliding))
            {
                TakeOver();
                if (sliding) orbit.Pan(d.x, d.y, Screen.height, Fc, cfg.field);
                else orbit.Orbit(d.x, d.y, Fc);
                PresetName = "";
                Push();
            }
            if (wheel != 0f)
            {
                TakeOver();
                // Windows sends 120 a notch; some mice and touchpads send small numbers
                float steps = Mathf.Abs(wheel) >= 60f ? wheel / 120f : Mathf.Sign(wheel);
                orbit.Zoom(Mathf.Clamp(steps, -3f, 3f), Fc);
                Push();
            }
        }

        // , = longer lens (flatter), . = wider lens (deeper), keeping the same framing.
        void StepLens(int dir)
        {
            var lenses = Fc.lensesDeg;
            if (lenses.Count == 0) return;
            TakeOver();
            float now = orbit.fovDeg;
            int pick = dir > 0 ? lenses.Count - 1 : 0;
            if (dir > 0) { for (int i = 0; i < lenses.Count; i++) if (lenses[i] > now + 0.01f) { pick = i; break; } }
            else { for (int i = lenses.Count - 1; i >= 0; i--) if (lenses[i] < now - 0.01f) { pick = i; break; } }
            orbit.SetLens(lenses[pick], Fc);
            Push();
        }

        void Push() => rig.Hold(orbit.Position, orbit.target, orbit.fovDeg);

        // The line for the lab's panel.
        public string HelpLine()
        {
            string state = Free ? (PresetName != "" ? PresetName : "free") : "TV camera";
            return "<color=#9fe0ff>Camera</color> drag = turn   right-drag = slide   wheel = zoom   F1-F" + Mathf.Min(Fc.presets.Count, PresetKeys.Length) +
                   " angles\n<color=#9fe0ff>Lens</color> , longer   . wider   <color=#9fe0ff>Back to TV camera</color> C   <b>(" + state + ", " +
                   FovDeg.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "° lens)</b>";
        }
    }
}
