using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The little things that keep a man alive (View/Alive.cs): idle breathing and weight shifts, heads that follow the
    // ball, the ball's spiral and tumble. All stepped on key frames (no wobble), each behind its own FEATURES switch.
    public sealed class AliveTests
    {
        static int TicksPerKey(GameConfig cfg) => Mathf.CeilToInt(cfg.sim.tickHz / cfg.anim.fps);

        [Test]
        public void IdleStepsOnKeyFramesAndStaysSmall()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("idleMotion"));
            int changes = 0, keys = 0;
            float lastB = float.NaN, lastS = float.NaN; int lastKey = int.MinValue;
            for (int tick = 0; tick < cfg.sim.tickHz * 20; tick++)
            {
                int key = Alive.Key(cfg, tick, 3);
                Alive.Idle(cfg, key, out float b, out float s);
                Assert.That(b, Is.InRange(0f, cfg.anim.idleBreathDeg), "a breath is a couple of degrees");
                Assert.That(s, Is.InRange(-1f, 1f));
                if (key == lastKey) { Assert.AreEqual(lastB, b); Assert.AreEqual(lastS, s); continue; }   // (held between keys)
                if (!float.IsNaN(lastB) && (b != lastB || s != lastS)) changes++;
                lastKey = key; lastB = b; lastS = s; keys++;
            }
            Assert.Greater(changes, keys / 2, "it does move, a key at a time");
        }

        [Test]
        public void TheWeightShiftReachesBothFeetAndHolds()
        {
            var cfg = ConfigTests.Load();
            int left = 0, right = 0;
            for (int key = 0; key < cfg.anim.idleShiftKeys; key++)
            {
                Alive.Idle(cfg, key, out _, out float s);
                if (s >= 1f) left++;
                if (s <= -1f) right++;
            }
            Assert.Greater(left, 2, "a moment on the left foot");
            Assert.Greater(right, 2, "a moment on the right foot");
        }

        [Test]
        public void TheLineDoesNotBreatheAsOne()
        {
            var cfg = ConfigTests.Load();
            int same = 0, n = 0;
            for (int tick = 0; tick < cfg.sim.tickHz * 5; tick += TicksPerKey(cfg))
            {
                Alive.Idle(cfg, Alive.Key(cfg, tick, 1), out float a, out _);
                Alive.Idle(cfg, Alive.Key(cfg, tick, 2), out float b, out _);
                if (Mathf.Approximately(a, b)) same++;
                n++;
            }
            Assert.Less(same, n / 2, "two men side by side breathe on their own beats");
        }

        [Test]
        public void NoIdleMotionSwitchesItOff()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_IDLE_MOTION", "1" } });
            for (int key = 0; key < 100; key++)
            {
                Alive.Idle(cfg, key, out float b, out float s);
                Assert.AreEqual(0f, b); Assert.AreEqual(0f, s);
            }
        }

        [Test]
        public void TheFeetStayPutWhileHeShiftsHisWeight()
        {
            var cfg = ConfigTests.Load();
            var fig = FootballFigure.Create(null, "Idle", cfg.view.playerHeightM);
            try
            {
                fig.SwayCm = cfg.anim.idleSwayCm; fig.RollDeg = cfg.anim.idleRollDeg; fig.KneeDeg = cfg.anim.idleKneeDeg;
                Vector3 Foot(Transform shin) => shin.TransformPoint(new Vector3(0f, -31f, 0f));
                fig.Idle(0f, 0f); fig.Pose(FootballFigure.PoseKind.Stand, 0);
                Vector3 footL = Foot(fig.ShinL), hips = fig.Hips.position, chest = fig.Torso.up;
                fig.Idle(cfg.anim.idleBreathDeg, 1f); fig.Pose(FootballFigure.PoseKind.Stand, 0);
                float m = cfg.view.playerHeightM / FootballFigure.RigHeight;   // metres per rig cm
                Assert.Less((Foot(fig.ShinL) - footL).magnitude, 1f * m, "the foot he stands on stays planted");
                Assert.AreEqual(cfg.anim.idleSwayCm * m, (fig.Hips.position - hips).z, 0.3f * m, "his hips go over his left foot");
                Assert.Greater(Vector3.Angle(chest, fig.Torso.up), cfg.anim.idleBreathDeg * 0.5f, "his chest comes up");
                Assert.Less(Vector3.Angle(chest, fig.Torso.up), cfg.anim.idleBreathDeg + cfg.anim.idleRollDeg + 1f, "only a little");
                fig.Idle(0f, 0f); fig.Pose(FootballFigure.PoseKind.Stand, 0);
                Assert.Less((fig.Hips.position - hips).magnitude, 1e-4f, "and back");
            }
            finally { Object.DestroyImmediate(fig.gameObject); }
        }

        [Test]
        public void HisHeadTurnsToTheBallAsFarAsANeckGoes()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("headTrack"));
            // facing +x (yaw 0); +90 yaw faces -z
            Assert.IsTrue(Alive.HeadTrack(cfg, 0f, 0f, 0f, 10f, 0.5f, -5f, out float yaw, out float tip));
            Assert.Greater(yaw, 0f, "the ball off to his right (-z): the head turns right");
            Assert.AreEqual(0f, Mathf.Repeat(yaw, cfg.anim.lookStepDeg), 1e-3f, "in lookStepDeg steps");
            Assert.LessOrEqual(tip, 0f, "a ball at waist height: level or a little down");
            Alive.HeadTrack(cfg, 0f, 0f, 0f, -10f, 1f, 0.5f, out yaw, out _);
            Assert.AreEqual(cfg.anim.trackHeadDeg, Mathf.Abs(yaw), 1e-3f, "the ball behind him: as far as his neck goes, no further");
            Alive.HeadTrack(cfg, 0f, 0f, 0f, 5f, 20f, 0f, out yaw, out tip);
            Assert.AreEqual(0f, yaw, 1e-3f);
            Assert.AreEqual(cfg.anim.trackUpDeg, tip, 1e-3f, "a punt high above: he looks up");
            Assert.IsFalse(Alive.HeadTrack(cfg, 0f, 0f, 0f, 0.1f, 1f, 0f, out _, out _), "the ball in his own hands: nothing to turn to");
        }

        [Test]
        public void TrackTurnsOnlyTheHead()
        {
            var cfg = ConfigTests.Load();
            var fig = FootballFigure.Create(null, "Track", cfg.view.playerHeightM);
            try
            {
                fig.Look(0f, 0f, float.NaN); fig.Pose(FootballFigure.PoseKind.Ready, 0);
                Quaternion chest = fig.Torso.rotation, head = fig.Head.rotation;
                fig.Track(40f, 10f); fig.Pose(FootballFigure.PoseKind.Ready, 0);
                Assert.Less(Quaternion.Angle(chest, fig.Torso.rotation), 1e-3f, "the shoulders stay square");
                Assert.Greater(Quaternion.Angle(head, fig.Head.rotation), 35f, "the head turns");
                fig.Look(0f, 0f, float.NaN); fig.Pose(FootballFigure.PoseKind.Ready, 0);
                Assert.Less(Quaternion.Angle(head, fig.Head.rotation), 1e-3f, "and the next Look puts it back");
            }
            finally { Object.DestroyImmediate(fig.gameObject); }
        }

        [Test]
        public void NoHeadTrackSwitchesItOff()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_HEAD_TRACK", "1" } });
            Assert.IsFalse(Alive.HeadTrack(cfg, 0f, 0f, 0f, 10f, 0.5f, -5f, out float yaw, out float tip));
            Assert.AreEqual(0f, yaw); Assert.AreEqual(0f, tip);
        }

        [Test]
        public void APassSpiralsAndAKickTumbles()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual(0f, Alive.Spin(cfg, 0, false), "it leaves the hand unturned");
            Assert.AreEqual(cfg.anim.spiralDegPerKey, Alive.Spin(cfg, 1, false), 1e-3f);
            Assert.AreEqual(cfg.anim.tumbleDegPerKey, Alive.Spin(cfg, 1, true), 1e-3f);
            for (int k = 0; k < 40; k++) Assert.That(Alive.Spin(cfg, k, false), Is.InRange(0f, 360f));
            // (not a multiple of 90 or 180 a key, or the strobe of 8 keys a second would make it look still)
            Assert.AreNotEqual(0f, Mathf.Repeat(cfg.anim.spiralDegPerKey, 180f));
            Assert.AreNotEqual(0f, Mathf.Repeat(cfg.anim.tumbleDegPerKey, 180f));
            var off = ConfigTests.Load(new Dictionary<string, string> { { "NO_BALL_SPIN", "1" } });
            Assert.AreEqual(0f, Alive.Spin(off, 5, false));
            Assert.AreEqual(0f, Alive.Spin(off, 5, true));
        }
    }
}
