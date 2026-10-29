using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The body in motion, closer to how football players move: the spine bends as a curve, not one plank at the waist
    // (FEATURES.spineChain); the pelvis turns and dips with the stride (FEATURES.strideHips); a defender closing on the
    // ball carrier breaks down for the tackle (FEATURES.breakdown).
    public sealed class BodyMotionTests
    {
        static Transform Bone(Component on, string name)
        {
            foreach (var t in on.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        [Test]
        public void TheSwitchesAreInGameJsonAndOffByEnv()
        {
            foreach (var (name, env) in new[] { ("spineChain", "NO_SPINE_CHAIN"), ("strideHips", "NO_STRIDE_HIPS"), ("breakdown", "NO_BREAKDOWN") })
            {
                Assert.IsTrue(ConfigTests.Load().Feature(name), name + " ships on");
                Assert.AreEqual(env, ConfigLoader.EnvName(name));
                Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { env, "1" } }).Feature(name));
            }
        }

        [Test]
        public void EveryPlayerModelBendsThroughItsWholeSpine()
        {
            var cfg = ConfigTests.Load();
            var mat = Looks.Toon(null);
            var positions = new List<string> { null };
            foreach (var team in cfg.teams) foreach (var pm in team.posModels) positions.Add(pm.pos);
            foreach (var team in cfg.teams)
                foreach (var pos in positions)
                {
                    string who = team.@short + " " + (pos ?? "player");
                    // the same pose, the spine in one piece and as a curve
                    var shoulders = new Quaternion[2];
                    var chestBend = new float[2];
                    for (int k = 0; k < 2; k++)
                    {
                        cfg.features["spineChain"] = k == 1;
                        var fig = FootballFigure.Create(null, "Test", cfg.view.playerHeightM);
                        try
                        {
                            Assert.IsTrue(ModelSkin.Wear(fig, team, pos, cfg, mat), who + " wears its model");
                            var skin = fig.GetComponentInChildren<ModelSkin>();
                            Assert.AreEqual(k == 1, skin.Curved, who + ": a curved spine only with the switch on");
                            fig.Pose(FootballFigure.PoseKind.Block, 1);   // (bent well over: lean 46)
                            skin.Follow();
                            var chest = Bone(skin, "Chest");
                            var upper = Bone(skin, "UpperChest");
                            shoulders[k] = upper.rotation;
                            chestBend[k] = Quaternion.Angle(chest.rotation, Bone(skin, "Spine").rotation);
                        }
                        finally { Object.DestroyImmediate(fig.gameObject); }
                    }
                    TestContext.WriteLine(who + ": chest on spine " + chestBend[0].ToString("0.0") + " deg in one piece, " + chestBend[1].ToString("0.0") + " deg curved");
                    Assert.Less(Quaternion.Angle(shoulders[0], shoulders[1]), 0.5f, who + ": the shoulders end up turned just as before (the arms carry on from there)");
                    Assert.Greater(chestBend[1], chestBend[0] + 5f, who + ": the chest bends on the spine, not just at the waist");
                }
            cfg.features["spineChain"] = true;
        }

        [Test]
        public void ThePelvisTurnsWithTheLegReachingForwardAndDipsOnTheSwingingSide()
        {
            var cfg = ConfigTests.Load();
            var g = StrideLegs.Figure;
            float cm = cfg.field.yardM * FootballFigure.RigHeight / cfg.view.playerHeightM, v = cfg.players.sprintSpeedYdS;
            var land = Stride.Pose(cfg.anim, cfg.players, g, 0f, v, cm);
            Assert.Greater(land.HipYaw, 0f, "the left foot landing: his left hip is forward");
            var other = Stride.Pose(cfg.anim, cfg.players, g, 0.5f, v, cm);
            Assert.Less(other.HipYaw, 0f, "the right foot landing: his right hip is forward");
            Assert.Less(land.HipYaw * land.Twist, 0f, "the shoulders turn against the hips");
            float stance = cfg.anim.strideStance[2];
            Assert.Less(Stride.Pose(cfg.anim, cfg.players, g, stance * 0.5f, v, cm).HipRoll, 0f, "on his left foot the right (swinging) side dips");
            Assert.Greater(Stride.Pose(cfg.anim, cfg.players, g, 0.5f + stance * 0.5f, v, cm).HipRoll, 0f, "and the other way on his right");
            var off = Stride.Pose(cfg.anim, cfg.players, g, 0f, v, cm, true, false);
            Assert.AreEqual(0f, off.HipYaw); Assert.AreEqual(0f, off.HipRoll);
            // smooth all the way round
            float was = Stride.Pose(cfg.anim, cfg.players, g, 0f, v, cm).HipYaw;
            for (int i = 1; i <= 400; i++)
            {
                float now = Stride.Pose(cfg.anim, cfg.players, g, i / 400f, v, cm).HipYaw;
                Assert.Less(Mathf.Abs(now - was), 0.5f, "no jump in the pelvis at phase " + i / 400f);
                was = now;
            }
        }

        // A carrier running +x at 7 down the middle and one defender.
        static (World w, Player c, Player d) Pair(GameConfig cfg)
        {
            var w = new World(cfg, 5);
            var c = w.AddPlayer(0, w.Offense, false);
            var d = w.AddPlayer(1, w.Defense, false);
            w.Ball.GiveTo(c, cfg.ball);
            w.Phase = Phase.Live;
            c.X = 0f; c.Z = 0f; c.VX = 7f; c.VZ = 0f;
            return (w, c, d);
        }

        [Test]
        public void ADefenderBreaksDownClosingOnTheCarrierButRunsThroughFromBehind()
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            var a = cfg.anim;
            var (w, c, d) = Pair(cfg);
            d.X = a.gatherFromYd + 1f; d.Z = 0f;
            Assert.AreEqual(0f, PlayersView.Breakdown(w, d, cfg), "too far off");
            d.X = (a.gatherFromYd + a.gatherToYd) * 0.5f;
            float mid = PlayersView.Breakdown(w, d, cfg);
            Assert.That(mid, Is.InRange(0.3f, 0.7f), "half way in, half gathered");
            d.X = a.gatherToYd * 0.9f;
            Assert.AreEqual(1f, PlayersView.Breakdown(w, d, cfg), 1e-4f, "squared up in front of him");
            d.X = 0f; d.Z = a.gatherToYd;
            Assert.AreEqual(1f, PlayersView.Breakdown(w, d, cfg), 1e-4f, "coming from the side");
            d.X = -a.gatherToYd; d.Z = 0f;
            Assert.AreEqual(0f, PlayersView.Breakdown(w, d, cfg), "chasing him from behind he runs on through");
            Assert.AreEqual(0f, PlayersView.Breakdown(w, c, cfg), "not the carrier himself");
            cfg.features["breakdown"] = false;
            d.X = a.gatherToYd;
            Assert.AreEqual(0f, PlayersView.Breakdown(w, d, cfg), "switched off");
        }

        [Test]
        public void BreakingDownSinksTheHipsWithTheFeetStillOnTheGrass()
        {
            var cfg = ConfigTests.Load();
            var g = StrideLegs.Figure;
            float cm = cfg.field.yardM * FootballFigure.RigHeight / cfg.view.playerHeightM, v = cfg.anim.gatherGaitYdS;
            for (int i = 0; i < 20; i++)
            {
                float ph = i / 20f;
                var up = Stride.Pose(cfg.anim, cfg.players, g, ph, v, cm, true, true, 0f);
                var down = Stride.Pose(cfg.anim, cfg.players, g, ph, v, cm, true, true, 1f);
                Assert.Greater(down.Drop, up.Drop + 2f, "lower at phase " + ph);
            }
        }
    }
}
