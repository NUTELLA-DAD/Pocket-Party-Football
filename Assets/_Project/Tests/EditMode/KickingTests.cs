using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // WS-01: kickoffs, punts, field goals, tries and safeties (Sim/Kicking.cs, FEATURES.kicking), the bot kicker and the
    // kick meter (only the kicker's phone gets it). Kicking ships OFF, so each test turns it on for its own world.
    public sealed class KickingTests
    {
        static GameConfig On(bool wind = false, bool arc = false)
        {
            var cfg = ConfigTests.Load();
            cfg.features["kicking"] = true;
            cfg.features["wind"] = wind;   // (calm, so a straight kick goes straight; the wind has its own tests)
            cfg.features["kickArc"] = arc;   // (the field goal's sweep has its tests; the arc has its own below)
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
            Assert.IsFalse(cfg.Feature("kicking"), "the tests of other rules run with kicking off (ConfigTests.Load)");
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

        // FEATURES.kickerModel: the QB's spot turns into the kicker (his own model, PlayersView) only once a kick is lined
        // up, never in the huddle (the call stays secret), and back to the QB for the next huddle.
        [Test]
        public void TheKickerShowsOnlyForAKick()
        {
            var cfg = On();
            Assert.IsTrue(cfg.Feature("kickerModel"));
            var w = At(cfg, -20f, cfg.downs.downsToGain, -5f);   // 4th and 15 on our own 30: the bot coach punts
            Assert.IsTrue(Run(w, 30, () =>
            {
                if (w.Phase == Phase.Huddle) foreach (var p in w.Players) Assert.IsFalse(Kicking.ShowsKicker(w, p), "no kicker in the huddle");
                return w.Phase == Phase.Set;
            }));
            Assert.AreEqual(KickKind.Punt, Kicking.Mode(w));
            var kicker = Kicking.Kicker(w);
            foreach (var p in w.Players) Assert.AreEqual(p == kicker, Kicking.ShowsKicker(w, p), "only the punter, once lined up");
            Assert.IsTrue(Run(w, 30, () => w.Kick.Kicked));
            Assert.IsTrue(Kicking.ShowsKicker(w, kicker), "still the kicker with the ball in the air");
            Assert.IsTrue(Run(w, 60, () => w.Phase == Phase.Huddle));
            foreach (var p in w.Players) Assert.IsFalse(Kicking.ShowsKicker(w, p), "the QB again in the next huddle");
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            if (Kicking.Mode(w) == KickKind.None)
                foreach (var p in w.Players) Assert.IsFalse(Kicking.ShowsKicker(w, p), "an ordinary play has no kicker");

            var off = On();
            ConfigLoader.ApplyEnvOverrides(off, k => k == "NO_KICKER_MODEL" ? "1" : null);
            var w2 = WorldTests.Full(off);
            w2.Step(null);
            Assert.IsTrue(w2.Kick.Kickoff);
            Assert.IsFalse(Kicking.ShowsKicker(w2, Kicking.Kicker(w2)), "NO_KICKER_MODEL=1: the QB kicks in his own model");
            var w3 = WorldTests.Full(On());
            w3.Step(null);
            Assert.IsTrue(Kicking.ShowsKicker(w3, Kicking.Kicker(w3)), "the kicker takes the opening kickoff");
        }

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

        // FEATURES.kickMotion (View/Moves.cs): lined up for the kick the kicker stands over the ball; his kick swings his
        // leg (not a throw's arm) and ends after kickS; switched off, the kick shows as before (a throw).
        [TestCase(true)]
        [TestCase(false)]
        public void TheKickerSetsUpThenSwingsHisLeg(bool on)
        {
            var cfg = On();
            cfg.features["kickMotion"] = on;
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f);
            var me = w.ReplaceBot(w.Offense, 500);
            Assert.IsTrue(Huddle.Lock(w, 500, cfg.kicking.fgPlay));
            var m = new Moves();
            Assert.IsTrue(Run(w, 30, () => { m.Observe(w); return w.Phase == Phase.Set; }));
            m.Observe(w);
            if (on) Assert.AreEqual(Move.KickSet, m.Of(w, me).Kind, "over the ball");
            if (on) Assert.AreEqual(0, m.Of(w, me).Frame, "a place kick");
            Assert.IsTrue(Kicking.Kick(w, 500, 0.8f, 0f));
            // (FEATURES.fgHolder: he runs up to the holder first; the holder had the ball, so no throw either way)
            Assert.IsTrue(Run(w, 3, () => { m.Observe(w); return w.Kick.Kicked; }), "kicked once he is at the ball");
            w.Step(null); m.Observe(w);
            if (on) Assert.AreEqual(Move.Kick, m.Of(w, me).Kind, "the kick");
            else Assert.AreNotEqual(Move.Kick, m.Of(w, me).Kind, "no kick motion");
            for (int i = 0; i < (int)(cfg.anim.kickS * cfg.sim.tickHz) + 1; i++) { w.Step(null); m.Observe(w); }
            Assert.AreNotEqual(Move.Kick, m.Of(w, me).Kind, "the kick ends");
            Assert.AreEqual("NO_KICK_MOTION", ConfigLoader.EnvName("kickMotion"));
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
            float d = Math.Abs(w.Dir * cfg.field.HalfLength - w.Kick.HoldX);   // (from the holder's spot)
            float power = Kicking.FieldGoalPower(w, d) + 0.1f;
            int kicking = w.Offense;
            float los = w.LineOfScrimmageX;
            Assert.IsTrue(Kicking.Kick(w, 500, power, aim), "the swipe snaps and kicks");
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.IsFalse(Kicking.Kick(w, 500, power, aim), "one kick a play");
            Assert.IsTrue(Run(w, 3, () => w.Kick.Kicked), "the run-up, then the kick");
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
        // ---- the field goal's hold, the two taps and the wind ----

        // A field goal lined up by a person: our kicker, and (with `holder`) our holder too.
        static World FieldGoal(GameConfig cfg, out Player kicker, int seed = 1)
        {
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f, seed);
            kicker = w.ReplaceBot(w.Offense, 500);
            Assert.IsTrue(Huddle.Lock(w, 500, cfg.kicking.fgPlay));
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(KickKind.FieldGoal, Kicking.Mode(w));
            return w;
        }

        [Test]
        public void TheHolderTakesTheSnapAndTheKickerRunsUpToHim()
        {
            var cfg = On();
            Assert.IsTrue(cfg.Feature("fgHolder"), "FEATURES.fgHolder ships on");
            Assert.AreEqual("NO_FG_HOLDER", ConfigLoader.EnvName("fgHolder"));
            var w = FieldGoal(cfg, out var kicker);
            var c = cfg.kicking;
            var holder = Kicking.Holder(w);
            Assert.IsNotNull(holder, "a holder on a field goal");
            Assert.AreEqual(c.holderSpot, holder.Spot);
            Assert.AreEqual(w.Offense, holder.Team);
            Assert.AreSame(holder, Kicking.SnapTaker(w), "the snap goes to him");
            Assert.AreEqual(holder.Slot, w.Ball.Carrier, "lined up: the ball is his to hold");
            Assert.IsTrue(Kicking.Holding(w));
            Assert.AreEqual(w.LineOfScrimmageX - w.Dir * c.fieldGoalBackYd, w.Kick.HoldX, 1e-3f, "the spot, fieldGoalBackYd deep");
            Assert.AreEqual(w.Kick.HoldX, holder.X, 1e-3f);
            Assert.Greater((w.Kick.HoldX - kicker.X) * w.Dir, c.kickerBackYd - 0.1f, "the kicker stands back of the spot");
            Assert.Greater(kicker.Z * w.Dir, 0f, "on the holder's left");
            Assert.AreEqual("kick", PhoneLayouts.For(w, kicker));
            var near = w.ReplaceBot(w.Offense, 501);
            Assert.AreEqual(c.holderSpot, near.Spot, "the next person in takes the holder's spot");
            holder = Kicking.Holder(w);
            Assert.AreSame(near, holder);
            Assert.AreEqual(holder.Slot, w.Ball.Carrier, "with the ball");
            Assert.AreEqual("hold", PhoneLayouts.For(w, holder), "the holder's phone: just FIND ME, no stick");

            Assert.IsTrue(Kicking.Kick(w, 500, 0.85f, 0f));
            Assert.IsTrue(w.Kick.Approach, "the kicker runs up");
            Assert.IsFalse(w.Kick.Kicked, "not in the air yet");
            float was = (w.Kick.HoldX - kicker.X) * w.Dir;
            w.Step(null); w.Step(null);
            Assert.Less((w.Kick.HoldX - kicker.X) * w.Dir, was, "closer to the ball");
            Assert.IsTrue(Run(w, 3, () => w.Kick.Kicked));
            Assert.AreEqual(w.Kick.HoldX, w.Ball.X, 1e-3f, "kicked off the holder's spot");
            Assert.AreEqual(w.Kick.HoldZ, w.Ball.Z, 1e-3f);
            Assert.Less(Math.Abs(w.Kick.HoldX - kicker.X), c.kickContactYd + 0.1f, "the kicker at the ball");
            Assert.GreaterOrEqual(w.PlayTicks, Huddle.Ticks(w, c.holdReadyS), "the holder had it down first");
            Assert.IsTrue(Run(w, 20, () => w.Phase == Phase.Whistle));
            Assert.AreEqual("FIELD GOAL", w.Result);
        }

        [Test]
        public void NoFgHolderLetsTheKickerTakeTheSnapAsBefore()
        {
            var cfg = On();
            cfg.features["fgHolder"] = false;
            var w = FieldGoal(cfg, out var kicker);
            Assert.IsNull(Kicking.Holder(w));
            Assert.AreSame(kicker, Kicking.SnapTaker(w));
            Assert.AreEqual(kicker.Slot, w.Ball.Carrier);
            Assert.AreEqual(w.LineOfScrimmageX - w.Dir * cfg.kicking.fieldGoalBackYd, kicker.X, 1e-3f);
            Assert.IsTrue(Kicking.Kick(w, 500, 0.85f, 0f));
            Assert.IsTrue(w.Kick.Kicked, "no run-up: in the air at once");
        }

        [Test]
        public void BotsKickFieldGoalsFromTheHoldToo()
        {
            var cfg = On();
            var w = At(cfg, 25f, cfg.downs.downsToGain, 35f);
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(KickKind.FieldGoal, Kicking.Mode(w));
            Assert.IsNotNull(Kicking.Holder(w));
            Assert.IsTrue(Run(w, 10, () => w.Kick.Kicked));
            Assert.AreEqual(w.Kick.HoldX, w.Ball.X, 1e-3f);
            Assert.IsTrue(Run(w, 20, () => w.Phase == Phase.Whistle));
            Assert.AreEqual("FIELD GOAL", w.Result, "in a calm the bot makes a 42-yarder");
        }

        [Test]
        public void TheSweepsAreStraightLinesThereAndBack()
        {
            var c = On().kicking;
            Assert.Greater(c.aimSweepS, 0f);
            Assert.Greater(c.powerSweepS, 0f);
            Assert.AreEqual(0f, Kicking.AimSweep(c, 0f), 1e-4f, "the aim starts in the middle");
            Assert.AreEqual(1f, Kicking.AimSweep(c, c.aimSweepS * 0.25f), 1e-4f, "then all the way right");
            Assert.AreEqual(0f, Kicking.AimSweep(c, c.aimSweepS * 0.5f), 1e-4f);
            Assert.AreEqual(-1f, Kicking.AimSweep(c, c.aimSweepS * 0.75f), 1e-4f, "then left");
            Assert.AreEqual(0.5f, Kicking.AimSweep(c, c.aimSweepS * 0.125f), 1e-4f, "at an even speed");
            Assert.AreEqual(0f, Kicking.PowerSweep(c, 0f), 1e-4f, "the power starts at nothing");
            Assert.AreEqual(1f, Kicking.PowerSweep(c, c.powerSweepS * 0.5f), 1e-4f, "full halfway");
            Assert.AreEqual(0.5f, Kicking.PowerSweep(c, c.powerSweepS * 0.75f), 1e-4f);
            Assert.AreEqual(Kicking.PowerSweep(c, 0.3f), Kicking.PowerSweep(c, 0.3f + c.powerSweepS * 3f), 1e-4f, "and round again");
            for (float t = 0f; t < 10f; t += 0.07f)
            {
                Assert.That(Kicking.AimSweep(c, t), Is.InRange(-1f, 1f));
                Assert.That(Kicking.PowerSweep(c, t), Is.InRange(0f, 1f));
            }
            // the phone does the same sum
            string js = System.IO.File.ReadAllText("Assets/StreamingAssets/phone/kick.js");
            StringAssert.Contains("function triangle(t, period)", js);
            StringAssert.Contains("'kickaim'", js, "the first tap sends the aim");
        }

        [Test]
        public void TwoTapsSetTheAimThenThePower()
        {
            var cfg = On();
            Assert.IsTrue(cfg.Feature("kickSweep"), "FEATURES.kickSweep ships on");
            Assert.AreEqual("NO_KICK_SWEEP", ConfigLoader.EnvName("kickSweep"));
            var w = FieldGoal(cfg, out var kicker);
            var c = cfg.kicking;
            Assert.AreEqual(Kicking.SweepStage.Aim, Kicking.Stage(w), "lined up: the aim sweeps");
            Assert.IsFalse(Kicking.SetAim(w, 501, 0f), "only the kicker taps");
            for (int i = 0; i < 7; i++) w.Step(null);
            float aim = Kicking.SweepNow(w);
            Assert.AreEqual(Kicking.AimSweep(c, (w.Tick - w.Kick.AimTick) / (float)cfg.sim.tickHz), aim, 1e-5f);
            Assert.IsTrue(Kicking.Tap(w, 500), "the first tap");
            Assert.AreEqual(aim, w.Kick.Aim, 1e-5f, "the aim is where the arrow was");
            Assert.AreEqual(Phase.Live, w.Phase, "and the ball is snapped");
            Assert.AreEqual(Kicking.SweepStage.Power, Kicking.Stage(w));
            Assert.IsFalse(Kicking.SetAim(w, 500, 0.9f), "the aim is set once");
            for (int i = 0; i < 11; i++) w.Step(null);
            float power = Kicking.SweepNow(w);
            Assert.AreEqual(Kicking.PowerSweep(c, (w.Tick - w.Kick.PowerTick) / (float)cfg.sim.tickHz), power, 1e-5f);
            Assert.IsTrue(Kicking.Tap(w, 500), "the second tap kicks");
            Assert.AreEqual(Kicking.SweepStage.None, Kicking.Stage(w));
            Assert.AreEqual(power, w.Kick.PendingPower, 1e-5f);
            Assert.AreEqual(aim, w.Kick.PendingAim, 1e-5f);
            Assert.IsTrue(Run(w, 3, () => w.Kick.Kicked));
            Assert.AreEqual(power, w.Kick.Power, 1e-5f);
            // the arc climbs with the power
            Assert.AreEqual(c.fgMinAngleDeg, Kicking.Climb(w, KickKind.FieldGoal, 0f), 1e-4f);
            Assert.AreEqual(c.fgMaxAngleDeg, Kicking.Climb(w, KickKind.FieldGoal, 1f), 1e-4f);
            Assert.Greater(Kicking.Climb(w, KickKind.FieldGoal, 0.8f), Kicking.Climb(w, KickKind.FieldGoal, 0.4f));
        }

        [Test]
        public void APhoneTapTakesTheArrowWhereThePhoneShowedIt()
        {
            var cfg = On();
            Assert.IsTrue(cfg.Feature("kickSync"), "FEATURES.kickSync ships on");
            Assert.AreEqual("NO_KICK_SYNC", ConfigLoader.EnvName("kickSync"));
            var w = FieldGoal(cfg, out var kicker);
            var c = cfg.kicking;
            Assert.Greater(c.phoneSyncS, 0f);
            Assert.Greater(c.tapSlackS, 0f);
            for (int i = 0; i < 30; i++) w.Step(null);
            // the game's own clock for the sweep (the phone runs its arrow from it)
            float now = Kicking.SweepSeconds(w);
            Assert.AreEqual((w.Tick - w.Kick.AimTick) / (float)cfg.sim.tickHz, now, 1e-5f);
            Assert.AreEqual(Kicking.AimSweep(c, now), Kicking.SweepNow(w), 1e-5f);
            // the phone showed its arrow a tenth of a second ago (the trip over the wifi): that aim is the aim
            float st = now - 0.1f;
            Assert.IsTrue(Kicking.TapAt(w, kicker.Slot, st), "the first tap");
            Assert.AreEqual(Kicking.AimSweep(c, st), w.Kick.Aim, 1e-5f, "the aim the phone showed");
            Assert.AreEqual(Kicking.SweepStage.Power, Kicking.Stage(w));
            for (int i = 0; i < 9; i++) w.Step(null);
            now = Kicking.SweepSeconds(w);
            Assert.AreEqual(9f / cfg.sim.tickHz, now, 1e-5f, "the power's clock starts at the first tap");
            // a phone claiming a second ahead of the game's clock gets the game's arrow now, never a later one
            Assert.IsTrue(Kicking.TapAt(w, kicker.Slot, now + 5f), "the second tap kicks");
            Assert.AreEqual(Kicking.PowerSweep(c, now), w.Kick.PendingPower, 1e-5f);
        }

        [Test]
        public void APhoneTapFarBehindTheGameIsHeldToTheSlack()
        {
            var cfg = On();
            var w = FieldGoal(cfg, out var kicker);
            var c = cfg.kicking;
            for (int i = 0; i < 40; i++) w.Step(null);
            float now = Kicking.SweepSeconds(w);
            Assert.Greater(now, c.tapSlackS + 0.2f);
            Assert.IsTrue(Kicking.TapAt(w, kicker.Slot, 0f));
            Assert.AreEqual(Kicking.AimSweep(c, now - c.tapSlackS), w.Kick.Aim, 1e-5f, "at most tapSlackS behind the game");
            Assert.IsFalse(Kicking.TapAt(w, 501, 0f), "only the kicker taps");
        }

        [Test]
        public void TooSlowToAimAndTheSnapComesAnyway()
        {
            var cfg = On();
            var w = FieldGoal(cfg, out var kicker);
            Assert.IsTrue(Run(w, cfg.play.humanHikeMaxS + 1f, () => w.Phase == Phase.Live), "the line-up runs out");
            w.Step(null);
            Assert.IsTrue(w.Kick.AimSet, "the aim stops where the arrow was");
            Assert.AreEqual(Kicking.SweepStage.Power, Kicking.Stage(w), "now the power sweeps");
            Assert.IsTrue(Run(w, cfg.kicking.humanKickMaxS + 2f, () => w.Kick.Kicked), "and the bot kicks if he never taps");
        }

        [Test]
        public void TheTopOfThePowerSweepHooks()
        {
            var cfg = On();
            var c = cfg.kicking;
            Assert.Less(c.shankFromPower, 1f);
            Assert.Greater(c.shankAimDeg, 0f);
            float Across(float power, int seed)
            {
                var w = FieldGoal(cfg, out _, seed);
                Assert.IsTrue(Kicking.SetAim(w, 500, 0f));
                Assert.IsTrue(Kicking.Kick(w, 500, power, 0f));
                Assert.IsTrue(Run(w, 3, () => w.Kick.Kicked));
                return w.Ball.VZ;
            }
            Assert.AreEqual(0f, Across(c.shankFromPower - 0.05f, 1), 1e-4f, "just under the red: dead straight");
            bool hooked = false;
            for (int seed = 1; seed <= 3; seed++) hooked |= Math.Abs(Across(1f, seed)) > 1e-3f;
            Assert.IsTrue(hooked, "in the red it goes off line");
            Assert.That(Kicking.NeedPower(FieldGoal(cfg, out _)), Is.InRange(0.05f, c.shankFromPower), "a 42-yarder can be made out of the red");
        }

        [Test]
        public void TheWindIsSteadyForASpellAndNeverTooStrong()
        {
            var cfg = On(true);
            Assert.IsTrue(cfg.Feature("wind"), "FEATURES.wind ships on");
            Assert.AreEqual("NO_WIND", ConfigLoader.EnvName("wind"));
            var c = cfg.kicking;
            var w = WorldTests.Full(cfg, 7);
            var seen = new HashSet<string>();
            for (int n = 0; n < c.windEveryHuddles * 8; n++)
            {
                w.HuddleNumber = n;
                Kicking.Wind(w, out float x, out float z);
                Assert.LessOrEqual(Math.Sqrt(x * x + z * z), c.windMaxYdS + 1e-3f);
                w.HuddleNumber = n - n % c.windEveryHuddles;
                Kicking.Wind(w, out float x0, out float z0);
                Assert.AreEqual(x0, x, 1e-6f, "the same all spell");
                Assert.AreEqual(z0, z, 1e-6f);
                seen.Add(x.ToString("F2") + "," + z.ToString("F2"));
            }
            Assert.Greater(seen.Count, 3, "it changes from spell to spell");
            var again = WorldTests.Full(cfg, 7);
            again.HuddleNumber = 13; w.HuddleNumber = 13;
            Kicking.Wind(w, out float ax, out float az); Kicking.Wind(again, out float bx, out float bz);
            Assert.AreEqual(ax, bx); Assert.AreEqual(az, bz);
            Assert.AreEqual(w.Rng.Value(), again.Rng.Value(), "drawn from the seed, not the sim's random numbers");
            var calm = WorldTests.Full(On(false), 7);
            calm.HuddleNumber = 13;
            Kicking.Wind(calm, out float cx, out float cz);
            Assert.AreEqual(0f, cx); Assert.AreEqual(0f, cz);
        }

        [Test]
        public void TheWindPushesAFieldGoal()
        {
            // a seed whose wind at this field goal blows hard across the field
            for (int seed = 1; seed < 200; seed++)
            {
                var windy = FieldGoal(On(true), out _, seed);
                Kicking.Wind(windy, out float wx, out float wz);
                if (Math.Abs(wz) < 3f) continue;
                var calm = FieldGoal(On(false), out _, seed);
                float Z(World w)
                {
                    Assert.IsTrue(Kicking.Kick(w, 500, 0.85f, 0f));
                    Assert.IsTrue(Run(w, 10, () => w.Ball.X * w.Dir >= w.Cfg.field.HalfLength || w.Phase == Phase.Whistle));
                    return w.Ball.Z;
                }
                float drift = Z(windy) - Z(calm);
                Assert.Greater(drift * Math.Sign(wz), 1f, "blown the way the wind blows (seed " + seed + ")");
                // the ball on the ground and everything else is left alone
                var b = new World(On(true), seed);
                b.Ball.Launch(10f, 0f, true, b.Cfg.ball);
                Kicking.Drift(b, out float dx, out float dz);
                Assert.AreEqual(0f, dx); Assert.AreEqual(0f, dz);
                return;
            }
            Assert.Fail("no windy seed found");
        }
    

        // ---- the arc (FEATURES.kickArc): steer it, HIKE, tap the power, tap the pitch ----

        static float Share(KickingConfig c, float deg) => (deg - c.arcMinPitchDeg) / (c.arcMaxPitchDeg - c.arcMinPitchDeg);

        static float ClimbDeg(Ball b) => (float)(Math.Atan2(b.VY, Math.Sqrt(b.VX * b.VX + b.VZ * b.VZ)) * 180.0 / Math.PI);

        [Test]
        public void TheArcIsSteeredThenSnappedThenThePowerThenThePitch()
        {
            var cfg = On(arc: true);
            Assert.IsTrue(ConfigTests.Load().Feature("kickArc"), "FEATURES.kickArc ships on");
            Assert.AreEqual("NO_KICK_ARC", ConfigLoader.EnvName("kickArc"));
            var c = cfg.kicking;
            Assert.Greater(c.pitchSweepS, 0f);
            Assert.That(c.arcMinPitchDeg, Is.InRange(0f, c.arcMaxPitchDeg - 1f));
            Assert.LessOrEqual(c.arcMaxPitchDeg, 90f);
            var w = FieldGoal(cfg, out var kicker);
            Assert.IsTrue(Kicking.ArcOn(w));
            Assert.IsFalse(Kicking.SweepOn(w), "the arc takes over from the sweep");
            Assert.AreEqual(Kicking.SweepStage.Aim, Kicking.Stage(w));
            Assert.AreEqual(0f, w.Kick.Aim, 1e-6f, "the arc starts straight");

            // lined up: he steers it (nobody else can), and it stays where he left it
            Assert.IsFalse(Kicking.Steer(w, 501, 0.5f), "only the kicker steers");
            Assert.IsTrue(Kicking.Steer(w, kicker.Slot, 0.4f));
            Assert.IsTrue(Kicking.Steer(w, kicker.Slot, 3f));
            Assert.AreEqual(1f, w.Kick.Aim, 1e-6f, "no further than all the way");
            Assert.IsTrue(Kicking.Steer(w, kicker.Slot, 0.4f));
            for (int i = 0; i < 20; i++) w.Step(null);
            Assert.AreEqual(Phase.Set, w.Phase, "the arc doesn't snap it by itself");
            Assert.AreEqual(0.4f, Kicking.SweepNow(w), 1e-6f, "no sweep: the aim is his");

            // HIKE (the pad's A, the phone's HIKE): the snap, with that aim
            Assert.IsTrue(Kicking.Tap(w, kicker.Slot));
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.AreEqual(0.4f, w.Kick.Aim, 1e-6f);
            Assert.AreEqual(Kicking.SweepStage.Power, Kicking.Stage(w));
            Assert.IsFalse(Kicking.Steer(w, kicker.Slot, -1f), "no steering after the snap");

            // the POWER meter runs up and down: the first tap stops it
            for (int i = 0; i < 11; i++) w.Step(null);
            float power = Kicking.SweepNow(w);
            Assert.AreEqual(Kicking.PowerSweep(c, 11f / cfg.sim.tickHz), power, 1e-5f);
            Assert.IsFalse(Kicking.SetPower(w, 501, 0.5f), "only the kicker taps");
            Assert.IsTrue(Kicking.Tap(w, kicker.Slot));
            Assert.AreEqual(Kicking.SweepStage.Pitch, Kicking.Stage(w), "then the pitch");
            Assert.AreEqual(power, w.Kick.ChosenPower, 1e-6f);
            Assert.IsFalse(w.Kick.Approach, "no kick yet");

            // the PITCH ANGLE needle runs up and down: the second tap stops it and kicks
            for (int i = 0; i < 7; i++) w.Step(null);
            float share = Kicking.SweepNow(w);
            Assert.AreEqual(Kicking.PitchSweep(c, 7f / cfg.sim.tickHz), share, 1e-5f);
            Kicking.ArcNow(w, out float shownPower, out float shownPitch);
            Assert.AreEqual(power, shownPower, 1e-6f, "the TV's arc shows his power");
            Assert.AreEqual(Kicking.PitchDeg(c, share), shownPitch, 1e-4f, "and the pitch running");
            Assert.IsTrue(Kicking.Tap(w, kicker.Slot));
            Assert.AreEqual(Kicking.SweepStage.None, Kicking.Stage(w));
            Assert.IsTrue(w.Kick.Approach, "the run-up to the holder");
            Assert.IsTrue(Run(w, 3, () => w.Kick.Kicked));
            Assert.AreEqual(power, w.Kick.Power, 1e-5f);
            Assert.AreEqual(Kicking.PitchDeg(c, share), ClimbDeg(w.Ball), 0.05f, "it climbs at his pitch");
            Assert.Less(w.Ball.VZ * w.Dir, 0f, "aimed right: -z going +x");
        }

        [Test]
        public void TheSweepsOfTheArcAreStraightLinesToo()
        {
            var c = On(arc: true).kicking;
            Assert.AreEqual(0f, Kicking.PitchSweep(c, 0f), 1e-4f, "the needle starts at the bottom");
            Assert.AreEqual(1f, Kicking.PitchSweep(c, c.pitchSweepS * 0.5f), 1e-4f, "the top halfway");
            Assert.AreEqual(0.5f, Kicking.PitchSweep(c, c.pitchSweepS * 0.75f), 1e-4f, "at an even speed");
            Assert.AreEqual(c.arcMinPitchDeg, Kicking.PitchDeg(c, 0f), 1e-4f);
            Assert.AreEqual(c.arcMaxPitchDeg, Kicking.PitchDeg(c, 1f), 1e-4f);
            Assert.AreEqual(c.arcMaxPitchDeg, Kicking.PitchDeg(c, 7f), 1e-4f, "never past the end");
            string js = System.IO.File.ReadAllText("Assets/StreamingAssets/phone/kick.js");
            StringAssert.Contains("'kicksteer'", js, "the phone steers");
            StringAssert.Contains("'kickpower'", js, "the first tap sends the power");
            StringAssert.Contains("pitch: share", js, "the second the pitch");
        }

        // A field goal at a fair power: a climb in the shaded window scores, one too flat to reach the bar doesn't.
        [TestCase(true, "FIELD GOAL")]
        [TestCase(false, "NO GOOD")]
        public void AnArcFieldGoalNeedsAPitchThatClearsTheBar(bool inWindow, string result)
        {
            var cfg = On(arc: true);
            var c = cfg.kicking;
            var w = FieldGoal(cfg, out var kicker);
            float power = Math.Min(c.shankFromPower - 0.02f, Kicking.NeedPower(w) + 0.08f);
            Assert.That(Kicking.NeedPower(w), Is.InRange(0.05f, c.shankFromPower - 0.05f), "a 42-yarder can be made out of the red");
            Assert.IsTrue(Kicking.ClearPitch(w, power, out float lo, out float hi), "some climbs clear the bar");
            Assert.That(c.fieldGoalAngleDeg, Is.InRange(lo, hi), "the usual climb among them");
            Assert.Greater(lo, c.arcMinPitchDeg, "the flattest climb doesn't");
            Assert.IsFalse(Kicking.ClearPitch(w, 0f, out _, out _), "a dribble clears nothing");
            float pitch = inWindow ? (lo + hi) * 0.5f : c.arcMinPitchDeg;
            Assert.IsTrue(Kicking.SetAim(w, kicker.Slot, 0f), "HIKE");
            Assert.IsTrue(Kicking.SetPower(w, kicker.Slot, power));
            Assert.IsTrue(Kicking.KickPitched(w, kicker.Slot, Share(c, pitch)));
            Assert.IsFalse(Kicking.KickPitched(w, kicker.Slot, 0.5f), "one kick a play");
            Assert.IsTrue(Run(w, 20, () => w.Phase == Phase.Whistle));
            Assert.AreEqual(result, w.Result);
        }

        [Test]
        public void APuntIsSteeredAndPitchedToo()
        {
            var cfg = On(arc: true);
            var c = cfg.kicking;
            var w = At(cfg, -20f, cfg.downs.downsToGain, -5f);
            var punter = w.ReplaceBot(w.Offense, 500);
            Assert.IsTrue(Huddle.Lock(w, 500, c.puntPlay));
            Assert.IsTrue(Run(w, 30, () => w.Phase == Phase.Set));
            Assert.AreEqual(KickKind.Punt, Kicking.Mode(w));
            Assert.IsTrue(Kicking.ArcOn(w));
            Assert.AreEqual(Kicking.SweepStage.Aim, Kicking.Stage(w));
            Assert.AreEqual("kick", PhoneLayouts.For(w, punter));
            Assert.That(c.puntGoodMinDeg, Is.InRange(c.arcMinPitchDeg, c.puntGoodMaxDeg));
            Assert.That(c.puntGoodMaxDeg, Is.InRange(c.puntGoodMinDeg, c.arcMaxPitchDeg));
            Assert.IsFalse(Kicking.ClearPitch(w, 1f, out _, out _), "no bar to clear on a punt");
            Assert.IsTrue(Kicking.Steer(w, punter.Slot, -1f));
            Assert.IsTrue(Kicking.SetAim(w, punter.Slot, w.Kick.Aim), "HIKE");
            Assert.AreEqual(punter.Slot, w.Ball.Carrier, "the snap is his");
            Assert.IsTrue(Kicking.SetPower(w, punter.Slot, 0.8f));
            Assert.IsFalse(w.Kick.Kicked);
            Assert.IsTrue(Kicking.KickPitched(w, punter.Slot, Share(c, 60f)));
            Assert.IsTrue(w.Kick.Kicked, "no holder: away at once");
            Assert.AreEqual(60f, ClimbDeg(w.Ball), 0.05f, "a high punt");
            Assert.Greater(w.Ball.VZ * w.Dir, 0f, "steered to his left");
            int kicking = w.Offense;
            Assert.IsTrue(Run(w, 40, () => w.Phase == Phase.Whistle));
            Assert.IsTrue(Run(w, 10, () => w.Phase != Phase.Whistle));
            Assert.AreNotEqual(kicking, w.Offense, "the other team has it");
        }

        [Test]
        public void TooSlowWithTheArcAndTheBotKicksIt()
        {
            var cfg = On(arc: true);
            var w = FieldGoal(cfg, out var kicker);
            Assert.IsTrue(Kicking.Steer(w, kicker.Slot, -0.3f));
            Assert.IsTrue(Run(w, cfg.play.humanHikeMaxS + 1f, () => w.Phase == Phase.Live), "the line-up runs out");
            w.Step(null);
            Assert.IsTrue(w.Kick.AimSet);
            Assert.AreEqual(-0.3f, w.Kick.Aim, 1e-6f, "the aim stays where he steered it");
            Assert.AreEqual(Kicking.SweepStage.Power, Kicking.Stage(w), "the power runs");
            Assert.IsTrue(Run(w, cfg.kicking.humanKickMaxS + 2f, () => w.Kick.Kicked), "and the bot kicks if he never taps");
        }

        [Test]
        public void NoKickArcKeepsTheSweepAndTheSwipe()
        {
            var cfg = On(arc: false);
            var w = FieldGoal(cfg, out var kicker);
            Assert.IsFalse(Kicking.ArcOn(w));
            Assert.IsTrue(Kicking.SweepOn(w), "the field goal's sweep as before");
            Assert.IsFalse(Kicking.Steer(w, kicker.Slot, 0.5f));
            var p = At(cfg, -20f, cfg.downs.downsToGain, -5f);
            p.ReplaceBot(p.Offense, 500);
            Assert.IsTrue(Huddle.Lock(p, 500, cfg.kicking.puntPlay));
            Assert.IsTrue(Run(p, 30, () => p.Phase == Phase.Set));
            Assert.IsFalse(Kicking.TapsOn(p), "a punt is a swipe again");
            Assert.AreEqual(Kicking.SweepStage.None, Kicking.Stage(p));
        }
    }
}
