using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The practice lab's free camera (FEATURES.labFreeCam).
    public sealed class OrbitCameraTests
    {
        const float Aspect = 16f / 9f;

        [Test]
        public void TakingOverFromTheTvCameraDoesNotJump()
        {
            var cfg = ConfigTests.Load();
            var p = CameraRig.Pose(cfg, Aspect, 10f);
            var target = new Vector3(10f * cfg.field.yardM, 0f, cfg.camera.lookAtZYd * cfg.field.yardM);
            var o = OrbitCamera.From(p.position, p.rotation, target, cfg.camera.fovDeg);
            Assert.Less(Vector3.Distance(p.position, o.Position), 0.01f);
            Assert.Less(Quaternion.Angle(p.rotation, o.Rotation), 0.1f);
        }

        [Test]
        public void TheFirstPresetIsTheTvCamera()
        {
            var cfg = ConfigTests.Load();
            var p = CameraRig.Pose(cfg, Aspect, 0f);
            var o = OrbitCamera.Preset(cfg.camera.free.presets[0], new Vector3(0f, 0f, cfg.camera.lookAtZYd * cfg.field.yardM), Aspect, cfg.field.yardM);
            Assert.Less(Vector3.Distance(p.position, o.Position), 0.05f);
            Assert.AreEqual(cfg.camera.fovDeg, o.fovDeg, 1e-4f);
        }

        [Test]
        public void TurningTiltingAndZoomingStayInBounds()
        {
            var cfg = ConfigTests.Load();
            var c = cfg.camera.free;
            var o = new OrbitCamera { target = Vector3.zero, yawDeg = 0f, pitchDeg = 19f, distM = 50f, fovDeg = 30f };
            o.Orbit(0f, 100000f, c);
            Assert.AreEqual(c.maxPitchDeg, o.pitchDeg, 1e-4f);
            o.Orbit(0f, -100000f, c);
            Assert.AreEqual(c.minPitchDeg, o.pitchDeg, 1e-4f);
            Assert.Greater(o.Position.y, 0f);                            // never under the grass
            o.Zoom(1000f, c);
            Assert.AreEqual(c.minDistM, o.distM, 1e-4f);
            o.Zoom(-1000f, c);
            Assert.AreEqual(c.maxDistM, o.distM, 1e-4f);
            o.Pan(1e7f, 1e7f, 1080f, c, cfg.field);
            Assert.LessOrEqual(Mathf.Abs(o.target.x), (cfg.field.HalfLength + c.boundsMarginYd) * cfg.field.yardM + 1e-3f);
            Assert.AreEqual(0f, o.target.y);                             // slides along the grass
            Assert.AreEqual(0f, o.Rotation.eulerAngles.z, 1e-3f);        // never rolls
        }

        [Test]
        public void ChangingTheLensKeepsTheFraming()
        {
            var cfg = ConfigTests.Load();
            var o = new OrbitCamera { target = Vector3.zero, yawDeg = 0f, pitchDeg = 19f, distM = 190f, fovDeg = 7f };
            float before = o.distM * Mathf.Tan(o.fovDeg * 0.5f * Mathf.Deg2Rad);
            o.SetLens(35f, cfg.camera.free);
            Assert.AreEqual(35f, o.fovDeg);
            Assert.Less(o.distM, 190f);                                  // a wider lens stands closer
            Assert.AreEqual(before, o.distM * Mathf.Tan(o.fovDeg * 0.5f * Mathf.Deg2Rad), 0.01f);
        }

        // FEATURES.snapZoom: in a live play the TV camera stands further back and higher, showing camera.live.visibleYards.
        [Test]
        public void TheTvCameraZoomsOutForTheLivePlay()
        {
            var cfg = ConfigTests.Load();
            var c = cfg.camera;
            var set = CameraRig.Pose(cfg, Aspect, 0f, 0f);
            var live = CameraRig.Pose(cfg, Aspect, 0f, 1f);
            float hHalf = Mathf.Atan(Mathf.Tan(c.fovDeg * 0.5f * Mathf.Deg2Rad) * Aspect);
            Assert.AreEqual(c.live.visibleYards, 2f * live.distance * Mathf.Tan(hHalf) / cfg.field.yardM, 0.01f);
            Assert.Greater(live.distance, set.distance);
            Assert.Greater(live.position.y, set.position.y);
            Assert.AreEqual(c.live.pitchDeg, live.rotation.eulerAngles.x, 1e-3f);
            Assert.AreEqual(0f, live.rotation.eulerAngles.z, 1e-3f);
            Assert.IsTrue(cfg.Feature("snapZoom"));
            Assert.AreEqual("NO_SNAP_ZOOM", ConfigLoader.EnvName("snapZoom"));
        }

        [Test]
        public void NoLabFreeCamTurnsItOff()
        {
            Assert.AreEqual("NO_LAB_FREE_CAM", ConfigLoader.EnvName("labFreeCam"));
            Assert.IsTrue(ConfigTests.Load().Feature("labFreeCam"));
        }
    }
}
