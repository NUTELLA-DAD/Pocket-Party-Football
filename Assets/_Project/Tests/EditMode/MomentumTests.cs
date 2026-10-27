using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.momentumLean (View/Momentum.cs): the facing turns at a limited rate, small cuts lean a little, a full
    // reverse plants and leans hard the new way, then pushes off leaning forward. Driven by the real sim (the lab's
    // "cuts" drill, the same run as Boot/MotionReel.cs), so the leans come from the game's own speeding up and slowing.
    public sealed class MomentumTests
    {
        struct Seen { public float maxFwd, minFwd, maxSide, maxPlant, maxTurn; }

        static readonly (int ticks, float x, float z, bool sprint, string label)[] Run =
        {
            (12, 0f, 0f, false, "stand"),
            (45, 1f, 0f, false, "start"),
            (15, 0.94f, 0.34f, false, "cuts"), (15, 0.94f, -0.34f, false, "cuts"), (15, 0.94f, 0.34f, false, "cuts"), (15, 0.94f, -0.34f, false, "cuts"),
            (30, 1f, 0f, true, "sprint"),
            (60, -1f, 0f, true, "reverse"),
        };

        static Dictionary<string, Seen> Drive(out GameConfig cfg)
        {
            cfg = ConfigTests.Load();
            var w = new World(cfg, 1);
            var lab = new Lab(w, Lab.Parse(File.ReadAllText("Assets/_Project/Config/lab.json")));
            lab.Pick(lab.FindDrill("cuts"));
            var ms = default(MomentumState);
            var seen = new Dictionary<string, Seen>();
            float lastYaw = float.NaN;
            foreach (var r in Run)
                for (int i = 0; i < r.ticks; i++)
                {
                    w.Step(new Dictionary<int, PlayerInput> { [lab.You] = new PlayerInput { MoveX = r.x, MoveZ = r.z, Sprint = r.sprint } });
                    lab.AfterTick();
                    Assert.AreEqual(Phase.Live, w.Phase, "the cuts drill runs the whole way (" + r.label + ")");
                    var p = w.Find(lab.You);
                    Momentum.Step(ref ms, cfg.anim, p.X, p.Z, p.VX, p.VZ, 0f, cfg.players.sprintSpeedYdS, w.Dt);
                    Momentum.Lean(ms, cfg.anim, out float fwd, out float side, out float plant);
                    seen.TryGetValue(r.label, out var s);
                    s.maxFwd = Mathf.Max(s.maxFwd, fwd); s.minFwd = Mathf.Min(s.minFwd, fwd);
                    s.maxSide = Mathf.Max(s.maxSide, Mathf.Abs(side)); s.maxPlant = Mathf.Max(s.maxPlant, plant);
                    if (!float.IsNaN(lastYaw)) s.maxTurn = Mathf.Max(s.maxTurn, Mathf.Abs(Mathf.DeltaAngle(lastYaw, ms.Yaw)));
                    lastYaw = ms.Yaw;
                    seen[r.label] = s;
                }
            return seen;
        }

        [Test]
        public void TheFacingNeverSnapsRound()
        {
            var seen = Drive(out var cfg);
            float most = cfg.anim.turnSlowDegS / cfg.sim.tickHz + 0.01f;
            foreach (var kv in seen) Assert.LessOrEqual(kv.Value.maxTurn, most, kv.Key + ": at most turnSlowDegS a second");
        }

        [Test]
        public void StandingStillDoesNotLean()
        {
            var s = Drive(out _)["stand"];
            Assert.AreEqual(0f, s.maxFwd, "(standing: no speed lean either)"); Assert.AreEqual(0f, s.minFwd); Assert.AreEqual(0f, s.maxSide);
        }

        [Test]
        public void SmallCutsShiftTheStanceALittle()
        {
            var seen = Drive(out var cfg);
            var c = seen["cuts"];
            Assert.Greater(c.maxSide, 0f, "a cut leans into it");
            Assert.LessOrEqual(c.maxSide, cfg.anim.sideCapDeg, "but only a little");
            Assert.AreEqual(0f, c.maxPlant, "no plant on a small cut");
            Assert.Greater(seen["start"].maxFwd, 0f, "speeding up leans forward");
        }

        [Test]
        public void AFullReversePlantsLeansBackThenPushesOff()
        {
            var seen = Drive(out var cfg);
            var r = seen["reverse"];
            Assert.AreEqual(1f, r.maxPlant, "the lead leg digs in");
            Assert.LessOrEqual(r.minFwd, -cfg.anim.leanMaxDeg * 0.7f, "a big lean back, the new way");
            Assert.GreaterOrEqual(r.maxFwd, cfg.anim.leanMaxDeg * 0.5f, "then forward pushing off");
            Assert.Greater(Mathf.Abs(r.minFwd), 1.5f * seen["cuts"].maxSide, "much bigger than a small cut");
            Assert.LessOrEqual(r.maxSide, cfg.anim.sideCapDeg, "mid turn-around he never tips over sideways");
        }

        [Test]
        public void ATurnAroundSwingsThroughTheCameraSide()
        {
            // facing +x (0), turning to face -x (180 / -180): the way through +90 (toward the camera on the -z sideline)
            Assert.Greater(Momentum.Turn(0f, 180f, 30f), 0f);
            Assert.Greater(Momentum.Turn(0f, -179f, 30f), 0f);
            Assert.Less(Momentum.Turn(180f, 0f, 30f), 0f);
            Assert.AreEqual(-30f, Momentum.Turn(0f, -90f, 30f), "a quarter turn goes the short way");
        }

        [Test]
        public void APlayerPutBackOnHisSpotStartsFresh()
        {
            var cfg = ConfigTests.Load();
            var ms = default(MomentumState);
            Momentum.Step(ref ms, cfg.anim, 0f, 0f, 8f, 0f, 0f, 8.5f, 1f / 30f);
            Momentum.Step(ref ms, cfg.anim, 0.27f, 0f, 0f, 0f, 0f, 8.5f, 1f / 30f);   // a hard stop: leans back
            Momentum.Lean(ms, cfg.anim, out float fwd, out _, out _);
            Assert.Less(fwd, 0f);
            Momentum.Step(ref ms, cfg.anim, 20f, 5f, 0f, 0f, 180f, 8.5f, 1f / 30f);   // lined up somewhere else
            Momentum.Lean(ms, cfg.anim, out fwd, out float side, out _);
            Assert.AreEqual(0f, fwd); Assert.AreEqual(0f, side);
            Assert.AreEqual(180f, Mathf.Abs(ms.Yaw), 0.01f, "facing his team's way");
        }
    }
}
