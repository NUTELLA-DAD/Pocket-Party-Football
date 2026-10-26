using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.modelBall (FootballFigure.MakeBall): Kenny's football model, slimmed, the length game.json asks for,
    // shiny with an ink outline like the ball built from code; NO_MODEL_BALL=1 goes back to that one.
    public sealed class ModelBallTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("modelBall"), "FEATURES.modelBall is in game.json");
            Assert.AreEqual("NO_MODEL_BALL", ConfigLoader.EnvName("modelBall"));
            cfg.features["modelBall"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_MODEL_BALL" ? "1" : null);
            Assert.IsFalse(cfg.Feature("modelBall"));
        }

        [Test]
        public void TheModelIsLightAndTheRightSize()
        {
            var cfg = ConfigTests.Load();
            var mat = Looks.Toon(null);
            Assert.IsNotNull(mat, "Resources/Toon.mat");
            var go = FootballFigure.MakeBall(null, mat, cfg.view.ballLengthM, true);
            try
            {
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.AreNotEqual("Ball", mesh.name, "the model, not the ball built from code");
                Assert.LessOrEqual(mesh.triangles.Length / 3, 4000, "slimmed");
                Vector3 size = Vector3.Scale(mesh.bounds.size, go.transform.localScale);
                Assert.AreEqual(cfg.view.ballLengthM, size.z, 1e-3f, "its length is view.ballLengthM, along +z");
                Assert.That(size.x / size.z, Is.InRange(0.5f, 0.65f), "football-shaped, not a sphere");
                Assert.AreEqual(size.x, size.y, 0.02f);
                Assert.Less(mesh.bounds.center.magnitude, 0.01f, "centred, so it sits in the hand like the old one");
                var tex = go.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_MainTex");
                Assert.IsNotNull(tex, "its colour map");
                Assert.LessOrEqual(tex.width, 1024);
                // the toon vertex contract (BallImport.cs): shiny with an outline, outlines along the smooth normal
                var colors = mesh.colors;
                var tangents = mesh.tangents;
                Assert.AreEqual(mesh.vertexCount, colors.Length);
                Assert.AreEqual(mesh.vertexCount, tangents.Length);
                Assert.AreEqual(FigureKit.Shiny, colors[0].a, 1e-3f);
                Assert.AreEqual(1f, tangents[0].w);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SwitchedOffItIsTheBallBuiltFromCode()
        {
            var cfg = ConfigTests.Load();
            var go = FootballFigure.MakeBall(null, Looks.Toon(null), cfg.view.ballLengthM, false);
            try { Assert.AreEqual("Ball", go.GetComponent<MeshFilter>().sharedMesh.name); }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
