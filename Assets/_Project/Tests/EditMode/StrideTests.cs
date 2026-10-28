using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.strideCycle (View/Stride.cs): a whole stride with every in-between. Walked through a stride in small
    // steps on the #24 model's short legs and on the figure's own: nothing jumps, a planted foot stays put on the grass,
    // the knee gives under load and folds in the swing, a sprint leaves the grass and a walk never does.
    public sealed class StrideTests
    {
        // the Chiefs #24 model's left leg standing (rig cm), as ModelSkin measures it
        static readonly StrideLegs Model = new StrideLegs { HipY = 53.6f, Thigh = 14.3f, Shin = 27.4f, Ankle = 12.1f, ShinRest = -4.2f, Foot = 13f, FootDropDeg = 40f };
        static readonly StrideLegs[] Legs = { Model, StrideLegs.Figure };
        const int Steps = 800;

        static GameConfig cfg;
        static float CmPerYd => cfg.field.yardM * FootballFigure.RigHeight / cfg.view.playerHeightM;
        static float[] Speeds => new[] { cfg.anim.strideWalkYdS, cfg.players.jogSpeedYdS, cfg.players.sprintSpeedYdS };

        [SetUp] public void Load() => cfg = ConfigTests.Load();

        static StridePose At(StrideLegs g, float phase, float speed) => Stride.Pose(cfg.anim, cfg.players, g, phase, speed, CmPerYd);

        // a foot's ankle (x ahead of the hip, y up from the grass) from the figure's angles
        static Vector2 Ankle(StrideLegs g, StridePose p, bool left)
        {
            float thigh = left ? p.ThighL : p.ThighR, shin = left ? p.ShinL : p.ShinR;
            float t = (thigh + g.ThighRest) * Mathf.Deg2Rad, s = (thigh + shin + g.ShinRest) * Mathf.Deg2Rad;
            float hip = g.HipY - p.Drop;
            return new Vector2(g.Thigh * Mathf.Sin(t) + g.Shin * Mathf.Sin(s), hip - g.Thigh * Mathf.Cos(t) - g.Shin * Mathf.Cos(s));
        }

        static IEnumerable<float> Values(StridePose p)
        {
            yield return p.ThighL; yield return p.ShinL; yield return p.ThighR; yield return p.ShinR;
            yield return p.ArmL; yield return p.ForeL; yield return p.ArmR; yield return p.ForeR;
            yield return p.ToeL; yield return p.ToeR; yield return p.BallL; yield return p.BallR; yield return p.Twist;
        }

        [Test]
        public void NothingJumpsBetweenInBetweens()
        {
            foreach (var g in Legs)
                foreach (float v in Speeds)
                {
                    var was = At(g, 0f, v);
                    for (int i = 1; i <= Steps; i++)
                    {
                        var now = At(g, (float)i / Steps, v);   // (the last one wraps round to the first)
                        var a = new List<float>(Values(was)); var b = new List<float>(Values(now));
                        for (int j = 0; j < a.Count; j++)
                            Assert.Less(Mathf.Abs(b[j] - a[j]), 4f, "angle " + j + " at " + v + " yd/s, step " + i + " of " + Steps);
                        Assert.Less(Mathf.Abs(now.Drop - was.Drop), 1f, "the hips at " + v + " yd/s, step " + i);
                        was = now;
                    }
                }
        }

        [Test]
        public void APlantedFootStaysPutAndNoFootGoesUnderTheGrass()
        {
            foreach (var g in Legs)
                foreach (float v in Speeds)
                {
                    float cycle = Stride.CycleYd(cfg.anim, cfg.players, v) * CmPerYd;
                    float? plantedAt = null; float slid = 0f;
                    for (int i = 0; i <= Steps; i++)
                    {
                        float phase = (float)i / Steps;
                        var p = At(g, phase, v);
                        var l = Ankle(g, p, true); var r = Ankle(g, p, false);
                        Assert.GreaterOrEqual(l.y, g.Ankle - 0.1f, "left foot at " + v + " yd/s, phase " + phase);
                        Assert.GreaterOrEqual(r.y, g.Ankle - 0.1f, "right foot at " + v + " yd/s, phase " + phase);
                        // flat on the grass, the left foot's place on the field (the body has run phase strides)
                        if (l.y < g.Ankle + 0.3f && phase < 0.1f)
                        {
                            float field = l.x + phase * cycle;
                            if (plantedAt == null) plantedAt = field;
                            slid = Mathf.Max(slid, Mathf.Abs(field - plantedAt.Value));
                        }
                    }
                    Assert.IsNotNull(plantedAt, "the left foot is down at the start of the stride (" + v + " yd/s)");
                    Assert.Less(slid, 0.5f, "a planted foot doesn't slide (" + v + " yd/s)");
                }
        }

        [Test]
        public void TheKneeGivesOnLandingAndFoldsInTheSwing()
        {
            foreach (float v in new[] { cfg.players.jogSpeedYdS, cfg.players.sprintSpeedYdS })
            {
                bool sprint = v >= cfg.players.sprintSpeedYdS;
                float straightest = 180f, mostFold = 0f;
                for (int i = 0; i < Steps; i++)
                {
                    var p = At(Model, (float)i / Steps, v);
                    straightest = Mathf.Min(straightest, -p.ShinL);
                    mostFold = Mathf.Max(mostFold, -p.ShinL);
                }
                Assert.Greater(straightest, 5f, "never locked straight (" + v + " yd/s)");
                Assert.Greater(mostFold, sprint ? 120f : 85f, "the heel comes up behind, higher at a sprint (" + v + " yd/s)");
                // over the planted foot the knee bends more than it did touching down
                var land = At(Model, 0f, v); var load = At(Model, 0.06f, v);
                Assert.Greater(-load.ShinL, -land.ShinL, "the knee gives under the load (" + v + " yd/s)");
            }
        }

        [Test]
        public void ASprintLeavesTheGrassAndAWalkNeverDoes()
        {
            bool Flies(float v)
            {
                for (int i = 0; i < Steps; i++)
                {
                    var p = At(Model, (float)i / Steps, v);
                    if (Ankle(Model, p, true).y > Model.Ankle + 1.5f && Ankle(Model, p, false).y > Model.Ankle + 1.5f) return true;
                }
                return false;
            }
            Assert.IsTrue(Flies(cfg.players.sprintSpeedYdS), "both feet off the grass for a moment");
            Assert.IsFalse(Flies(cfg.anim.strideWalkYdS), "a walk always has a foot down");
            Assert.Greater(Stride.CycleYd(cfg.anim, cfg.players, cfg.players.sprintSpeedYdS), Stride.CycleYd(cfg.anim, cfg.players, cfg.players.jogSpeedYdS));
            Assert.Greater(Stride.CycleYd(cfg.anim, cfg.players, cfg.players.jogSpeedYdS), Stride.CycleYd(cfg.anim, cfg.players, cfg.anim.strideWalkYdS));
        }

        [Test]
        public void TheArmsSwingAgainstTheLegs()
        {
            float most = float.MinValue; StridePose atMost = default;
            for (int i = 0; i < Steps; i++)
            {
                var p = At(Model, (float)i / Steps, cfg.players.jogSpeedYdS);
                if (p.ThighL > most) { most = p.ThighL; atMost = p; }
            }
            Assert.Less(atMost.ArmL, atMost.ArmR, "left knee forward, left arm back");
            Assert.Greater(atMost.ForeR, atMost.ForeL, "the forward arm's elbow bends more");
        }

        [Test]
        public void StandingStillHoldsTheStride()
        {
            Assert.AreEqual(0.3f, Stride.Advance(0.3f, cfg.anim, cfg.players, 6f, 0f), 1e-6f);
            float yd = Stride.CycleYd(cfg.anim, cfg.players, 6f);
            Assert.AreEqual(0.55f, Stride.Advance(0.3f, cfg.anim, cfg.players, 6f, yd * 0.25f), 1e-4f, "a quarter of a stride run");
        }

        // FEATURES.footFlex: pushing off, the heel comes up but the toes stay flat on the grass (the foot bends at its ball);
        // in the air the toes come back straight and lift to clear the grass; switched off, the foot never bends.
        [Test]
        public void TheFootBendsAtItsBallPushingOff()
        {
            foreach (float v in Speeds)
            {
                float most = 0f; StridePose atMost = default; float lifted = 0f;
                for (int i = 0; i < Steps; i++)
                {
                    var p = At(Model, (float)i / Steps, v);
                    if (p.BallL > most) { most = p.BallL; atMost = p; }
                    lifted = Mathf.Max(lifted, p.ToeL);
                    Assert.GreaterOrEqual(p.BallL, 0f, "the toes never bend down (" + v + " yd/s)");
                    var off = Stride.Pose(cfg.anim, cfg.players, Model, (float)i / Steps, v, CmPerYd, false);
                    Assert.AreEqual(0f, off.BallL, "switched off (" + v + " yd/s)");
                }
                Assert.Greater(most, 10f, "the foot bends at its ball (" + v + " yd/s)");
                Assert.Less(Mathf.Abs(atMost.ToeL + atMost.BallL), 0.2f * most, "the toes stay about flat (" + v + " yd/s)");
                Assert.Greater(lifted, 3f, "the toes lift somewhere in the stride (" + v + " yd/s)");
            }
            Assert.AreEqual("NO_FOOT_FLEX", ConfigLoader.EnvName("footFlex"));
            Assert.IsTrue(cfg.Feature("footFlex"));
        }

        [Test]
        public void TheSwitchTurnsItOff()
        {
            Assert.AreEqual("NO_STRIDE_CYCLE", ConfigLoader.EnvName("strideCycle"));
            Assert.IsTrue(cfg.Feature("strideCycle"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { ["NO_STRIDE_CYCLE"] = "1" }).Feature("strideCycle"));
        }
    }
}
