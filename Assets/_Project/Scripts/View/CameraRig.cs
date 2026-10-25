using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The one broadcast camera: stands on the near sideline, long lens, fixed pitch, never tilts, rolls or spins.
    // It only slides along X (following the ball from M1-e). It is placed so visibleYards of field fit across the screen
    // at the look-at point.
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        GameConfig cfg;
        float lastAspect;
        float lookAtXYd;

        public static CameraRig Create(GameConfig cfg)
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            rig.cfg = cfg;
            rig.lookAtXYd = cfg.camera.lookAtXYd;
            rig.Cam = go.AddComponent<Camera>();
            rig.Cam.tag = "MainCamera";
            rig.Cam.clearFlags = CameraClearFlags.SolidColor;
            rig.Cam.backgroundColor = Colors.Hex(cfg.field.skyColor);
            rig.Apply();
            return rig;
        }

        public void SetLookAtX(float xYd) { lookAtXYd = xYd; Apply(); }

        void LateUpdate()
        {
            if (!Mathf.Approximately(Cam.aspect, lastAspect)) Apply();
        }

        void Apply()
        {
            lastAspect = Cam.aspect;
            var p = Pose(cfg, Cam.aspect, lookAtXYd);
            transform.SetPositionAndRotation(p.position, p.rotation);
            Cam.fieldOfView = cfg.camera.fovDeg;
            Cam.nearClipPlane = 1f;
            Cam.farClipPlane = p.distance * 4f;
        }

        public struct CamPose { public Vector3 position; public Quaternion rotation; public float distance; }

        // Pure maths, so the EditMode tests can check the framing without a window.
        public static CamPose Pose(GameConfig cfg, float aspect, float lookAtXYd)
        {
            var c = cfg.camera;
            float m = cfg.field.yardM;
            float vHalf = c.fovDeg * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
            float dist = c.visibleYards * m * 0.5f / Mathf.Tan(hHalf);
            float side = c.sideline < 0f ? 1f : -1f;   // -Z sideline looks toward +Z
            var rot = Quaternion.Euler(c.pitchDeg, side > 0f ? 0f : 180f, 0f);
            var target = new Vector3(lookAtXYd * m, 0f, c.lookAtZYd * m);
            return new CamPose { position = target - rot * Vector3.forward * dist, rotation = rot, distance = dist };
        }
    }
}
