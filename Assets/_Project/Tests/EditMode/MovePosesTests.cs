using NUnit.Framework;
using PocketFootball.View;
using Pose = PocketFootball.View.FootballFigure.PoseKind;

namespace PocketFootball.Tests.EditMode
{
    // Which figure pose each move shows, key frame by key frame (PlayersView.PoseFor).
    public sealed class MovePosesTests
    {
        static Pose PoseOf(MoveNow m, out int frame)
        {
            Assert.IsTrue(PlayersView.PoseFor(m, 0, out var kind, out frame), m.Kind + " has a pose");
            return kind;
        }

        [Test]
        public void EveryMoveHasAPose()
        {
            foreach (Move k in System.Enum.GetValues(typeof(Move)))
            {
                bool has = PlayersView.PoseFor(new MoveNow { Kind = k }, 0, out _, out _);
                Assert.AreEqual(k != Move.None, has, k.ToString());
            }
        }

        [Test]
        public void ATackledManFallsThenLiesThenGetsUp()
        {
            Assert.AreEqual(Pose.FallFront, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 0 }, out _));
            Assert.AreEqual(Pose.FallFront, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 1 }, out int f));
            Assert.AreEqual(1, f);
            Assert.AreEqual(Pose.Prone, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 9 }, out _));
            Assert.AreEqual(Pose.Supine, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 9, OnBack = true }, out _));
            Assert.AreEqual(Pose.Kneel, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 20, GettingUp = true, UpFrame = 0 }, out _));
            Assert.AreEqual(Pose.Stand, PoseOf(new MoveNow { Kind = Move.Tackled, Frame = 22, GettingUp = true, UpFrame = 3 }, out _));
        }

        [Test]
        public void TheTacklerLaunchesThenGoesDownOnHisFront()
        {
            Assert.AreEqual(Pose.Dive, PoseOf(new MoveNow { Kind = Move.Tackle, Frame = 0 }, out _));
            Assert.AreEqual(Pose.FallFront, PoseOf(new MoveNow { Kind = Move.Tackle, Frame = 1 }, out _));
            Assert.AreEqual(Pose.Prone, PoseOf(new MoveNow { Kind = Move.Tackle, Frame = 5, OnBack = true }, out _));
        }

        [Test]
        public void TheThrowComesOverTheTopThenFollowsThrough()
        {
            Assert.AreEqual(Pose.Throw, PoseOf(new MoveNow { Kind = Move.Aim }, out _));
            PoseOf(new MoveNow { Kind = Move.Throw, Frame = 0 }, out int a);
            PoseOf(new MoveNow { Kind = Move.Throw, Frame = 3 }, out int b);
            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            // FEATURES.throwMotion: wound up, then four keys from the release, held on the last
            PoseOf(new MoveNow { Kind = Move.Aim, Full = true }, out int up);
            Assert.AreEqual(1, up);
            for (int k = 0; k < 6; k++)
            {
                Assert.AreEqual(Pose.Release, PoseOf(new MoveNow { Kind = Move.Throw, Frame = k, Full = true }, out int f));
                Assert.AreEqual(2 + System.Math.Min(3, k), f);
            }
        }

        // FEATURES.kickMotion: set over the ball (a place kick or a punt), then the leg's five keys, held on the landing
        [Test]
        public void TheKickerSetsThenSwingsThrough()
        {
            Assert.AreEqual(Pose.KickSet, PoseOf(new MoveNow { Kind = Move.KickSet, Frame = 1 }, out int punt));
            Assert.AreEqual(1, punt);
            for (int k = 0; k < 8; k++)
            {
                Assert.AreEqual(Pose.Kick, PoseOf(new MoveNow { Kind = Move.Kick, Frame = k }, out int f));
                Assert.AreEqual(System.Math.Min(4, k), f);
            }
        }
    }
}
