using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The practice lab's free camera, as numbers: it circles a point on the field (target, metres) at a distance, turned
    // by yaw and tilted down by pitch, with its own lens. Pure maths, so the EditMode tests can check it without a window.
    // Yaw 0 looks toward +Z, like the TV camera on the -Z sideline. It never rolls.
    public struct OrbitCamera
    {
        public Vector3 target;
        public float yawDeg, pitchDeg, distM, fovDeg;

        public Quaternion Rotation => Quaternion.Euler(pitchDeg, yawDeg, 0f);
        public Vector3 Position => target - Rotation * Vector3.forward * distM;

        // Starting from wherever a camera is now (the TV camera), looking at `lookAt`.
        public static OrbitCamera From(Vector3 pos, Quaternion rot, Vector3 lookAt, float fovDeg)
        {
            var e = rot.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            return new OrbitCamera { target = lookAt, yawDeg = e.y, pitchDeg = pitch, distM = Vector3.Distance(pos, lookAt), fovDeg = fovDeg };
        }

        // A preset from game.json, standing far enough back that visibleYards fit across the screen at the target.
        public static OrbitCamera Preset(FreeCameraPreset p, Vector3 target, float aspect, float yardM)
        {
            return new OrbitCamera { target = target, yawDeg = p.yawDeg, pitchDeg = p.pitchDeg, fovDeg = p.fovDeg,
                distM = DistanceFor(p.visibleYards * yardM, p.fovDeg, aspect) };
        }

        public static float DistanceFor(float widthM, float fovDeg, float aspect)
        {
            float hHalf = Mathf.Atan(Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) * aspect);
            return widthM * 0.5f / Mathf.Tan(hHalf);
        }

        // Dragging: dx, dy in pixels. Turning right goes around the target; dragging up lifts it toward looking straight down.
        public void Orbit(float dx, float dy, FreeCameraConfig c)
        {
            yawDeg = Mathf.Repeat(yawDeg + dx * c.orbitDegPerPx, 360f);
            pitchDeg = Mathf.Clamp(pitchDeg + dy * c.orbitDegPerPx, c.minPitchDeg, c.maxPitchDeg);
        }

        // Right-dragging slides the target along the grass so the field follows the mouse.
        public void Pan(float dx, float dy, float screenHeightPx, FreeCameraConfig c, FieldConfig f)
        {
            float mPerPx = 2f * distM * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Max(screenHeightPx, 1f);
            var rot = Rotation;
            Vector3 right = rot * Vector3.right; right.y = 0f; right.Normalize();
            Vector3 ahead = rot * Vector3.forward + rot * Vector3.up; ahead.y = 0f; ahead.Normalize();   // "up the screen", on the grass
            target -= (right * dx + ahead * dy) * mPerPx;
            float mx = (f.HalfLength + c.boundsMarginYd) * f.yardM, mz = (f.HalfWidth + c.boundsMarginYd) * f.yardM;
            target = new Vector3(Mathf.Clamp(target.x, -mx, mx), 0f, Mathf.Clamp(target.z, -mz, mz));
        }

        // The wheel: steps > 0 comes closer.
        public void Zoom(float steps, FreeCameraConfig c)
        {
            distM = Mathf.Clamp(distM * Mathf.Pow(c.zoomStep, -steps), c.minDistM, c.maxDistM);
        }

        // A new lens, stepping back or in so the field at the target stays the same size on screen: only the perspective
        // changes (a long lens flattens, a wide one exaggerates depth).
        public void SetLens(float newFovDeg, FreeCameraConfig c)
        {
            float k = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(newFovDeg * 0.5f * Mathf.Deg2Rad);
            distM = Mathf.Clamp(distM * k, c.minDistM, c.maxDistM);
            fovDeg = newFovDeg;
        }
    }
}
