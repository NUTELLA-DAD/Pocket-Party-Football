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
        }
    }
}
