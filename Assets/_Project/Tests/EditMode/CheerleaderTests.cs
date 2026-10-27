using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.cheerleaders (Stadium.cs): each team's cheer squad on the far sideline wears Kenny's rigged cheerleader
    // in the team's colours and cheers in stepped poses, together; NO_CHEERLEADERS=1 leaves the sideline without them.
    public sealed class CheerleaderTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("cheerleaders"), "FEATURES.cheerleaders is on in game.json");
            Assert.AreEqual("NO_CHEERLEADERS", ConfigLoader.EnvName("cheerleaders"));
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_CHEERLEADERS" ? "1" : null);
            Assert.IsFalse(cfg.Feature("cheerleaders"));
        }

        [Test]
        public void EachTeamHasItsOwnColours()
        {
            var cfg = ConfigTests.Load();
            Assert.Greater(cfg.stadium.cheerPerTeam, 0);
            foreach (var t in cfg.teams)
            {
                Assert.IsNotNull(Resources.Load<GameObject>(t.cheerModel), t.name + ": Resources/" + t.cheerModel);
                var tex = Resources.Load<Texture2D>(t.cheerTexture);
                Assert.IsNotNull(tex, t.name + ": Resources/" + t.cheerTexture);
                Assert.LessOrEqual(tex.width, 2048, "imported at most 2048 (ModelImport)");
            }
            Assert.AreNotEqual(cfg.teams[0].cheerTexture, cfg.teams[1].cheerTexture, "Chiefs red and gold, Roughriders green and white");
        }

        [Test]
        public void TheRoutineIsSteppedAndTheSquadsTakeTurns()
        {
            var sc = ConfigTests.Load().stadium;
            Assert.Greater(sc.cheerFps, 0f);
            Assert.LessOrEqual(sc.cheerFps, 8f, "no faster than the crowd's keys");
            Assert.AreEqual(Stadium.CrowdKey(0f, sc.cheerFps), Stadium.CrowdKey(0.9f / sc.cheerFps, sc.cheerFps), "a pose holds for a whole key");
            var used = new bool[FootballFigure.CheerFrames];
            int differ = 0;
            for (int k = -40; k < 200; k++)
            {
                int a = Stadium.CheerFrame(sc, k, 0), b = Stadium.CheerFrame(sc, k, 1);
                Assert.That(a, Is.InRange(0, FootballFigure.CheerFrames - 1));
                Assert.That(b, Is.InRange(0, FootballFigure.CheerFrames - 1));
                Assert.AreEqual(a, Stadium.CheerFrame(sc, k + sc.cheerRoutine.Count, 0), "the routine repeats");
                Assert.AreEqual(a, Stadium.CheerFrame(sc, k + sc.cheerTeamOffset, 1), "the second squad runs behind the first");
                used[a] = true;
                if (a != b) differ++;
            }
            for (int f = 0; f < used.Length; f++) Assert.IsTrue(used[f], "the routine uses cheer pose " + f);
            Assert.Greater(differ, 0, "the two squads aren't mirror copies");
        }

        [Test]
        public void TheCheerleaderRaisesHerPomPoms()
        {
            var cfg = ConfigTests.Load();
            var team = cfg.teams[0];
            var kit = Looks.Toon(null);
            var fig = FootballFigure.Create(null, team, 0, FootballFigure.Build.Slim, 0, cfg.view.playerHeightM, kit, null);
            try
            {
                var skin = ModelSkin.Wear(fig, team.cheerModel, team.cheerTexture, kit, false);
                Assert.IsNotNull(skin, "the cheerleader model is worn");
                var smr = fig.transform.Find("Model").GetComponentInChildren<SkinnedMeshRenderer>();   // (not the hidden figure's own mesh)
                Assert.IsTrue(smr.enabled, "the model shows");
                foreach (var r in fig.GetComponentsInChildren<Renderer>(true))
                    if (r != smr) Assert.IsFalse(r.enabled, "the figure built from code is hidden: " + r.name);
                Assert.IsNotNull(smr.sharedMaterial.GetTexture("_MainTex"), "her colour map");
                Assert.LessOrEqual(smr.sharedMesh.triangles.Length / 3, 25000, "slimmed like the players");
                Transform Bone(string n)
                {
                    foreach (var t in smr.bones) if (t.name == n) return t;
                    return null;
                }
                Transform head = Bone("Head"), handL = Bone("LeftHand"), handR = Bone("RightHand"), hips = Bone("Hips");

                fig.Pose(FootballFigure.PoseKind.Cheer, 1);   // the high V
                skin.Follow();
                Assert.Greater(handL.position.y, head.position.y, "high V: the left pom-pom above her head");
                Assert.Greater(handR.position.y, head.position.y, "high V: the right pom-pom above her head");
                float spread = Vector3.Distance(handL.position, handR.position);

                fig.Pose(FootballFigure.PoseKind.Cheer, 4);   // both arms straight up
                skin.Follow();
                Assert.Less(Vector3.Distance(handL.position, handR.position), spread, "straight up is narrower than the V");

                fig.Pose(FootballFigure.PoseKind.Cheer, 0);   // ready: pom-poms low
                skin.Follow();
                Assert.Less(handL.position.y, hips.position.y + 0.25f * cfg.view.playerHeightM);
            }
            finally { Object.DestroyImmediate(fig.gameObject); }
        }
    }
}
