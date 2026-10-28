using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The one broadcast camera: stands on the near sideline and never rolls or spins.
    // It slides along X (following the ball from M1-e). It is placed so visibleYards of field fit across the screen
    // at the look-at point. Once a play runs it eases back and up to camera.live (FEATURES.snapZoom, SetZoom).
    // For a field goal it swings round behind the kicker to look down the field at the posts (FEATURES.kickCam, SetKickView).
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        GameConfig cfg;
        float lastAspect;
        float lookAtXYd;
        float zoom;   // 0 = the framing before the snap, 1 = camera.live

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
            SkyView.UseSkyBox(cfg, rig.Cam);   // the sunset sky box (FEATURES.sunsetSky)
            var extra = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;   // the colour grade (ProjectSetup.LookVolume)
            extra.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            rig.Apply();
            return rig;
        }

        public void SetLookAtX(float xYd) { lookAtXYd = xYd; Apply(); }

        // FEATURES.kickCam: a field goal seen from behind the kicker, down the field to the posts. weight 0 = the
        // broadcast framing, 1 = standing at pos looking at lookAt (metres) with fovDeg; takes effect with the next
        // SetLookAtX. The camera still never rolls.
        float kickWeight, kickFov;
        Vector3 kickPos, kickLook;
        public void SetKickView(float weight, Vector3 pos, Vector3 lookAt, float fovDeg)
        {
            kickWeight = Mathf.Clamp01(weight); kickPos = pos; kickLook = lookAt; kickFov = fovDeg;
        }
        public float KickWeight => kickWeight;
        // 0..1 between the pre-snap framing and camera.live; takes effect with the next SetLookAtX.
        public void SetZoom(float t) { zoom = Mathf.Clamp01(t); }
        public float Zoom => zoom;

        // A photo scene's own close-up (the gauntlet's concept 01 lineup): the camera stands still at `pos` looking at
        // `lookAt` (metres) with its own field of view, instead of the broadcast framing.
        bool held;
        Vector3 heldPos, heldLook;
        float heldFov;
        public void Hold(Vector3 pos, Vector3 lookAt, float fovDeg) { held = true; heldPos = pos; heldLook = lookAt; heldFov = fovDeg; Apply(); }
        // Back to the broadcast framing (the lab's free camera lets go).
        public void Release() { held = false; Apply(); }
        public bool Held => held;
        // The point on the grass the broadcast framing looks at (metres).
        public Vector3 LookTarget => new Vector3(lookAtXYd * cfg.field.yardM, 0f, cfg.camera.lookAtZYd * cfg.field.yardM);

        void LateUpdate()
        {
            if (!Mathf.Approximately(Cam.aspect, lastAspect)) Apply();
        }

        void Apply()
        {
            lastAspect = Cam.aspect;
            if (held)
            {
                transform.SetPositionAndRotation(heldPos, Quaternion.LookRotation(heldLook - heldPos, Vector3.up));
                Cam.fieldOfView = heldFov;
                float d = Vector3.Distance(heldPos, heldLook);
                Cam.nearClipPlane = Mathf.Max(0.3f, d * 0.02f);
                Cam.farClipPlane = Mathf.Max(1500f, d * 4f);   // (far enough for the ground out to the haze round the stadium)
                return;
            }
            var p = Pose(cfg, Cam.aspect, lookAtXYd, zoom);
            if (kickWeight > 0f && (kickLook - kickPos).sqrMagnitude > 1e-4f)
            {
                var look = Quaternion.LookRotation(kickLook - kickPos, Vector3.up);
                transform.SetPositionAndRotation(Vector3.Lerp(p.position, kickPos, kickWeight), Quaternion.Slerp(p.rotation, look, kickWeight));
                Cam.fieldOfView = Mathf.Lerp(cfg.camera.fovDeg, kickFov, kickWeight);
                Cam.nearClipPlane = Mathf.Lerp(1f, 0.3f, kickWeight);
                Cam.farClipPlane = p.distance * 4f;
                return;
            }
            transform.SetPositionAndRotation(p.position, p.rotation);
            Cam.fieldOfView = cfg.camera.fovDeg;
            Cam.nearClipPlane = 1f;
            Cam.farClipPlane = p.distance * 4f;
        }

        public struct CamPose { public Vector3 position; public Quaternion rotation; public float distance; }

        // Pure maths, so the EditMode tests can check the framing without a window.
        public static CamPose Pose(GameConfig cfg, float aspect, float lookAtXYd, float zoom = 0f)
        {
            var c = cfg.camera;
            float m = cfg.field.yardM;
            float vHalf = c.fovDeg * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
            float yards = zoom > 0f ? Mathf.Lerp(c.visibleYards, c.live.visibleYards, zoom) : c.visibleYards;
            float pitch = zoom > 0f ? Mathf.Lerp(c.pitchDeg, c.live.pitchDeg, zoom) : c.pitchDeg;
            float dist = yards * m * 0.5f / Mathf.Tan(hHalf);
            float side = c.sideline < 0f ? 1f : -1f;   // -Z sideline looks toward +Z
            var rot = Quaternion.Euler(pitch, side > 0f ? 0f : 180f, 0f);
            var target = new Vector3(lookAtXYd * m, 0f, c.lookAtZYd * m);
            return new CamPose { position = target - rot * Vector3.forward * dist, rotation = rot, distance = dist };
        }
    }
}
