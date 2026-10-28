using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-03 defender moves (Sim/DefenseMoves.cs, FEATURES.defmoves): swim, bull rush, swat, jump, dive tackle.
    public sealed class DefenseMovesTests
    {
        const int Qb = 500, Def = 600;

        static GameConfig Cfg(System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            cfg.features["defmoves"] = true;
            cfg.defense.botCoverChance = 0f;   // only the person under test swats or dives
            cfg.defense.botDiveChance = 0f;
            tune?.Invoke(cfg);
            return cfg;
        }

        // Hiked: a person at QB, a person at the defense's first spot (the middle LB).
        static World Live(System.Action<GameConfig> tune = null)
        {
            var w = WorldTests.Full(Cfg(tune));
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Defense, Def);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            w.Step(In(Qb, new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
            return w;
        }

        static Dictionary<int, PlayerInput> In(int slot, PlayerInput i) => new Dictionary<int, PlayerInput> { { slot, i } };

        // the other defenders far away, out of the play
        static void Clear(World w, float z)
        {
            foreach (var p in w.Players)
                if (p.Team == w.Defense && p.Slot != Def) { p.X = w.LineOfScrimmageX + w.Dir * 40f; p.Z = z; p.HomeX = p.X; p.HomeZ = p.Z; p.VX = p.VZ = 0f; }
        }

        static void Pin(Player p, float x, float z) { p.X = x; p.Z = z; p.VX = p.VZ = 0f; }

        // ---- the switch ----

        [Test]
        public void OffMeansOffAndNoDefmovesTurnsItOff()
        {
            var cfg = ConfigTests.Load();
            cfg.features["defmoves"] = false;               // (whatever game.json ships)
            var w = WorldTests.Full(cfg);
            Assert.IsFalse(DefenseMoves.Press(w, 0, DefMove.Dive));
            cfg.features["defmoves"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_DEFMOVES" ? "1" : null);
            Assert.IsFalse(cfg.Feature("defmoves"));
        }

        // ---- swat and jump ----

        // A lob to a receiver standing at (tx, tz); the person on defense one yard short of him on the ball's line; everyone
        // else off the field.
        // `press` is called every tick the ball is in the air, before the step.
        static void Lob(World w, out Player r, out Player d, System.Action<World> press)
        {
            r = LivePlay.Receiver(w, 1);
            d = w.Find(Def);
            // just the three of them: no other receiver (the tight end's route crosses here) or defender gets to it first
            var others = new List<int>();
            foreach (var p in w.Players) if (p.Slot != Qb && p != r && p != d) others.Add(p.Slot);
            foreach (int slot in others) w.RemovePlayer(slot);
            float tx = w.LineOfScrimmageX + w.Dir * 12f, tz = -8f;
            Assert.IsTrue(w.Throw(tx, tz, true));
            w.Thrown = true;
            float ux = tx - w.Ball.X, uz = tz - w.Ball.Z, ul = (float)System.Math.Sqrt(ux * ux + uz * uz);
            ux /= ul; uz /= ul;
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying && w.Phase == Phase.Live; i++)
            {
                Pin(r, tx, tz);
                if (DefenseMoves.Get(w, Def).MissTicks == 0) Pin(d, tx - ux, tz - uz);
                press(w);
                w.Step(null);
            }
        }

        // Well timed: pressed as the ball comes down, a few ticks before it is in reach.
        static void PressAsItComesDown(World w, DefMove m, ref bool pressed)
        {
            if (pressed || w.Ball.VY >= 0f || w.Ball.Y > w.Cfg.defense.swatMaxHeightYd + 1.5f) return;
            Assert.IsTrue(DefenseMoves.Press(w, Def, m));
            pressed = true;
        }

        [Test]
        public void WithoutASwatTheReceiverCatchesIt()
        {
            var w = Live(c => { c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; });
            Lob(w, out var r, out _, _ => { });
            Assert.AreEqual(BallState.Held, w.Ball.State, "caught");
            Assert.AreEqual(r.Slot, w.Ball.Carrier);
        }

        [Test]
        public void AWellTimedSwatKnocksTheBallDown()
        {
            var w = Live(c => { c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; c.defense.swatPerfectChance = 1f; });
            bool pressed = false;
            Lob(w, out var r, out var d, x => PressAsItComesDown(x, DefMove.Swat, ref pressed));
            Assert.IsTrue(pressed);
            Assert.AreEqual(1, DefenseMoves.Tried(w, DefMove.Swat));
            Assert.AreEqual(1, DefenseMoves.Won(w, DefMove.Swat), "the swat got there");
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("INCOMPLETE", w.Result, "knocked to the grass, though the receiver was right there");
            Assert.AreNotEqual(r.Slot, w.Ball.Carrier);
        }

        [Test]
        public void ASwatPressedTooEarlyWhiffsAndHeIsOffBalance()
        {
            var w = Live(c => { c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; c.defense.swatPerfectChance = 1f; c.defense.swatChance = 1f; });
            bool pressed = false;
            int missAt = -1;
            Lob(w, out var r, out _, x =>
            {
                if (!pressed) { Assert.IsTrue(DefenseMoves.Press(x, Def, DefMove.Swat)); pressed = true; }   // right at the throw: far too early
                if (missAt < 0 && DefenseMoves.Get(x, Def).MissTicks > 0) missAt = x.Tick;
            });
            Assert.AreEqual(1, DefenseMoves.Tried(w, DefMove.Swat));
            Assert.AreEqual(0, DefenseMoves.Won(w, DefMove.Swat), "his arms were down again by the time it came");
            Assert.GreaterOrEqual(missAt, 0, "off balance after the whiff");
            Assert.AreEqual(r.Slot, w.Ball.Carrier, "so the receiver catches it");
        }

        [Test]
        public void AWellTimedJumpCanPickItOff()
        {
            var w = Live(c => { c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; c.defense.jumpPickPerfectChance = 1f; });
            bool pressed = false;
            Lob(w, out _, out var d, x => PressAsItComesDown(x, DefMove.Jump, ref pressed));
            Assert.AreEqual(1, DefenseMoves.Won(w, DefMove.Jump));
            Assert.AreEqual(d.Slot, w.Ball.Carrier, "the defender has it");
            Assert.IsTrue(w.Intercepted, "and runs it back (FEATURES.intreturn)");
        }

        // FEATURES.jumpAnytime: JUMP from the phone always jumps, pass or no pass: off the grass for the hop, then he
        // lands (off balance: there was nothing to go for). With the switch off a jump needs a pass in the air.
        [Test]
        public void TheJumpButtonJumpsWithNoPassInTheAir()
        {
            var w = Live();
            Assert.AreNotEqual(BallState.Flying, w.Ball.State);
            var d = w.Find(Def);
            w.Step(In(Def, new PlayerInput { Def = DefMove.Jump }));
            Assert.AreEqual(DefMove.Jump, DefenseMoves.Get(w, Def).Move, "up he goes");
            float top = 0f;
            for (int i = 0; i < Huddle.Ticks(w, w.Cfg.defense.swatActiveS) + 2 && DefenseMoves.Get(w, Def).Move == DefMove.Jump; i++)
            {
                top = System.Math.Max(top, PocketFootball.View.DefenseMovesView.LiftYd(w, d));
                w.Step(null);
            }
            Assert.Greater(top, 0f, "his feet left the grass on the TV");
            Assert.AreEqual(DefMove.None, DefenseMoves.Get(w, Def).Move, "and he came down");
            Assert.AreEqual(0f, PocketFootball.View.DefenseMovesView.LiftYd(w, d));
            Assert.AreEqual("NO_JUMP_ANYTIME", ConfigLoader.EnvName("jumpAnytime"));
            Assert.IsTrue(ConfigTests.Load().Feature("jumpAnytime"), "on in game.json");
        }

        [Test]
        public void NoJumpAnytimeNeedsAPassInTheAir()
        {
            var w = Live(c => c.features["jumpAnytime"] = false);
            w.Step(In(Def, new PlayerInput { Def = DefMove.Jump }));
            Assert.AreEqual(DefMove.None, DefenseMoves.Get(w, Def).Move);
        }

        // A person who only presses buttons (no stick) has not put the phone down: no bot takes over his spot.
        [Test]
        public void PressingJumpIsNotIdle()
        {
            var w = Live();
            var d = w.Find(Def);
            int ticks = (int)(w.Cfg.bots.idleTakeoverS * w.Cfg.sim.tickHz) + 5;
            int ran = 0;
            for (; ran < ticks && w.Phase == Phase.Live; ran++)
                w.Step(In(Def, new PlayerInput { Def = ran % 10 == 0 ? DefMove.Jump : DefMove.None }));
            Assert.GreaterOrEqual(ran, 30, "the play ran long enough to tell");
            Assert.Less(d.IdleTicks, 10, "each press counts as a touch");
        }

        // ---- dive tackle ----

        // The QB with the ball sprinting across the field (toward +Z, or -Z), the person on defense `gap` yards on the
        // -Z side of him.
        static (Player qb, Player d) Chase(World w, float gap, float run)
        {
            Clear(w, -25f);
            var qb = w.Find(Qb);
            var d = w.Find(Def);
            qb.VX = 0f; qb.VZ = run * w.Cfg.players.sprintSpeedYdS;
            Pin(d, qb.X, qb.Z - gap);
            return (qb, d);
        }

        static Dictionary<int, PlayerInput> Run(float run) => In(Qb, new PlayerInput { MoveZ = run, Sprint = true });

        [Test]
        public void ALateDiveMissesAndTheDiverStaysDown()
        {
            var w = Live();
            var c = w.Cfg.defense;
            var (qb, d) = Chase(w, 4.5f, 1f);           // he is already past, running away
            Assert.IsTrue(DefenseMoves.Press(w, Def, DefMove.Dive));
            w.Step(Run(1f));
            var st = DefenseMoves.Get(w, Def);
            Assert.AreEqual(DefMove.Dive, st.Move, "in the air");
            int air = Huddle.Ticks(w, c.diveAirS);
            for (int i = 0; i < air + 1 && st.Move == DefMove.Dive; i++) w.Step(Run(1f));
            Assert.AreEqual(DefMove.None, st.Move, "landed");
            Assert.AreEqual(Phase.Live, w.Phase, "no tackle: the play goes on");
            Assert.AreEqual(1, DefenseMoves.Tried(w, DefMove.Dive));
            Assert.AreEqual(0, DefenseMoves.Won(w, DefMove.Dive));
            int down = Huddle.Ticks(w, c.diveDownS);
            Assert.Greater(down, 0);
            Assert.AreEqual(down, st.DownTicks, "down for diveDownS");
            float x = d.X, z = d.Z;
            for (int i = 0; i < down - 1; i++)
            {
                DefenseMoves.Press(w, Def, DefMove.Dive);   // can't dive again from the grass
                w.Step(Run(1f));
                Assert.IsTrue(DefenseMoves.IsDown(w, d), "still down after " + (i + 1) + " of " + down + " ticks");
                Assert.AreEqual(x, d.X); Assert.AreEqual(z, d.Z);
                Assert.Greater(d.TackleCooldown, 0, "no tackles from the grass");
            }
            w.Step(Run(1f));
            Assert.IsFalse(DefenseMoves.IsDown(w, d), "back up after " + down + " ticks");
            Assert.AreEqual(0, d.TackleCooldown);
            Assert.AreEqual(1, DefenseMoves.Tried(w, DefMove.Dive), "the presses on the grass did nothing");
            Assert.AreEqual(Phase.Live, w.Phase);
        }

        [Test]
        public void AWellTimedDiveBringsHimDown()
        {
            var w = Live(c => { c.defense.diveChance = 1f; c.defense.diveStretchPenalty = 0f; });
            var (qb, d) = Chase(w, 2.5f, -1f);          // running right at him
            Assert.IsTrue(DefenseMoves.Press(w, Def, DefMove.Dive));
            for (int i = 0; i < Huddle.Ticks(w, w.Cfg.defense.diveAirS) + 1 && w.Phase == Phase.Live; i++) w.Step(Run(-1f));
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("SACKED", w.Result, "the QB behind the line");
            Assert.AreEqual(1, DefenseMoves.Won(w, DefMove.Dive));
        }

        [Test]
        public void AJukeBeatsTheDive()
        {
            var w = Live(c => { c.defense.diveChance = 1f; c.defense.diveStretchPenalty = 0f; });
            var (qb, d) = Chase(w, 2.5f, -1f);
            Assert.IsTrue(DefenseMoves.Press(w, Def, DefMove.Dive));
            w.Step(In(Qb, new PlayerInput { MoveZ = -1f, MoveX = 1f, Sprint = true, Juke = true }));
            for (int i = 0; i < Huddle.Ticks(w, w.Cfg.defense.diveAirS) + 1 && DefenseMoves.Get(w, Def).Move == DefMove.Dive; i++) w.Step(Run(-1f));
            Assert.AreEqual(Phase.Live, w.Phase, "juked");
            Assert.AreEqual(0, DefenseMoves.Won(w, DefMove.Dive));
            Assert.IsTrue(DefenseMoves.IsDown(w, d));
        }

        // ---- the phone buttons ----

        [Test]
        public void ThePhoneButtonsBecomeMoves()
        {
            var src = new PocketFootball.Input.PhoneSource(new PocketParty.Net.Seat { Slot = 0 }) { Aid = "rush1" };
            foreach (var kv in new Dictionary<string, DefMove> { { "swim", DefMove.Swim }, { "bull", DefMove.BullRush }, { "swat", DefMove.Swat }, { "jump", DefMove.Jump } })
            {
                Assert.IsTrue(src.OnButton(kv.Key, true, "rush1"), kv.Key);
                Assert.AreEqual(kv.Value, src.Poll().Def, kv.Key);
                Assert.AreEqual(DefMove.None, src.Poll().Def, kv.Key + " is pressed once");
            }
            Assert.IsTrue(src.OnButton("dive", true, "rush1"));
            var i = src.Poll();
            Assert.AreEqual(DefMove.Dive, i.Def);
            Assert.IsTrue(i.Tackle, "DIVE TACKLE is still a tackle when the moves are off");
        }

        [Test]
        public void WithTheMovesOffDiveTackleIsAPlainTackle()
        {
            var cfg = ConfigTests.Load();
            cfg.features["defmoves"] = false;
            var w = WorldTests.Full(cfg);
            var input = new PlayerInput { Def = DefMove.Dive, Tackle = true };
            DefenseMoves.Act(w, w.Players[0], ref input);
            Assert.IsTrue(input.Tackle);
        }

        [Test]
        public void TheDiveButtonDivesInAGame()
        {
            var w = Live();
            var (qb, d) = Chase(w, 4.5f, 1f);
            w.Step(new Dictionary<int, PlayerInput>
            {
                { Qb, new PlayerInput { MoveZ = 1f, Sprint = true } },
                { Def, new PlayerInput { Def = DefMove.Dive, Tackle = true } },
            });
            Assert.AreEqual(DefMove.Dive, DefenseMoves.Get(w, Def).Move, "in the air");
            Assert.AreEqual(0, d.StumbleTicks, "and no plain tackle try as well");
        }

        // ---- the line ----

        // Until a bot defender is held by a block for more than `ticks` ticks; null if none is.
        static Player HeldFor(World w, int ticks)
        {
            for (int i = 0; i < w.Cfg.sim.tickHz * 3 && w.Phase == Phase.Live; i++)
            {
                foreach (var p in w.Players) if (p.Team == w.Defense && !p.Human && p.BlockedTicks > ticks) return p;
                w.Step(null);
            }
            return null;
        }

        static World Rush(System.Action<GameConfig> tune)
        {
            var w = WorldTests.Full(Cfg(c => { c.defense.botLineMoveS = 99f; tune(c); }));   // the bots leave the line moves to the test
            w.Step(null);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "mblitz";
            for (int i = 0; i < w.Cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            return w;
        }

        [Test]
        public void AQuickSwimSlipsTheBlock()
        {
            var w = Rush(c => c.defense.swimEarlyChance = 1f);
            var p = HeldFor(w, 0);
            Assert.IsNotNull(p, "the line engages");
            DefenseMoves.Press(w, p.Slot, DefMove.Swim);
            w.Step(null);
            Assert.AreEqual(1, DefenseMoves.Won(w, DefMove.Swim));
            Assert.AreEqual(0, p.BlockedTicks);
            Assert.Greater(p.FreeTicks, 0, "running free");
        }

        [Test]
        public void ALateSwimUsesTheLateOdds()
        {
            var w = Rush(c => { c.defense.swimEarlyChance = 1f; c.defense.swimLateChance = 0f; });
            var p = HeldFor(w, Huddle.Ticks(w, w.Cfg.defense.swimEarlyS) + 1);
            Assert.IsNotNull(p, "a block lasts past the early window");
            DefenseMoves.Press(w, p.Slot, DefMove.Swim);
            w.Step(null);
            Assert.AreEqual(1, DefenseMoves.Tried(w, DefMove.Swim));
            Assert.AreEqual(0, DefenseMoves.Won(w, DefMove.Swim), "too late to swim");
        }

        [Test]
        public void ABullRushDrivesTheBlockerBack()
        {
            var w = Rush(c => { c.defense.bullSnapChance = 1f; c.defense.bullChance = 1f; });
            var p = HeldFor(w, 0);
            Assert.IsNotNull(p);
            DefenseMoves.Press(w, p.Slot, DefMove.BullRush);
            w.Step(null);
            Assert.AreEqual(1, DefenseMoves.Won(w, DefMove.BullRush));
            bool stumbling = false;
            foreach (var o in w.Players) if (o.Team == w.Offense && o.StumbleTicks > 0) stumbling = true;
            Assert.IsTrue(stumbling, "the blocker stumbles back");
            Assert.Greater(p.FreeTicks, 0);
        }

        // ---- bot games ----

        [Test]
        public void SameSeedSameGameWithTheMoves()
        {
            var cfg = ConfigTests.Load();
            cfg.features["defmoves"] = true;
            var a = WorldTests.Full(cfg, 7);
            var b = WorldTests.Full(cfg, 7);
            for (int i = 0; i < a.Cfg.sim.tickHz * 90; i++) { a.Step(null); b.Step(null); }
            Assert.AreEqual(a.Hash(), b.Hash());
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BotGamesWithTheMovesStaySensible(int seed)
        {
            var cfg = ConfigTests.Load();
            cfg.features["defmoves"] = true;                // the bots' own odds, as shipped
            var w = WorldTests.Full(cfg, seed);
            for (int i = 0; i < 10 * 60 * cfg.sim.tickHz; i++) w.Step(null);
            var sb = new StringBuilder();
            sb.Append(w.PlaysRun).Append(" plays, ").Append(w.YardsGained).Append(" yards:");
            foreach (var kv in w.Results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
            foreach (DefMove m in System.Enum.GetValues(typeof(DefMove)))
                if (m != DefMove.None) sb.Append(' ').Append(m).Append(' ').Append(DefenseMoves.Won(w, m)).Append('/').Append(DefenseMoves.Tried(w, m)).Append(',');
            string r = sb.ToString();
            TestContext.WriteLine("seed " + seed + ": " + r);
            int Count(string k) => w.Results.TryGetValue(k, out int n) ? n : 0;
            Assert.GreaterOrEqual(w.PlaysRun, 20, "plays keep happening: " + r);
            Assert.That(w.YardsGained / (float)w.PlaysRun, Is.InRange(1f, 15f), "yards per play: " + r);
            Assert.Less(Count("SACKED"), w.PlaysRun * 0.4f, r);
            Assert.Less(Count("INCOMPLETE"), w.PlaysRun * 0.6f, r);
            Assert.Less(Count("PLAY OVER"), w.PlaysRun * 0.1f, r);
            Assert.Greater(DefenseMoves.Tried(w, DefMove.Swim) + DefenseMoves.Tried(w, DefMove.BullRush), 0, "the bot line uses its moves: " + r);
            Assert.Greater(DefenseMoves.Tried(w, DefMove.Dive), 0, "bots dive: " + r);
        }
    }
}
