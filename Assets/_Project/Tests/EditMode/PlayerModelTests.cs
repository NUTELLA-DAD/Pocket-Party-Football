using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // Every player wears a rigged model (ModelSkin); the figure built from code is gone, so a model that fails to load
    // would leave a player invisible. Every team's model, and every position's, must load and fit the pose rig.
    public sealed class PlayerModelTests
    {
        [Test]
        public void EveryTeamAndPositionWearsAModel()
        {
            var cfg = ConfigTests.Load();
            var mat = Looks.Toon(null);
            Assert.IsNotNull(mat, "Resources/Toon.mat");
            var positions = new List<string> { null };
            foreach (var team in cfg.teams)
                foreach (var pm in team.posModels) positions.Add(pm.pos);
            foreach (var team in cfg.teams)
            {
                Assert.IsFalse(string.IsNullOrEmpty(team.model), team.@short + " has a \"model\" in game.json");
                foreach (var pos in positions)
                {
                    var fig = FootballFigure.Create(null, "Test", cfg.view.playerHeightM);
                    try
                    {
                        Assert.AreEqual(0, fig.GetComponentsInChildren<Renderer>(true).Length, "the pose rig has no mesh of its own");
                        Assert.IsTrue(ModelSkin.Wear(fig, team, pos, cfg, mat), team.@short + " " + (pos ?? "player") + " wears its model");
                        var smr = fig.GetComponentInChildren<SkinnedMeshRenderer>();
                        Assert.IsNotNull(smr);
                        Assert.IsNotNull(smr.sharedMaterial.GetTexture("_MainTex"), team.@short + " " + (pos ?? "player") + " has its colour map");
                        var baked = new Mesh();   // (the posed model itself; the renderer's bounds are padded for dives and falls)
                        smr.BakeMesh(baked, true);
                        float lo = float.MaxValue, hi = float.MinValue;
                        foreach (var v in baked.vertices)
                        {
                            float y = smr.transform.TransformPoint(v).y;
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                        Object.DestroyImmediate(baked);
                        Assert.That(hi - lo, Is.InRange(cfg.view.playerHeightM * 0.6f, cfg.view.playerHeightM * 1.2f), team.@short + " " + (pos ?? "player") + " height (in the Ready crouch)");
                    }
                    finally { Object.DestroyImmediate(fig.gameObject); }
                }
            }
        }

        [Test]
        public void ASidelinePlayerIsBakedStill()
        {
            var cfg = ConfigTests.Load();
            var fig = FootballFigure.Create(null, "Sideline", cfg.view.playerHeightM);
            var holder = fig.gameObject;
            try
            {
                Assert.IsTrue(ModelSkin.Wear(fig, cfg.teams[0], null, cfg, Looks.Toon(null)));
                ModelSkin.Freeze(fig, FootballFigure.PoseKind.Stand);
                Assert.AreEqual(0, holder.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, "no skinning left");
                Assert.AreEqual(0, holder.GetComponentsInChildren<ModelSkin>(true).Length);
                var mr = holder.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(mr, "one plain mesh");
                Bounds b = mr.bounds;
                Assert.AreEqual(cfg.view.playerHeightM, b.size.y, cfg.view.playerHeightM * 0.1f, "standing, full height");
                Assert.AreEqual(0f, b.min.y, cfg.view.playerHeightM * 0.05f, "feet on the grass");
                Assert.Less(new Vector2(b.center.x, b.center.z).magnitude, cfg.view.playerHeightM * 0.3f, "where the player stands");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        // tools/rig/handfix.py: every player's model and the referee's have a thumb and the four fingers as bones under
        // each hand (two each, so a hand can close round the ball), and the hands' skin follows them.
        [Test]
        public void EveryModelsHandsHaveThumbAndFingerBones()
        {
            var cfg = ConfigTests.Load();
            var models = new SortedSet<string> { cfg.stadium.refModel };
            foreach (var team in cfg.teams)
            {
                models.Add(team.model);
                foreach (var pm in team.posModels) models.Add(pm.model);
            }
            foreach (var path in models)
            {
                var prefab = Resources.Load<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                var bones = new HashSet<string>();
                foreach (var b in smr.bones) bones.Add(b.name);
                var weighted = new HashSet<int>();
                foreach (var w in smr.sharedMesh.boneWeights)
                {
                    if (w.weight0 > 0.2f) weighted.Add(w.boneIndex0);
                    if (w.weight1 > 0.2f) weighted.Add(w.boneIndex1);
                }
                foreach (var side in new[] { "Left", "Right" })
                    foreach (var n in new[] { "Thumb", "ThumbTip", "Fingers", "FingersTip" })
                    {
                        Assert.IsTrue(bones.Contains(side + n), path + " has the bone " + side + n);
                        int i = System.Array.FindIndex(smr.bones, b => b.name == side + n);
                        Assert.IsTrue(weighted.Contains(i), path + ": the skin follows " + side + n);
                        Assert.AreEqual(side + (n.StartsWith("Thumb") ? (n == "Thumb" ? "Hand" : "Thumb") : (n == "Fingers" ? "Hand" : "Fingers")),
                            smr.bones[i].parent.name, path + " " + side + n + "'s parent");
                    }
            }
        }
    }
}
