using System;
using NUnit.Framework;

namespace PocketFootball.Tests.PlayMode
{
    // WS-01: the PlayMode tests boot the real game, which reads NO_KICKING from the environment. With kicking on the
    // game opens with a kickoff, and these tests expect the first huddle, so they all run with NO_KICKING=1 (the
    // EditMode tests get the same from ConfigTests.Load). Put back whatever was there before when they finish.
    [SetUpFixture]
    public sealed class KickingOffForTests
    {
        string was;

        [OneTimeSetUp]
        public void Off()
        {
            was = Environment.GetEnvironmentVariable("NO_KICKING");
            Environment.SetEnvironmentVariable("NO_KICKING", "1");
        }

        [OneTimeTearDown]
        public void Back() => Environment.SetEnvironmentVariable("NO_KICKING", was);
    }
}
