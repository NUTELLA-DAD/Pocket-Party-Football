using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-04: the carrier's SPIN, STIFF ARM and DIVE (FEATURES.carriermoves) and fumbles (FEATURES.fumbles).
    public sealed class CarrierMovesTests
    {
        const int Me = 500;

        // A world lined up and Set, both plays called, a person at QB, the switches and numbers as asked.
        static World AtTheLine(bool moves, bool fumbles, System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            cfg.features["carriermoves"] = moves;
            cfg.features["fumbles"] = fumbles;
            tune?.Invoke(cfg);
            var w = WorldTests.Full(cfg);
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Me);                   // a person at QB
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Me, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            return w;
        }

        static void Run(World w, int ticks, Dictionary<int, PlayerInput> input = null) { for (int i = 0; i < ticks; i++) w.Step(input); }
        static Dictionary<int, PlayerInput> Press(PlayerInput i) => new Dictionary<int, PlayerInput> { { Me, i } };

        // Everyone but the QB and the players named goes far away down the field (out of every reach).
        static void ClearAllBut(World w, Player qb, params Player[] keep)
        {
            float far = w.Cfg.field.GoalLineX * w.Dir;
            foreach (var p in w.Players)
            {
                if (p == qb || System.Array.IndexOf(keep, p) >= 0) continue;
                p.X = far; p.Z = p.Team == w.Offense ? w.Cfg.field.HalfWidth : -w.Cfg.field.HalfWidth;
                p.HomeX = p.X; p.HomeZ = p.Z; p.VX = p.VZ = 0f;
            }
        }

        static Player FirstBot(World w, int team, Player not = null)
        {
            foreach (var p in w.Players) if (p.Team == team && !p.Human && p != not) return p;
            return null;
        }

        // Hiked, the QB holding it with one defender next to nobody yet.
        static (World w, Player qb, Player d) Hiked(bool moves, bool fumbles, System.Action<GameConfig> tune = null)
        {
            var w = AtTheLine(moves, fumbles, tune);
            w.Step(Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
            var qb = w.Find(Me);
            var d = FirstBot(w, w.Defense);
            ClearAllBut(w, qb, d);
            d.X = qb.X + w.Dir * 3f; d.Z = qb.Z; d.VX = d.VZ = 0f;
            return (w, qb, d);
        }

        static void SureTackles(GameConfig c) { c.play.tackleBase = 1f; c.play.tackleSpeedBonus = 0f; c.play.tackleFromBehindBonus = 0f; }

        // ---- the switches ----

        [Test]
        public void BothSwitchesShipOff()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("carriermoves"), "FEATURES.carriermoves is in game.json");
            Assert.IsTrue(cfg.features.ContainsKey("fumbles"), "FEATURES.fumbles is in game.json");
            Assert.IsTrue(cfg.Feature("carriermoves"), "on in game.json (the merge captain turned it on)");
            Assert.IsTrue(cfg.Feature("fumbles"), "on in game.json (the merge captain turned it on and re-pinned)");
            Assert.AreEqual("NO_CARRIERMOVES", ConfigLoader.EnvName("carriermoves"));
            Assert.AreEqual("NO_FUMBLES", ConfigLoader.EnvName("fumbles"));
        }

        [Test]
        public void TheEnvOverridesTurnThemOff()
        {
            var json = System.IO.File.ReadAllText("Assets/_Project/Config/game.json").Replace("\"carriermoves\": false", "\"carriermoves\": true").Replace("\"fumbles\": false", "\"fumbles\": true");
            var env = new Dictionary<string, string> { { "NO_CARRIERMOVES", "1" }, { "NO_FUMBLES", "1" } };
            var on = ConfigLoader.Parse(json);
            Assert.IsTrue(on.Feature("carriermoves") && on.Feature("fumbles"));
            var off = ConfigLoader.Parse(json, k => env.TryGetValue(k, out var v) ? v : null);
            Assert.IsFalse(off.Feature("carriermoves"));
            Assert.IsFalse(off.Feature("fumbles"));
        }

        [Test]
        public void TheCarrierBlockLoads()
        {
            var c = ConfigTests.Load().carrier;
            Assert.Greater(c.spinTicks, 0);
            Assert.Greater(c.stiffArmTicks, 0);
            Assert.Greater(c.diveTicks, 0);
            Assert.That(c.spinTackleMul, Is.InRange(0f, 1f));
            Assert.That(c.stiffArmTackleMul, Is.InRange(0f, 1f));
            Assert.That(c.diveTackleMul, Is.InRange(0f, 1f));
            Assert.That(c.fumbleChance, Is.InRange(0f, 0.2f), "a small chance");
            Assert.Greater(c.bigHitSpeedYdS, 0f);
            Assert.Greater(c.scrambleMaxS, 0f);
        }

        // ---- the moves change the tackle odds as configured ----

        [Test]
        public void SpinMultipliesTheTackleOdds()
        {
            var (w, qb, d) = Hiked(true, false);
            Assert.AreEqual(0.8f, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f, "no move: the odds as they were");
            w.Step(Press(new PlayerInput { Move = CarrierMove.Spin }));
            Assert.AreEqual(CarrierMove.Spin, w.Carry.Move);
            Assert.AreEqual(Me, w.Carry.Slot);
            Assert.AreEqual(0.8f * w.Cfg.carrier.spinTackleMul, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f);
        }

        [Test]
        public void StiffArmOnlyHelpsAgainstAManInFront()
        {
            var (w, qb, d) = Hiked(true, false);
            w.Step(Press(new PlayerInput { Move = CarrierMove.StiffArm, MoveX = w.Dir }));
            Assert.AreEqual(CarrierMove.StiffArm, w.Carry.Move);
            qb.VX = w.Dir * 5f; qb.VZ = 0f;                 // running at the goal line
            d.X = qb.X + w.Dir * 1f; d.Z = qb.Z;            // in front
            Assert.AreEqual(0.8f * w.Cfg.carrier.stiffArmTackleMul, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f, "in front: the arm keeps him off");
            d.X = qb.X - w.Dir * 1f;                         // behind
            Assert.AreEqual(0.8f, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f, "from behind: no help");
        }

        [Test]
        public void DiveMultipliesTheTackleOddsNearTheGoalLine()
        {
            var (w, qb, d) = Hiked(true, false);
            w.Step(Press(new PlayerInput { Move = CarrierMove.Dive }));
            Assert.AreEqual(CarrierMove.None, w.Carry.Move, "no dive from far out");
            qb.X = w.Dir * (w.Cfg.field.GoalLineX - 2f); qb.VX = qb.VZ = 0f;
            d.X = qb.X - w.Dir * 3f;
            w.Step(Press(new PlayerInput { Move = CarrierMove.Dive }));
            Assert.AreEqual(CarrierMove.Dive, w.Carry.Move);
            Assert.AreEqual(0.8f * w.Cfg.carrier.diveTackleMul, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f);
        }

        [Test]
        public void ASpinMakesASureTackleMissWhenConfiguredTo()
        {
            var (w, qb, d) = Hiked(true, false, c => { SureTackles(c); c.carrier.spinTackleMul = 0f; });
            w.Step(Press(new PlayerInput { Move = CarrierMove.Spin }));
            Assert.AreEqual(CarrierMove.Spin, w.Carry.Move);
            d.X = qb.X + w.Dir * 0.8f; d.Z = qb.Z; d.TackleCooldown = 0;
            w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "spun out of it");
            Assert.Greater(d.StumbleTicks, 0, "the tackler stumbles");
        }

        [Test]
        public void ASpinThatDoesNotHelpStillGetsTackled()
        {
            var (w, qb, d) = Hiked(true, false, c => { SureTackles(c); c.carrier.spinTackleMul = 1f; });
            w.Step(Press(new PlayerInput { Move = CarrierMove.Spin }));
            d.X = qb.X + w.Dir * 0.8f; d.Z = qb.Z; d.TackleCooldown = 0;
            w.Step(null);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.That(w.Result, Is.EqualTo("TACKLED").Or.EqualTo("SACKED"));
        }

        [Test]
        public void MovesDoNothingWithTheSwitchOff()
        {
            var (w, qb, d) = Hiked(false, false);
            w.Step(Press(new PlayerInput { Move = CarrierMove.Spin }));
            Assert.AreEqual(CarrierMove.None, w.Carry.Move);
            w.Carry.Move = CarrierMove.Spin; w.Carry.Slot = qb.Slot;   // even if it were set somehow
            Assert.AreEqual(0.8f, CarrierMoves.TackleOdds(w, d, qb, 0.8f), 1e-6f);
        }

        [Test]
        public void OneMoveAtATimeThenACooldown()
        {
            var (w, qb, d) = Hiked(true, false, c => { c.play.tackleBase = 0f; c.play.tackleSpeedBonus = 0f; c.play.tackleFromBehindBonus = 0f; });
            w.Step(Press(new PlayerInput { Move = CarrierMove.Spin }));
            w.Step(Press(new PlayerInput { Move = CarrierMove.StiffArm }));
            Assert.AreEqual(CarrierMove.Spin, w.Carry.Move, "the spin is still on");
            Run(w, w.Cfg.carrier.spinTicks + 1);
            Assert.AreEqual(CarrierMove.None, w.Carry.Move, "the spin is over");
            w.Step(Press(new PlayerInput { Move = CarrierMove.StiffArm }));
            Assert.AreEqual(CarrierMove.None, w.Carry.Move, "cooling down");
            Run(w, w.Cfg.carrier.cooldownTicks + 1);
            if (w.Phase != Phase.Live) Assert.Inconclusive("the play ended before the cooldown did");
            w.Step(Press(new PlayerInput { Move = CarrierMove.StiffArm }));
            Assert.AreEqual(CarrierMove.StiffArm, w.Carry.Move);
        }

        [Test]
        public void ADiveShortOfTheGoalLineIsDownWhereHeLands()
        {
            var (w, qb, d) = Hiked(true, false, c => c.play.tackleBase = 0f);
            float goal = w.Cfg.field.GoalLineX;
            float start = w.Dir * (goal - 4.5f);
            qb.X = start; qb.Z = 0f; qb.VX = qb.VZ = 0f;
            w.Step(Press(new PlayerInput { Move = CarrierMove.Dive }));
            Assert.AreEqual(CarrierMove.Dive, w.Carry.Move);
            Run(w, w.Cfg.carrier.diveTicks + 1);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("DOWN", w.Result);
            Assert.Greater((w.NextLos - start) * w.Dir, w.Cfg.carrier.diveLungeYd * 0.5f, "the lunge gained ground");
            Assert.Less(w.NextLos * w.Dir, goal, "short of the goal line");
        }

        [Test]
        public void ADiveOverTheGoalLineIsATouchdown()
        {
            var (w, qb, d) = Hiked(true, false, c => c.play.tackleBase = 0f);
            int team = w.Offense;
            qb.X = w.Dir * (w.Cfg.field.GoalLineX - 2f); qb.Z = 0f; qb.VX = qb.VZ = 0f;
            w.Step(Press(new PlayerInput { Move = CarrierMove.Dive }));
            for (int i = 0; i < w.Cfg.carrier.diveTicks + 2 && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual("TOUCHDOWN", w.Result);
            Assert.AreEqual(w.Cfg.downs.touchdownPoints, w.Score[team]);
        }

        [Test]
        public void ABotCarrierUsesHisMoves()
        {
            var cfg = ConfigTests.Load();
            cfg.features["carriermoves"] = true;
            var w = WorldTests.Full(cfg);
            Run(w, cfg.sim.tickHz * 180);
            int made = 0;
            foreach (int n in w.Carry.Started) made += n;
            Assert.Greater(made, 0, "bots spin, stiff-arm or dive in three minutes of football");
        }

        // ---- fumbles ----

        // A sure fumble on the hit, picked up at once by whoever is on his feet and gets there first.
        static void SureFumbles(GameConfig c)
        {
            SureTackles(c);
            c.carrier.fumbleChance = 1f; c.carrier.bigHitSpeedYdS = 0f; c.carrier.fumblePopYdS = 0f;
            c.carrier.recoverChance = 1f; c.carrier.recoverDelayTicks = 0;
        }

        // The QB is hit by d with r (a bot of the team asked for) a few yards off; returns once the ball is on the grass.
        static (World w, Player qb, Player d, Player r) Fumbled(bool defenseRecovers)
        {
            var (w, qb, d) = Hiked(false, true, SureFumbles);
            var r = FirstBot(w, defenseRecovers ? w.Defense : w.Offense, d);
            r.X = qb.X; r.Z = qb.Z + 3f; r.VX = r.VZ = 0f;
            d.X = qb.X + w.Dir * 0.8f; d.Z = qb.Z; d.TackleCooldown = 0;
            w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "the hit knocked it loose: no whistle");
            Assert.AreEqual(BallState.Loose, w.Ball.State);
            Assert.IsTrue(Fumbles.Loose(w));
            Assert.AreEqual(1, w.Carry.Fumbles);
            Assert.AreEqual(Me, w.Carry.FumbledBy);
            return (w, qb, d, r);
        }

        static void ToWhistle(World w) { for (int i = 0; i < w.Cfg.sim.tickHz * 10 && w.Phase == Phase.Live; i++) w.Step(null); }

        [Test]
        public void AFumbleRecoveredByTheDefenseChangesPossessionAtTheSpot()
        {
            var (w, qb, d, r) = Fumbled(true);
            int had = w.Offense;
            Assert.AreNotEqual(had, r.Team, "a defender scrambles for it");
            ToWhistle(w);
            Assert.AreEqual("FUMBLE LOST", w.Result);
            Assert.AreEqual(r.Slot, w.Carry.RecoveredBy, "the scrambling defender fell on it");
            float spot = r.X;
            Assert.AreEqual(spot, w.NextLos, 1e-3f, "the next play starts where he recovered it");
            Run(w, (int)(w.Cfg.play.whistleS * w.Cfg.sim.tickHz) + 2);
            Assert.AreEqual(1 - had, w.Offense, "the other team has the ball now");
            Assert.AreEqual(spot, w.LineOfScrimmageX, 1e-3f, "at the spot of the recovery");
            Assert.AreEqual(1, w.Down, "first and ten");
        }

        [Test]
        public void AFumbleTheOffenseFallsOnStaysWithThem()
        {
            var (w, qb, d, r) = Fumbled(false);
            int had = w.Offense;
            ToWhistle(w);
            Assert.AreEqual("FUMBLE RECOVERED", w.Result);
            Run(w, (int)(w.Cfg.play.whistleS * w.Cfg.sim.tickHz) + 2);
            Assert.AreEqual(had, w.Offense, "still their ball");
        }

        [Test]
        public void ALooseBallIsNotAnIncompletion()
        {
            var (w, qb, d, r) = Fumbled(true);
            w.Step(null);
            Assert.AreNotEqual("INCOMPLETE", w.Result);
        }

        [Test]
        public void NoFumbleWithTheSwitchOff()
        {
            var (w, qb, d) = Hiked(false, false, SureFumbles);
            d.X = qb.X + w.Dir * 0.8f; d.Z = qb.Z; d.TackleCooldown = 0;
            w.Step(null);
            Assert.AreEqual(Phase.Whistle, w.Phase, "the tackle ends the play as before");
            Assert.That(w.Result, Is.EqualTo("TACKLED").Or.EqualTo("SACKED"));
            Assert.AreEqual(0, w.Carry.Fumbles);
        }

        [Test]
        public void NoFumbleInABotGameWithTheSwitchOffEvenAtFullChance()
        {
            var cfg = ConfigTests.Load();
            cfg.features["fumbles"] = false;
            cfg.carrier.fumbleChance = 1f; cfg.carrier.bigHitSpeedYdS = 0f;
            var w = WorldTests.Full(cfg);
            for (int i = 0; i < cfg.sim.tickHz * 180; i++)
            {
                w.Step(null);
                Assert.IsFalse(w.Carry.Loose);
            }
            Assert.AreEqual(0, w.Carry.Fumbles);
            foreach (var kv in w.Results) StringAssert.DoesNotContain("FUMBLE", kv.Key);
        }

        [Test]
        public void TheSwitchesOffLeaveTheGameExactlyAsItWas()
        {
            // the same seeded bot game, with the carrier numbers turned right up but both switches off, hashes the same
            var off = ConfigTests.Load();
            off.features["carriermoves"] = false; off.features["fumbles"] = false;
            var plain = WorldTests.Full(off, 2);
            var cfg = ConfigTests.Load();
            cfg.features["carriermoves"] = false; cfg.features["fumbles"] = false;
            cfg.carrier.fumbleChance = 1f; cfg.carrier.bigHitSpeedYdS = 0f; cfg.carrier.spinTackleMul = 0f; cfg.carrier.botMoveYd = 50f;
            var tuned = WorldTests.Full(cfg, 2);
            for (int i = 0; i < cfg.sim.tickHz * 120; i++)
            {
                plain.Step(null); tuned.Step(null);
                Assert.AreEqual(plain.Hash(), tuned.Hash(), "tick " + i);
            }
        }

        [Test]
        public void AFumbleFestStillPlaysOnAndReplaysTheSame()
        {
            World Game()
            {
                var cfg = ConfigTests.Load();
                cfg.features["carriermoves"] = true;
                cfg.features["fumbles"] = true;
                cfg.carrier.fumbleChance = 1f; cfg.carrier.bigHitSpeedYdS = 0f;
                var w = WorldTests.Full(cfg, 3);
                Run(w, cfg.sim.tickHz * 180);
                return w;
            }
            var a = Game();
            var b = Game();
            Assert.Greater(a.Carry.Fumbles, 0, "every hit is a fumble");
            Assert.Greater(a.PlaysRun, 2, "plays keep coming after fumbles");
            Assert.AreEqual(a.Hash(), b.Hash(), "same seed, same game");
            Assert.AreEqual(a.Carry.Fumbles, b.Carry.Fumbles);
            Assert.AreEqual(a.Carry.FumblesLost, b.Carry.FumblesLost);
        }
    }
}
