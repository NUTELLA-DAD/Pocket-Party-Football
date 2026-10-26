using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-01: kickoffs, punts, field goals, tries and safeties (Sim/Kicking.cs, FEATURES.kicking), the bot kicker and the
    // kick meter (only the kicker's phone gets it). Kicking ships OFF, so each test turns it on for its own world.
    public sealed class KickingTests
    {
        static GameConfig On()
        {
            var cfg = ConfigTests.Load();
            cfg.features["kicking"] = true;
            return cfg;
        }

        // Steps until `done` (checked before every tick) or `seconds` of game time. False if time ran out first.
        static bool Run(World w, float seconds, Func<bool> done)
        {
            int n = (int)(seconds * w.Cfg.sim.tickHz);
            for (int i = 0; i < n; i++) { if (done()) return true; w.Step(null); }
            return done();
        }

        // A bot game past the opening kickoff, lined up at `los` on `down` (the offense attacks +X), in its first huddle.
        static World At(GameConfig cfg, float los, int down, float firstDownX, int seed = 1)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Kick.Opened = true;          // (no opening kickoff)
            w.LineOfScrimmageX = los;
            w.Down = down;
            w.FirstDownX = firstDownX;
            w.Step(null);                  // the first huddle lines everyone up at los
            Assert.AreEqual(Phase.Huddle, w.Phase);
            return w;
        }

        static float Goal(World w) => w.Cfg.field.GoalLineX;

        // ---- the switch ----

        [Test]
        public void ShipsOffAndChangesNothingWhenOff()
        {
            var cfg = ConfigTests.Load();
            Assert.IsFalse(cfg.Feature("kicking"), "kicking ships off; the merge captain turns it on and re-pins the baseline");
            var w = WorldTests.Full(cfg);
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase, "no kickoff with kicking off: the game opens with a huddle");
            Assert.IsFalse(w.Kick.Kickoff);
            w.Down = cfg.downs.downsToGain;
            foreach (var p in Huddle.Playbook(w, w.Offense)) Assert.IsFalse(Kicking.IsKickPlay(w, p), "no " + p.name + " with kicking off");
            Assert.IsNull(Kicking.BotCall(w, w.Offense));
            Assert.AreEqual(KickKind.None, Kicking.Mode(w));
        }

        [Test]
        public void NoKickingTurnsItOff()
        {
            Assert.AreEqual("NO_KICKING", ConfigLoader.EnvName("kicking"));
            var cfg = On();
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_KICKING" ? "1" : null);
            Assert.IsFalse(cfg.Feature("kicking"));
        }

        [Test]
        public void ThePlayFilesAreThere()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            foreach (var id in new[] { cfg.kicking.puntPlay, cfg.kicking.fgPlay })
            {
                var p = Huddle.Find(w, id);
                Assert.IsNotNull(p, id + ".json");
                Assert.AreEqual("offense", p.side);
                Assert.IsTrue(Kicking.IsKickPlay(w, p));
            }
            Assert.AreEqual(cfg.kicking.kickoffKicking.Count, cfg.formation.offense.Count, "a kickoff spot for every kicking-team player");
            Assert.AreEqual(cfg.kicking.kickoffReceiving.Count, cfg.formation.defense.Count, "a kickoff spot for every receiver");
        }

        // ---- kickoffs ----

        [Test]
        public void AKickoffOpensABotGame()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            int kicking = cfg.kicking.openingKickTeam, receiving = kicking == 0 ? 1 : 0;
            Assert.IsTrue(w.Kick.Kickoff, "the game opens with a kickoff");
            Assert.AreEqual(Phase.Set, w.Phase, "no huddle before a kickoff");
            Assert.AreEqual(kicking, w.Offense);
            Assert.AreEqual(-w.Dir * (Goal(w) - cfg.kicking.kickoffFromYd), w.LineOfScrimmageX, 1e-3f, "from the kicking team's own 35");
            Assert.AreSame(Kicking.Kicker(w), LivePlay.Carrier(w), "the kicker has the ball");
            Assert.AreEqual("KICKOFF", Kicking.Name(w));
            foreach (var p in w.Players)
            {
                float ahead = (p.X - w.LineOfScrimmageX) * w.Dir;
                if (p.Team == receiving) Assert.Greater(ahead, 0f, "receivers stand downfield");
                else Assert.Less(ahead, 0f, "the kicking team stands behind the ball");
            }

            Assert.IsTrue(Run(w, 30, () => w.Kick.Kicked), "the bot kicker kicks");
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            Assert.Greater(w.Ball.VY, 0f, "up in the air");
            Assert.IsTrue(Run(w, 40, () => w.Phase == Phase.Whistle), "the kickoff ends with a whistle");
            string result = w.Result;
            CollectionAssert.Contains(new[] { "TACKLED", "OUT OF BOUNDS", "TOUCHBACK", "DOWNED", "TOUCHDOWN" }, result);
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.AreEqual(receiving, w.Offense, "the receiving team has the ball after a kickoff (" + result + ")");
            Assert.IsFalse(w.Kick.Kickoff);
            Assert.AreEqual(Phase.Huddle, w.Phase, "then the first huddle");
            Assert.AreEqual(1, w.Down);
        }

        [Test]
        public void AReturnTouchdownGivesTheReturnersATry()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            int kicking = w.Offense, receiving = w.Defense;
            Assert.IsTrue(Run(w, 40, () => w.Kick.Returned || w.Phase == Phase.Whistle));
            Assume.That(w.Kick.Returned, "the returner fielded it (a muff into the end zone would be a touchback)");
            var returner = LivePlay.Carrier(w);
            Assert.AreEqual(receiving, returner.Team);
            Assert.IsTrue(w.Intercepted, "the run back uses the interception-return rules");
            returner.X = -w.Dir * (Goal(w) + 2f);   // he has gone all the way
            w.Step(null);
            Assert.AreEqual("TOUCHDOWN", w.Result, "a return touchdown is a touchdown, not a pick six");
            Assert.AreEqual(cfg.downs.touchdownPoints, w.Score[receiving]);
            Assert.AreEqual(0, w.Score[kicking]);
            Assert.IsTrue(w.Kick.Try);
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.AreEqual(receiving, w.Offense, "the returners try for the extra point");
            Assert.AreEqual(w.Dir * (Goal(w) - cfg.kicking.tryFromYd), w.LineOfScrimmageX, 1e-3f);
        }

        // ---- 4th down ----

        [Test]
        public void FourthAndLongPunts()
        {
            var cfg = On();
            var w = At(cfg, -20f, cfg.downs.downsToGain, -5f);   // 4th and 15 on our own 30
            var book = Huddle.Playbook(w, w.Offense);
            Assert.IsTrue(book.Exists(p => p.id == cfg.kicking.puntPlay), "PUNT is in the 4th-down playbook");
            Assert.IsTrue(book.Exists(p => p.id == cfg.kicking.fgPlay), "so is FIELD GOAL");
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(cfg.kicking.puntPlay, w.Calls[w.Offense], "4th and long out of range: the bot coach punts");
            Assert.AreEqual(KickKind.Punt, Kicking.Mode(w));
            var punter = Kicking.Kicker(w);
            Assert.AreEqual(w.LineOfScrimmageX - w.Dir * cfg.kicking.puntBackYd, punter.X, 1e-3f, "the punter stands deep");
            var returner = LivePlay.SpotOf(w, w.Defense, cfg.kicking.returnerSpot);
            Assert.Greater((returner.X - w.LineOfScrimmageX) * w.Dir, cfg.kicking.puntBackYd, "a returner waits deep");

            int kicking = w.Offense, other = w.Defense;
            float los = w.LineOfScrimmageX, dir = w.Dir;
            Assert.IsTrue(Run(w, 30, () => w.Kick.Kicked), "he punts");
            Assert.IsTrue(Run(w, 40, () => w.Phase == Phase.Whistle));
            if (w.Result == "TOUCHDOWN") Assert.AreEqual(cfg.downs.touchdownPoints, w.Score[other], "a punt return touchdown");
            else
            {
                Assert.IsTrue(w.PendingTurnover, "the other team gets it");
                Assert.Greater((w.NextLos - los) * dir, 0f, "the punt gained ground (" + w.Result + " at " + w.NextLos + ")");
            }
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.AreEqual(other, w.Offense);
            Assert.AreNotEqual(kicking, w.Offense);
            Assert.AreEqual(1, w.Down);
        }

        [Test]
        public void FourthAndShortGoesForIt()
        {
            var cfg = On();
            var w = At(cfg, -20f, cfg.downs.downsToGain, -19f);  // 4th and 1 on our own 30
            Assert.IsNull(Kicking.BotCall(w, w.Offense), "4th and 1: no kick, run a play");
            w.Down = cfg.downs.downsToGain - 1;
            w.FirstDownX = -5f;
            Assert.IsNull(Kicking.BotCall(w, w.Offense), "3rd down: never a kick");
            foreach (var p in Huddle.Playbook(w, w.Offense)) Assert.IsFalse(Kicking.IsKickPlay(w, p), "no " + p.name + " before 4th down");
        }

        [Test]
        public void AFieldGoalInRangeScoresThreeThenTheyKickOff()
        {
            var cfg = On();
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f);   // 4th and 10 on their 25
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(cfg.kicking.fgPlay, w.Calls[w.Offense], "in range: the bot coach kicks a field goal");
            Assert.AreEqual(KickKind.FieldGoal, Kicking.Mode(w));
            Assert.AreEqual(42, Kicking.KickYards(w), "25 to the goal line + 10 of end zone + 7 back");
            int kicking = w.Offense;
            Assert.IsTrue(Run(w, 40, () => w.Phase == Phase.Whistle));
            Assert.AreEqual("FIELD GOAL", w.Result);
            Assert.AreEqual(cfg.kicking.fieldGoalPoints, w.Score[kicking]);
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.IsTrue(w.Kick.Kickoff, "a kickoff after the field goal");
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.AreEqual(kicking, w.Offense, "the team that scored kicks off");
        }

        // A person kicks with the meter: straight and hard enough is good, way off to the side is not.
        [TestCase(0f, "FIELD GOAL")]
        [TestCase(1f, "NO GOOD")]
        [TestCase(-1f, "NO GOOD")]
        public void APersonsKickGoesWhereItIsAimed(float aim, string result)
        {
            var cfg = On();
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f);
            var me = w.ReplaceBot(w.Offense, 500);
            Assert.AreEqual(0, me.Spot, "the person takes the kicker's spot");
            Assert.IsTrue(Huddle.Lock(w, 500, cfg.kicking.fgPlay), "the caller can lock FIELD GOAL on 4th down");
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual("kick", PhoneLayouts.For(w, me), "the kicker's phone shows the kick meter");
            Assert.IsFalse(Kicking.Kick(w, 501, 1f, 0f), "nobody else can kick");
            float d = Math.Abs(w.Dir * cfg.field.HalfLength - me.X);
            float power = Kicking.FieldGoalPower(w, d) + 0.1f;
            int kicking = w.Offense;
            float los = w.LineOfScrimmageX;
            Assert.IsTrue(Kicking.Kick(w, 500, power, aim), "the swipe snaps and kicks");
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            Assert.IsFalse(Kicking.Kick(w, 500, power, aim), "one kick a play");
            Assert.IsTrue(Run(w, 20, () => w.Phase == Phase.Whistle));
            Assert.AreEqual(result, w.Result);
            if (result == "FIELD GOAL") Assert.AreEqual(cfg.kicking.fieldGoalPoints, w.Score[kicking]);
            else
            {
                Assert.AreEqual(0, w.Score[kicking]);
                Assert.IsTrue(w.PendingTurnover, "a miss: the other team takes over at the line");
                Assert.AreEqual(los, w.NextLos, 1e-3f);
            }
        }

        [Test]
        public void ATooShortFieldGoalIsNoGood()
        {
            var cfg = On();
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f);
            w.ReplaceBot(w.Offense, 500);
            Assert.IsTrue(Huddle.Lock(w, 500, cfg.kicking.fgPlay));
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.IsTrue(Kicking.Kick(w, 500, 0f, 0f), "a dribble");
            Assert.IsTrue(Run(w, 20, () => w.Phase == Phase.Whistle));
            Assert.AreEqual("NO GOOD", w.Result);
        }

        // ---- scoring ----

        [Test]
        public void ATouchdownIsFollowedByATry()
        {
            var cfg = On();
            var w = At(cfg, 40f, 1, 50f);   // 1st and goal on their 10
            Assert.IsTrue(Run(w, 60, () => w.Phase == Phase.Live));
            int scorer = w.Offense;
            var carrier = LivePlay.Carrier(w);
            carrier.X = w.Dir * (Goal(w) + 2f);   // he is in
            w.Step(null);
            Assert.AreEqual("TOUCHDOWN", w.Result);
            Assert.AreEqual(cfg.downs.touchdownPoints, w.Score[scorer]);
            Assert.IsTrue(w.Kick.Try, "a try comes next");
            Assert.IsFalse(w.Kick.PendingKickoff, "not a kickoff yet");

            Assert.IsTrue(Run(w, 10, () => w.Phase == Phase.Huddle));
            Assert.AreEqual(scorer, w.Offense, "the team that scored tries");
            Assert.AreEqual(w.Dir * (Goal(w) - cfg.kicking.tryFromYd), w.LineOfScrimmageX, 1e-3f, "from the try line");
            var book = Huddle.Playbook(w, w.Offense);
            Assert.IsTrue(book.Exists(p => p.id == cfg.kicking.fgPlay), "the extra point is in the try playbook");
            Assert.IsFalse(book.Exists(p => p.id == cfg.kicking.puntPlay), "no punting a try");
            Assert.IsTrue(book.Count > 1, "or run a play for two");

            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(cfg.kicking.fgPlay, w.Calls[w.Offense], "the bot kicks the extra point");
            Assert.AreEqual("EXTRA POINT", Kicking.Name(w));
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Whistle));
            Assert.AreEqual("EXTRA POINT", w.Result);
            Assert.AreEqual(cfg.downs.touchdownPoints + cfg.kicking.tryKickPoints, w.Score[scorer]);
            Assert.IsFalse(w.Kick.Try);
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.IsTrue(w.Kick.Kickoff, "then the scoring team kicks off");
            Assert.AreEqual(scorer, w.Offense);
            Assert.AreEqual(-w.Dir * (Goal(w) - cfg.kicking.kickoffFromYd), w.LineOfScrimmageX, 1e-3f);
        }

        [Test]
        public void ATwoPointTryCountsTwo()
        {
            var cfg = On();
            var w = At(cfg, 40f, 1, 50f);
            Assert.IsTrue(Run(w, 60, () => w.Phase == Phase.Live));
            int scorer = w.Offense;
            LivePlay.Carrier(w).X = w.Dir * (Goal(w) + 2f);
            w.Step(null);
            Assert.IsTrue(Run(w, 10, () => w.Phase == Phase.Huddle));
            Assert.IsTrue(w.Kick.Try);
            w.Calls[w.Offense] = "sneak";   // go for two
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Live));
            LivePlay.Carrier(w).X = w.Dir * (Goal(w) + 2f);
            w.Step(null);
            Assert.AreEqual(cfg.downs.touchdownPoints + cfg.kicking.tryTouchdownPoints, w.Score[scorer]);
            Assert.IsTrue(w.Kick.PendingKickoff);
        }

        [Test]
        public void ASafetyIsTwoPointsAndAFreeKick()
        {
            var cfg = On();
            var w = At(cfg, -47f, 1, -37f);   // backed up on our own 3
            Assert.IsTrue(Run(w, 60, () => w.Phase == Phase.Live));
            int off = w.Offense, def = w.Defense;
            LivePlay.Carrier(w).X = -w.Dir * (cfg.field.HalfLength + 1f);   // out the back of his own end zone
            w.Step(null);
            Assert.AreEqual("SAFETY", w.Result);
            Assert.AreEqual(cfg.kicking.safetyPoints, w.Score[def]);
            Assert.AreEqual(0, w.Score[off]);
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.IsTrue(w.Kick.Kickoff, "a free kick");
            Assert.AreEqual(off, w.Offense, "by the team that gave up the safety");
            Assert.AreEqual(-w.Dir * (Goal(w) - cfg.kicking.safetyKickFromYd), w.LineOfScrimmageX, 1e-3f, "from its own 20");
        }

        // Ten-minute bot games with kicking on: every point on the board comes from a scoring play worth exactly that,
        // every touchdown gets a try, every score is followed by a kickoff, and the game never gets stuck.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ScoresAddUpInABotGame(int seed)
        {
            var cfg = On();
            var w = WorldTests.Full(cfg, seed);
            var c = cfg.kicking;
            int[] seen = new int[cfg.teams.Count];
            int tds = 0, tries = 0, kicks = 0, fgs = 0, safeties = 0, kickoffs = 0, punts = 0, longest = 0, inPhase = 0;
            bool wasTry = false, wasKickoff = false, wasPunt = false;
            Phase last = w.Phase;
            var log = new StringBuilder();
            for (int i = 0; i < 10 * 60 * cfg.sim.tickHz; i++)
            {
                w.Step(null);
                if (w.Kick.Kickoff && !wasKickoff) kickoffs++;
                wasKickoff = w.Kick.Kickoff;
                bool punt = Kicking.Mode(w) == KickKind.Punt && w.Kick.Kicked;
                if (punt && !wasPunt) punts++;
                wasPunt = punt;
                if (w.Kick.Try && !wasTry) tries++;
                wasTry = w.Kick.Try;
                inPhase = w.Phase == last ? inPhase + 1 : 0;
                last = w.Phase;
                longest = Math.Max(longest, inPhase);
                for (int t = 0; t < seen.Length; t++)
                {
                    int delta = w.Score[t] - seen[t];
                    if (delta == 0) continue;
                    seen[t] = w.Score[t];
                    log.Append(' ').Append(cfg.teams[t].@short).Append(" +").Append(delta).Append(' ').Append(w.Result).Append(',');
                    Assert.AreEqual(Phase.Whistle, w.Phase, "points only on a whistle");
                    if (delta == cfg.downs.touchdownPoints) { CollectionAssert.Contains(new[] { "TOUCHDOWN", "PICK SIX" }, w.Result); tds++; }
                    else if (delta == c.fieldGoalPoints) { Assert.AreEqual("FIELD GOAL", w.Result); fgs++; }
                    else if (delta == c.tryKickPoints) { Assert.AreEqual("EXTRA POINT", w.Result); kicks++; }
                    else if (delta == c.safetyPoints && w.Result == "SAFETY") safeties++;
                    else if (delta != c.tryTouchdownPoints) Assert.Fail("+" + delta + " on " + w.Result + ":" + log);
                }
            }
            string report = "seed " + seed + ": " + w.PlaysRun + " plays, " + kickoffs + " kickoffs, " + punts + " punts, " + tds + " TD, "
                + kicks + " PAT, " + fgs + " FG, " + safeties + " safeties, score " + w.Score[0] + "-" + w.Score[1] + ";" + log;
            TestContext.WriteLine(report);
            Assert.GreaterOrEqual(kickoffs, 1, "the game opens with a kickoff: " + report);
            Assert.AreEqual(tds, tries, "every touchdown gets a try: " + report);
            Assert.LessOrEqual(kicks, tries, report);
            // after every field goal, try and safety comes a kickoff (the last one may still be on its way)
            Assert.GreaterOrEqual(kickoffs, fgs + tries + safeties, "a kickoff after every score: " + report);
            Assert.GreaterOrEqual(w.PlaysRun, 20, "plays keep happening: " + report);
            Assert.Less(longest, (int)((cfg.huddle.seconds + cfg.play.maxPlayS + cfg.play.whistleS) * cfg.sim.tickHz), "never stuck in one phase: " + report);
        }

        [Test]
        public void TheSecondHalfOpensWithAKickoffByTheOtherTeam()
        {
            var cfg = On();
            cfg.features["clock"] = true;
            cfg.clock.quarterS = 20f;
            cfg.clock.halftimeS = 1f;
            var w = WorldTests.Full(cfg);
            Assert.IsTrue(Run(w, 600, () => w.GameClock.SecondHalf), "the clock reaches the second half");
            Assert.IsTrue(w.Kick.Kickoff, "the second half opens with a kickoff, not a huddle");
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.AreNotEqual(cfg.kicking.openingKickTeam, w.Offense, "the team that received the opening kickoff kicks");
            Assert.AreEqual(-w.Dir * (Goal(w) - cfg.kicking.kickoffFromYd), w.LineOfScrimmageX, 1e-3f, "from its own 35");
            Assert.IsFalse(w.Kick.Try);
        }

        // ---- the meter ----

        [Test]
        public void TheMeterIsOnlyForTheKicker()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg);
            w.Step(null);   // the opening kickoff line-up
            var kicker = Kicking.Kicker(w);
            Assert.IsNotNull(kicker);
            foreach (var p in w.Players)
            {
                bool me = p == kicker;
                Assert.AreEqual(me, Kicking.WaitingToKick(w, p), p.Pos + " #" + p.Number);
                Assert.AreEqual(me, PhoneLayouts.For(w, p) == "kick", p.Pos + " #" + p.Number + " got " + PhoneLayouts.For(w, p));
            }
            Assert.IsTrue(Run(w, 30, () => w.Kick.Kicked));
            foreach (var p in w.Players)
            {
                Assert.IsFalse(Kicking.WaitingToKick(w, p), "the meter goes away once the ball is kicked");
                Assert.AreNotEqual("kick", PhoneLayouts.For(w, p));
            }

            // with kicking off, nobody ever gets it
            var off = WorldTests.Full(ConfigTests.Load());
            for (int i = 0; i < 60 * cfg.sim.tickHz; i++)
            {
                off.Step(null);
                foreach (var p in off.Players) Assert.AreNotEqual("kick", PhoneLayouts.For(off, p));
            }
        }

        [Test]
        public void OnFourthDownOnlyThePuntersPhoneGetsTheMeter()
        {
            var cfg = On();
            var w = At(cfg, -20f, cfg.downs.downsToGain, -5f);
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            int kicks = 0;
            foreach (var p in w.Players) if (PhoneLayouts.For(w, p) == "kick") { kicks++; Assert.AreSame(Kicking.Kicker(w), p); }
            Assert.AreEqual(1, kicks, "the punter gets the meter instead of HIKE, and nobody else does");
        }
    }
}
