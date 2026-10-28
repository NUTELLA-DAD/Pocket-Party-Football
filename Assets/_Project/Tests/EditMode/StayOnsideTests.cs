using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.stayOnside (Sim/Mover.cs): before the snap a person can shuffle about on their own side but can't run
    // across the line of scrimmage; once the ball is hiked they can go anywhere.
    public sealed class StayOnsideTests
    {
        static GameConfig Cfg(bool on)
        {
            var cfg = ConfigTests.Load();
            cfg.features["stayOnside"] = on;
            return cfg;
        }

        static int Ticks(GameConfig c, float s) => (int)(s * c.sim.tickHz);

        // How far past the line the player stands, the player's team's way (negative: still on the own side).
        static float Past(World w, Player p)
        {
            float own = p.Team == w.Offense ? -w.Dir : w.Dir;
            return (w.LineOfScrimmageX - p.X) * own;
        }

        // A lane across the field with nobody lined up in it, so the run at the line meets no one.
        const float Lane = -10f;

        // Moves the person into the empty lane, then runs them at the line (sprinting) until the given phase is over;
        // returns the furthest past the line they got.
        static float RushUntil(World w, Player me, Phase phase, int maxTicks)
        {
            me.Z = Lane;
            float own = me.Team == w.Offense ? -w.Dir : w.Dir;
            var push = new System.Collections.Generic.Dictionary<int, PlayerInput> { [me.Slot] = new PlayerInput { MoveX = -own, Sprint = true } };
            float most = float.MinValue;
            for (int i = 0; i < maxTicks && w.Phase == phase; i++)
            {
                w.Step(push);
                if (w.Phase == phase) most = System.Math.Max(most, Past(w, me));
            }
            return most;
        }

        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("stayOnside"), "FEATURES.stayOnside is in game.json");
            Assert.Greater(cfg.players.onsideGapYd, 0f);
            Assert.AreEqual("NO_STAY_ONSIDE", ConfigLoader.EnvName("stayOnside"));
            var on = Cfg(true);
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_STAY_ONSIDE" ? "1" : null);
            Assert.IsFalse(on.Feature("stayOnside"));
        }

        [Test]
        public void ADefenderCantCrossInTheHuddleOrTheLineUpButCanAfterTheHike()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            var me = w.ReplaceBot(w.Defense, 500);
            int limit = Ticks(cfg, 60f);
            Assert.LessOrEqual(RushUntil(w, me, Phase.Huddle, limit), -cfg.players.onsideGapYd + 0.001f, "the huddle: held short of the line");
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.LessOrEqual(RushUntil(w, me, Phase.Set, limit), -cfg.players.onsideGapYd + 0.001f, "the line-up: held short of the line");
            Assert.AreEqual(Phase.Live, w.Phase, "the bot QB hiked it");
            float own = me.Team == w.Offense ? -w.Dir : w.Dir;
            me.X = w.LineOfScrimmageX - own * 3f; me.Z = Lane; me.VX = me.VZ = 0f;   // three yards over, in the open
            w.Step(null);
            Assert.Greater(Past(w, me), 2f, "after the snap nothing holds him back");
        }

        [Test]
        public void AnOffensivePlayerCantCrossBeforeTheSnapEither()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Offense, 500);   // the QB, ball in hand
            Assert.LessOrEqual(RushUntil(w, me, Phase.Huddle, Ticks(cfg, 60f)), -cfg.players.onsideGapYd + 0.001f);
        }

        [Test]
        public void ShufflingAlongTheLineStillWorks()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Defense, 500);
            float z0 = me.Z;
            var side = new System.Collections.Generic.Dictionary<int, PlayerInput> { [me.Slot] = new PlayerInput { MoveZ = 1f } };
            for (int i = 0; i < Ticks(cfg, 0.5f) && w.Phase == Phase.Huddle; i++) w.Step(side);
            Assert.Greater(me.Z - z0, 1f, "sideways moves aren't held");
        }

        [Test]
        public void WithTheSwitchOffADefenderCanCrossBeforeTheSnap()
        {
            var cfg = Cfg(false);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Defense, 500);
            Assert.Greater(RushUntil(w, me, Phase.Huddle, Ticks(cfg, 60f)), 0f, "the old way: nothing stops him");
        }
    }
}
