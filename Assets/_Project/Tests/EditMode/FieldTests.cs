using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    public sealed class FieldTests
    {
        [Test]
        public void YardLinesRunGoalToGoalPlusEndLines()
        {
            var f = ConfigTests.Load().field;
            var xs = FieldLayout.YardLineXs(f);
            Assert.AreEqual(21 + 2, xs.Count);           // every 5 yards from goal line to goal line, plus 2 end lines
            Assert.AreEqual(-60f, xs[0]);
            Assert.AreEqual(-50f, xs[1]);
            Assert.AreEqual(0f, xs[11]);                   // the 50-yard line
            Assert.AreEqual(60f, xs[xs.Count - 1]);
        }

        [Test]
        public void StripesCoverTheHundredYards()
        {
            var f = ConfigTests.Load().field;
            var s = FieldLayout.Stripes(f);
            Assert.AreEqual(20, s.Count);
            Assert.AreEqual(-50f, s[0][0]);
            Assert.AreEqual(50f, s[s.Count - 1][1]);
        }

        [Test]
        public void HashMarksSitInsideTheField()
        {
            var f = ConfigTests.Load().field;
            var h = FieldLayout.HashMarks(f);
            Assert.AreEqual(2 * (99 - 19), h.Count);      // one per yard on two rows, minus the 19 inner yard lines
            foreach (var m in h) Assert.Less(Mathf.Abs(m[1]), f.HalfWidth);
        }

        [Test]
        public void SidelineCameraShowsSixtyYards()
        {
            var cfg = ConfigTests.Load();
            float aspect = 16f / 9f;
            var p = CameraRig.Pose(cfg, aspect, 0f);
            // The camera stands outside the near sideline, above the grass, looking across the field.
            Assert.Less(p.position.z, -cfg.field.HalfWidth * cfg.field.yardM);
            Assert.Greater(p.position.y, 0f);
            Assert.Greater((p.rotation * Vector3.forward).z, 0f);
            // At the look-at point the screen is visibleYards wide.
            float hHalf = Mathf.Atan(Mathf.Tan(cfg.camera.fovDeg * 0.5f * Mathf.Deg2Rad) * aspect);
            float widthYd = 2f * p.distance * Mathf.Tan(hHalf) / cfg.field.yardM;
            Assert.AreEqual(cfg.camera.visibleYards, widthYd, 0.01f);
            // Never rolls.
            Assert.AreEqual(0f, p.rotation.eulerAngles.z, 1e-3f);
        }

        [Test]
        public void FieldBuildsFromConfig()
        {
            var cfg = ConfigTests.Load();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var go = FieldView.Build(cfg, mat);
            try
            {
                Assert.AreEqual("Field", go.name);
                var paint = go.transform.Find("Paint");
                Assert.IsNotNull(paint, "the painted field");
                var tex = paint.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_MainTex") as Texture2D;
                Assert.IsNotNull(tex, "the field paint texture");
                // 32 px per yard over the field and its border
                Assert.AreEqual(Mathf.CeilToInt((cfg.field.lengthYd + 2 * cfg.field.sidelineBorderYd) * 32), tex.width);
                Assert.IsNull(go.GetComponentInChildren<Collider>());
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
