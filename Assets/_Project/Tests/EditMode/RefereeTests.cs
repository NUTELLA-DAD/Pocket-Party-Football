using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.refModel (Stadium.cs): the sideline refs wear Kenny's rigged referee (striped shirt, whistle, flag),
    // standing still; NO_REF_MODEL=1 brings back the code-built person in stripes. FEATURES.refsOnLines: one stands at
    // each down marker (NO_REFS_ON_LINES=1 gives the markers back to the code-built chain crew).
    public sealed class RefereeTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("refModel"), "FEATURES.refModel is on in game.json");
            Assert.AreEqual("NO_REF_MODEL", ConfigLoader.EnvName("refModel"));
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_REF_MODEL" ? "1" : null);
            Assert.IsFalse(cfg.Feature("refModel"));
        }

        [Test]
        public void TheRefsOnTheLinesSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("refsOnLines"), "FEATURES.refsOnLines is on in game.json");
            Assert.AreEqual("NO_REFS_ON_LINES", ConfigLoader.EnvName("refsOnLines"));
            Assert.Greater(cfg.stadium.lineRefBackM, 0f, "off the field, behind the pole");
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_REFS_ON_LINES" ? "1" : null);
            Assert.IsFalse(cfg.Feature("refsOnLines"));
        }

        // FEATURES.refsOnLines (Stadium.cs): a ref stands at each of the four down markers (the line of scrimmage and
        // the first-down line, both sidelines) and moves with them.
        [Test]
        public void ARefStandsOnEachLineAndMovesWithIt()
        {
            var cfg = ConfigTests.Load();
            var stadium = new Stadium(cfg, null);
            var root = GameObject.Find("Stadium");
            try
            {
                stadium.SetMarkers(30f, 40f);
                var refs = new System.Collections.Generic.List<Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == "Line ref") refs.Add(t);
                Assert.AreEqual(4, refs.Count, "one at each down marker");
                float m = cfg.field.yardM, edge = cfg.field.HalfWidth * m;
                int onLos = 0, onFirst = 0;
                foreach (var r in refs)
                {
                    Assert.IsNotNull(r.GetComponentInChildren<SkinnedMeshRenderer>(), "wearing the referee model");
                    Assert.Greater(Mathf.Abs(r.position.z), edge, "off the field, on the sideline");
                    if (Mathf.Abs(r.position.x - 30f * m) < 0.01f) onLos++;
                    if (Mathf.Abs(r.position.x - 40f * m) < 0.01f) onFirst++;
                }
                Assert.AreEqual(2, onLos, "two on the line of scrimmage");
                Assert.AreEqual(2, onFirst, "two on the first-down line");
                stadium.SetMarkers(-12f, -2f);
                foreach (var r in refs)
                    Assert.IsTrue(Mathf.Abs(r.position.x + 12f * m) < 0.01f || Mathf.Abs(r.position.x + 2f * m) < 0.01f, "moved with the lines");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheModelAndItsColourMapAreThere()
        {
            var sc = ConfigTests.Load().stadium;
            Assert.IsNotNull(Resources.Load<GameObject>(sc.refModel), "Resources/" + sc.refModel);
            var tex = Resources.Load<Texture2D>(sc.refTexture);
            Assert.IsNotNull(tex, "Resources/" + sc.refTexture);
            Assert.LessOrEqual(tex.width, 2048, "the 8K original downsized like the players");
        }

        [Test]
        public void TheRefStandsOnTheSameSkeleton()
        {
            var cfg = ConfigTests.Load();
            var fig = FootballFigure.Create(null, "Referee", cfg.view.playerHeightM);
            try
            {
                var skin = ModelSkin.Wear(fig, cfg.stadium.refModel, cfg.stadium.refTexture, Looks.Toon(null), false);
                Assert.IsNotNull(skin, "the referee model is worn (every Humanoid bone found)");
                var smr = fig.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.IsNotNull(smr.sharedMaterial.GetTexture("_MainTex"), "his colour map");
                Assert.LessOrEqual(smr.sharedMesh.triangles.Length / 3, 25000, "slimmed like the players");
                Transform Bone(string n)
                {
                    foreach (var t in smr.bones) if (t.name == n) return t;
                    return null;
                }
                fig.Pose(FootballFigure.PoseKind.Stand, 0);
                skin.Follow();
                Transform head = Bone("Head"), hips = Bone("Hips"), handL = Bone("LeftHand"), footL = Bone("LeftFoot");
                Assert.Greater(head.position.y, hips.position.y, "standing up");
                Assert.Greater(hips.position.y, footL.position.y);
                Assert.Less(handL.position.y, head.position.y, "arms down at his sides, not a T-pose");
                // (smr.bounds is padded for dives and falls: measure the posed mesh itself)
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var v in baked.vertices)
                {
                    float y = smr.transform.TransformPoint(v).y;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
                Object.DestroyImmediate(baked);
                Assert.That(hi - lo, Is.InRange(0.8f * cfg.view.playerHeightM, 1.2f * cfg.view.playerHeightM), "player height");
                Assert.That(lo, Is.InRange(-0.05f, 0.05f), "feet on the grass");
            }
            finally { Object.DestroyImmediate(fig.gameObject); }
        }
    }
}
