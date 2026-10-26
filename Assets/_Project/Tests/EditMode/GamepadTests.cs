using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // WS-14: an Xbox pad joins, picks a team and plays a whole down through the same buttons as a phone.
    // The pad here is a PadState per frame; the frame loop does what Boot/GamepadHost does each frame.
    public sealed class GamepadTests
    {
        const int BotSlotBase = 100;

        sealed class Game
        {
            public GameConfig Cfg;
            public World W;
            public Roster R;
            public float Now;
            public readonly Dictionary<int, PlayerInput> Inputs = new Dictionary<int, PlayerInput>();
        }

        // Both teams all bots in the bots' slots, like PartyHost.FillWithBots; nobody has joined yet.
        static Game NewGame(System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            tune?.Invoke(cfg);
            var w = new World(cfg, 1);
            for (int i = 0; i < cfg.players.perTeam * cfg.teams.Count; i++) w.AddPlayer(BotSlotBase + i, i % cfg.teams.Count, false);
            return new Game { Cfg = cfg, W = w, R = new Roster(cfg.net.maxPlayers) };
        }

        static GamepadSource Pad(Game g, int number = 1)
        {
            var seat = GamepadSource.Join(g.R, g.Cfg.gamepad, number);
            Assert.IsNotNull(seat);
            return new GamepadSource(seat, g.Cfg.gamepad, g.Cfg.play.shapes);
        }

        static PadState Held(params PadButton[] b) => new PadState().With(b);

        // One frame with no tick (the lobby holds the game): the pad reads its layout and its commands run.
        static void Hold(Game g, GamepadSource pad, PadState s)
        {
            pad.Feed(s, GamepadSource.LayoutFor(g.W, pad.Seat), g.Now);
            while (pad.TakeCommand(out var cmd))
            {
                if (cmd.StartsWith("team:")) GamepadSource.Pick(g.W, pad.Seat, int.Parse(cmd.Substring(5)), g.Cfg.teams.Count, BotSlotBase);
                else if (cmd == "agree") GamepadSource.Agree(g.W, pad.Slot);
                else if (cmd == "kick") GamepadSource.Kick(g.W, pad.Slot, pad.KickPower, pad.KickAim);
            }
        }

        // One frame and one tick: as above, then the sim steps with the pad's input.
        static void Frame(Game g, GamepadSource pad, PadState s)
        {
            Hold(g, pad, s);
            g.Inputs.Clear();
            g.Inputs[pad.Slot] = pad.Poll();
            g.W.Step(g.Inputs);
            g.Now += g.W.Dt;
        }

        // A tap: down one frame, up the next.
        static void Tap(Game g, GamepadSource pad, PadButton b)
        {
            Frame(g, pad, Held(b));
            Frame(g, pad, default(PadState));
        }

        // ---- the switch ----
        [Test]
        public void ShipsOffAndNoGamepadTurnsItOff()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("gamepad"), "FEATURES.gamepad is on in game.json (the merge captain turned it on)");
            cfg.features["gamepad"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_GAMEPAD" ? "1" : null);
            Assert.IsFalse(cfg.Feature("gamepad"));
            Assert.AreEqual("NO_GAMEPAD", ConfigLoader.EnvName("gamepad"));
        }

        // ---- every phone layout has pad buttons ----
        [Test]
        public void EveryPhoneLayoutIsMapped()
        {
            var pads = ConfigTests.Load().gamepad;
            var layouts = new List<string>(PhoneLayouts.All) { "walk", "pick" };
            foreach (var l in layouts) Assert.IsTrue(pads.layouts.ContainsKey(l), "no pad buttons for the '" + l + "' layout");

            // the buttons each phone layout shows today (phone/layouts.js); a pad presses every one of them
            var phone = new Dictionary<string, string[]>
            {
                ["wait"] = new[] { "find" }, ["qbpre"] = new[] { "hike", "find" }, ["qb"] = new[] { "scramble" },
                ["carrier"] = new[] { "sprint", "juke", "spin", "stiff" }, ["route"] = new[] { "sprint", "call", "catch" },
                ["block"] = new[] { "block", "sprint" }, ["rush"] = new[] { "swim", "bull", "tackle", "swat" },
                ["lb"] = new[] { "sprint", "tackle", "dive", "swat" }, ["cover"] = new[] { "sprint", "tackle", "jump", "dive" },
                ["walk"] = new[] { "sprint" }, ["huddle"] = new[] { "agree" }, ["kick"] = new[] { "kick" }, ["pick"] = new[] { "team:0", "team:1" },
            };
            foreach (var kv in phone)
                foreach (var b in kv.Value)
                    Assert.IsTrue(Maps(pads, kv.Key, b), "the '" + kv.Key + "' layout has no pad button for " + b);
            Assert.IsTrue(Maps(pads, "qb", "throw:1") && Maps(pads, "qb", "throw:2") && Maps(pads, "qb", "throw:3"), "the QB throws to the receivers");

            // a heads-up (not a failure) when layouts.js grows a button the gamepad block doesn't have yet
            string js = File.ReadAllText("Assets/StreamingAssets/phone/layouts.js");
            var heads = Regex.Matches(js, @"(\w+):\s*\{\s*line:");
            for (int i = 0; i < heads.Count; i++)
            {
                string name = heads[i].Groups[1].Value;
                int end = i + 1 < heads.Count ? heads[i + 1].Index : js.IndexOf("};", heads[i].Index);
                if (end < 0) end = js.Length;
                foreach (Match m in Regex.Matches(js.Substring(heads[i].Index, end - heads[i].Index), @"\[\s*'(\w+)'\s*,"))
                    if (!Maps(pads, name, m.Groups[1].Value))
                        UnityEngine.Debug.LogWarning("[pad] phone layout '" + name + "' has a " + m.Groups[1].Value + " button with no pad button in game.json");
            }
        }

        static bool Maps(GamepadConfig c, string layout, string action)
        {
            if (c.layouts.TryGetValue(layout, out var m) && m.ContainsValue(action)) return true;
            return c.any.ContainsValue(action);
        }

        // ---- join ----
        [Test]
        public void APadJoinsAsAPersonLikeAPhone()
        {
            var g = NewGame();
            var pad = Pad(g);
            var two = Pad(g, 2);
            Assert.AreEqual("PAD 1", pad.Seat.Name);
            Assert.AreEqual("PAD 2", two.Seat.Name);
            Assert.AreNotEqual(pad.Slot, two.Slot);
            Assert.AreEqual(SeatKind.Phone, pad.Seat.Kind, "the lobby and the head counts see a person");
            Assert.IsTrue(pad.Connected);
            Assert.Less(pad.Seat.Conn, 0, "no connection: nothing is ever sent to a pad, so it can't see the other team's play");
            Assert.AreEqual(-1, pad.Seat.Team);
            Assert.AreEqual("pick", GamepadSource.LayoutFor(g.W, pad.Seat));
            Assert.IsNull(g.W.Find(pad.Slot), "no player until it picks a team");
        }

        [Test]
        public void TheGameFullStopsAPad()
        {
            var g = NewGame();
            for (int i = 0; i < g.R.Capacity; i++) Assert.IsNotNull(GamepadSource.Join(g.R, g.Cfg.gamepad, i + 1));
            Assert.IsNull(GamepadSource.Join(g.R, g.Cfg.gamepad, g.R.Capacity + 1));
        }

        [Test]
        public void TheJoiningStartDoesNothingMore()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Prime(Held(PadButton.Start));
            pad.Feed(Held(PadButton.Start), "pick", 0f);
            Assert.IsFalse(pad.TakeCommand(out _), "still the same press");
            pad.Feed(default(PadState), "pick", 0f);
            pad.Feed(Held(PadButton.Start), "pick", 0f);
            Assert.IsTrue(pad.TakeCommand(out var cmd));
            Assert.AreEqual("kickoff", cmd, "a new START press is the lobby's KICK OFF");
        }

        // ---- pick ----
        [Test]
        public void ShouldersPickATeamAndTakeABotsSpot()
        {
            var g = NewGame();
            var pad = Pad(g);
            Hold(g, pad, Held(PadButton.RB));                              // in the lobby: the game hasn't ticked
            Hold(g, pad, default(PadState));
            Assert.AreEqual(1, pad.Seat.Team);
            var p = g.W.Find(pad.Slot);
            Assert.IsNotNull(p);
            Assert.IsTrue(p.Human);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(g.Cfg.players.perTeam, g.W.TeamCount(1), "took a bot's spot, the team is still full");
            Assert.AreEqual(1, g.R.TeamCount(1, true), "counted as a person, like a phone");
            Assert.AreEqual("pick", GamepadSource.LayoutFor(g.W, pad.Seat), "until kickoff a pad can still change sides");

            // changing sides: the old spot goes back to a bot
            Hold(g, pad, Held(PadButton.LB));
            Hold(g, pad, default(PadState));
            Assert.AreEqual(0, pad.Seat.Team);
            Assert.AreEqual(0, g.W.Find(pad.Slot).Team);
            Assert.AreEqual(g.Cfg.players.perTeam, g.W.TeamCount(0));
            Assert.AreEqual(g.Cfg.players.perTeam, g.W.TeamCount(1), "a bot took the spot left behind");
            Assert.AreEqual(0, g.R.TeamCount(1, true));

            // once the game runs, LB and RB are game buttons again: no switching sides by accident
            Frame(g, pad, default(PadState));
            Assert.AreNotEqual("pick", GamepadSource.LayoutFor(g.W, pad.Seat));
            Tap(g, pad, PadButton.RB);
            Assert.AreEqual(0, pad.Seat.Team);
            Assert.AreEqual(0, g.W.Find(pad.Slot).Team);
        }

        // ---- the kick meter: hold A for power, the stick aims, let go to kick ----
        [Test]
        public void TheKickerPadKicks()
        {
            var g = NewGame(c => c.features["kicking"] = true);
            var pad = Pad(g);
            Hold(g, pad, Held(g.Cfg.kicking.openingKickTeam == 0 ? PadButton.LB : PadButton.RB));   // the kicking team: its QB spot kicks
            Hold(g, pad, default(PadState));
            int cap = g.Cfg.sim.tickHz * 5;
            for (int i = 0; i < cap && GamepadSource.LayoutFor(g.W, pad.Seat) != "kick"; i++) Frame(g, pad, default(PadState));
            Assert.AreEqual("kick", GamepadSource.LayoutFor(g.W, pad.Seat), "the opening kickoff: the pad is the kicker");
            Assert.IsFalse(g.W.Kick.Kicked);
            Frame(g, pad, Held(PadButton.A));
            for (int i = 0; i < g.Cfg.sim.tickHz / 2; i++) Frame(g, pad, new PadState { X = 0.6f }.With(PadButton.A));
            Assert.IsFalse(g.W.Kick.Kicked, "the kick goes when A is let go");
            Frame(g, pad, new PadState { X = 0.6f });
            Assert.IsTrue(g.W.Kick.Kicked, "let go: the ball is kicked");
            Assert.Greater(pad.KickPower, 0f);
            Assert.Less(pad.KickPower, 1f, "half a second of a one-second meter");
            Assert.Greater(pad.KickAim, 0f, "aimed right");
        }

        [Test]
        public void AFullTeamTurnsAPadAway()
        {
            var g = NewGame();
            foreach (var p in g.W.Players) if (p.Team == 0) p.Human = true;
            var pad = Pad(g);
            Assert.IsNull(GamepadSource.Pick(g.W, pad.Seat, 0, g.Cfg.teams.Count, BotSlotBase));
            Assert.AreEqual(-1, pad.Seat.Team);
            Assert.IsNull(GamepadSource.Pick(g.W, pad.Seat, 7, g.Cfg.teams.Count, BotSlotBase), "no such team");
        }

        // ---- the buttons ----
        [Test]
        public void StickMovesWithADeadzone()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Feed(new PadState { X = 1f, Y = 1f }, "carrier", 0f);
            var i = pad.Poll();
            Assert.Greater(i.MoveX, 0f, "right on the pad is right on the TV");
            Assert.Greater(i.MoveZ, 0f, "up on the pad is away from the camera");
            pad.Feed(new PadState { X = g.Cfg.gamepad.deadzone * 0.5f }, "carrier", 0f);
            Assert.AreEqual(0f, pad.Poll().MoveX, "a resting stick doesn't creep");
        }

        [Test]
        public void SprintIsHeldAndLetGoWhenTheLayoutChanges()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Feed(Held(PadButton.RT), "carrier", 0f);
            Assert.IsTrue(pad.Poll().Sprint);
            pad.Feed(Held(PadButton.RT), "route", 0f);
            Assert.IsTrue(pad.Poll().Sprint, "still sprinting: RT is SPRINT here too");
            pad.Feed(Held(PadButton.RT), "wait", 0f);
            Assert.IsFalse(pad.Poll().Sprint, "no SPRINT on the wait screen");
            pad.Feed(Held(PadButton.RT), "carrier", 0f);
            Assert.IsTrue(pad.Poll().Sprint);
            pad.Feed(default(PadState), "carrier", 0f);
            Assert.IsFalse(pad.Poll().Sprint);
        }

        [Test]
        public void PressesAreReadInTheirOwnLayout()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Feed(Held(PadButton.A), "qbpre", 0f);
            Assert.IsTrue(pad.Poll().Hike, "A is HIKE before the snap");
            pad.Feed(default(PadState), "carrier", 0f);
            pad.Feed(Held(PadButton.A), "carrier", 0f);
            var i = pad.Poll();
            Assert.IsFalse(i.Hike);
            Assert.IsTrue(i.Juke, "A is JUKE with the ball");
            pad.Feed(default(PadState), "cover", 0f);
            pad.Feed(Held(PadButton.X), "cover", 0f);
            Assert.IsTrue(pad.Poll().Tackle, "X is TACKLE on defense");
            Assert.IsFalse(pad.Poll().Tackle, "a press is taken once");
        }

        [Test]
        public void ATapIsABulletAndAHoldIsALob()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Feed(Held(PadButton.B), "qb", 0f);
            Assert.AreEqual(0, pad.Poll().Throw, "the throw goes on release");
            pad.Feed(default(PadState), "qb", 0.05f);
            var i = pad.Poll();
            Assert.AreEqual(2, i.Throw, "B throws to the second receiver");
            Assert.IsFalse(i.Lob);
            pad.Feed(Held(PadButton.A), "qb", 1f);
            pad.Feed(default(PadState), "qb", 1f + g.Cfg.gamepad.lobHoldS + 0.05f);
            i = pad.Poll();
            Assert.AreEqual(1, i.Throw);
            Assert.IsTrue(i.Lob, "held: a lob");
            pad.Feed(Held(PadButton.A), "qb", 2f);
            pad.Feed(default(PadState), "carrier", 2.05f);
            Assert.AreEqual(0, pad.Poll().Throw, "a throw let go after the QB lost the ball goes nowhere");
        }

        [Test]
        public void AnUnpluggedPadStops()
        {
            var g = NewGame();
            var pad = Pad(g);
            pad.Feed(new PadState { X = 1f }.With(PadButton.RT), "carrier", 0f);
            g.R.Disconnect(pad.Seat);
            pad.OnDisconnect();
            var i = pad.Poll();
            Assert.AreEqual(0f, i.MoveX);
            Assert.IsFalse(i.Sprint);
            Assert.IsFalse(pad.Connected);
        }

        // ---- the huddle: a pad can't see the playbook, so A goes with the team ----
        [Test]
        public void TheCallerPadLocksTheTeamsFavourite()
        {
            var g = NewGame();
            var pad = Pad(g);
            Tap(g, pad, PadButton.LB);                                     // offense, QB: the caller
            var phone = g.R.JoinPhone(5, "ANN", null, "p", out _);
            g.W.ReplaceBot(0, phone.Slot);
            Assert.AreEqual(Phase.Huddle, g.W.Phase);
            Assert.IsTrue(Huddle.Suggest(g.W, phone.Slot, "verts"));       // a teammate's phone thumbs-up
            Tap(g, pad, PadButton.A);
            Assert.AreEqual("verts", g.W.Calls[0]);
        }

        [Test]
        public void WithNoThumbsTheCallerPadLocksAPlay()
        {
            var g = NewGame();
            var pad = Pad(g);
            Tap(g, pad, PadButton.LB);
            Tap(g, pad, PadButton.A);
            Assert.IsNotNull(g.W.Calls[0]);
            Assert.AreEqual("offense", Huddle.Find(g.W, g.W.Calls[0]).side);
        }

        // ---- a whole down with the pad: join, pick, call, hike, throw, whistle, the next huddle ----
        [Test]
        public void APadPlaysAFullDown()
        {
            var g = NewGame();
            var pad = Pad(g);
            Tap(g, pad, PadButton.LB);
            var me = g.W.Find(pad.Slot);
            Assert.AreEqual("QB", me.Pos, "the first person on offense is the QB");
            var phone = g.R.JoinPhone(5, "ANN", null, "p", out _);
            g.W.ReplaceBot(0, phone.Slot);
            Assert.AreEqual(1, g.W.HuddleNumber);
            Assert.AreEqual("huddle", GamepadSource.LayoutFor(g.W, pad.Seat));
            Huddle.Suggest(g.W, phone.Slot, "slants");

            // the huddle: A locks the team's favourite, the bot defense calls when the clock runs out
            Tap(g, pad, PadButton.A);
            Assert.AreEqual("slants", g.W.Calls[0]);
            int cap = (int)((g.Cfg.huddle.seconds + 1f) * g.Cfg.sim.tickHz);
            for (int i = 0; i < cap && g.W.Phase == Phase.Huddle; i++) Frame(g, pad, default(PadState));
            Assert.AreEqual(Phase.Set, g.W.Phase);
            Assert.AreEqual("qbpre", GamepadSource.LayoutFor(g.W, pad.Seat));

            // the snap: A hikes
            Frame(g, pad, new PadState().With(PadButton.A));
            Assert.AreEqual(Phase.Live, g.W.Phase);
            Assert.AreEqual(pad.Slot, g.W.Ball.Carrier);
            Assert.AreEqual("qb", GamepadSource.LayoutFor(g.W, pad.Seat));

            // drop back a little, let the routes start, tap A: a bullet to the first receiver
            float startX = me.X;
            for (int i = 0; i < 15; i++) Frame(g, pad, new PadState { X = -g.Cfg.formation.attackDir });
            Assert.AreNotEqual(startX, me.X, "the stick moves the QB");
            Frame(g, pad, default(PadState));
            Frame(g, pad, Held(PadButton.A));
            Frame(g, pad, default(PadState));
            Assert.IsTrue(g.W.Thrown, "A threw the ball");

            // the play runs to the whistle, then the next huddle opens
            int play = (int)((g.Cfg.play.maxPlayS + 1f) * g.Cfg.sim.tickHz);
            for (int i = 0; i < play && g.W.Phase == Phase.Live; i++) Frame(g, pad, default(PadState));
            Assert.AreEqual(Phase.Whistle, g.W.Phase);
            Assert.IsNotEmpty(g.W.Result);
            Assert.AreEqual("wait", GamepadSource.LayoutFor(g.W, pad.Seat));
            int whistle = (int)((g.Cfg.play.whistleS + 1f) * g.Cfg.sim.tickHz);
            for (int i = 0; i < whistle && g.W.Phase == Phase.Whistle; i++) Frame(g, pad, default(PadState));
            Assert.AreEqual(Phase.Huddle, g.W.Phase);
            Assert.AreEqual(2, g.W.HuddleNumber);
            Assert.IsTrue(g.W.Find(pad.Slot).Human, "still the pad's player");
        }
    }
}
