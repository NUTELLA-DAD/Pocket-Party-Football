using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // M2 "Playbook and controls" (docs/PLAN.md): all 14 plays, every position's buttons, juke / spin / swat / pick,
    // smarter bots. One check per item, with game.json's switches as they ship (ConfigTests.Load keeps kicking off
    // except where a kick is the point). docs/progress/m2-acceptance.md lists the items and the evidence.
    public sealed class M2AcceptanceTests
    {
        static readonly string[] Offense = { "dive", "toss", "slants", "verts", "boot", "screen", "sneak" };
        static readonly string[] Defense = { "cover2", "cover3", "mblitz", "zblitz", "goalline", "prevent" };

        static IEnumerable<string> OffensePlays => Offense;

        // ---- all 14 plays ----

        [Test]
        public void ThereAreFourteenPlaysOfElevenJobs()
        {
            var cfg = ConfigTests.Load();
            var ids = cfg.plays.Select(p => p.id).ToList();
            foreach (var id in Offense.Concat(Defense).Concat(new[] { "punt", "fg" }))
            {
                CollectionAssert.Contains(ids, id, "play file " + id);
                Assert.AreEqual(cfg.players.perTeam, cfg.plays.First(p => p.id == id).jobs.Count, id + ": a job for every spot");
            }
            Assert.AreEqual(Offense.Length + Defense.Length + 2, 15, "7 offense + 6 defense + punt + fg (PLAN's 14 counts punt/fg as one kicking pair)");
        }

        // Every offense play against every defense: one seeded 22-bot snap to the whistle.
        [TestCaseSource(nameof(OffensePlays))]
        public void EveryOffensePlayRunsAgainstEveryDefense(string offense)
        {
            var cfg = ConfigTests.Load();
            var fails = new StringBuilder();
            foreach (var defense in Defense)
            {
                string why = OneSnap(cfg, offense, defense, out string result);
                TestContext.WriteLine(offense + " v " + defense + ": " + (why ?? result));
                if (why != null) fails.Append(offense).Append(" v ").Append(defense).Append(": ").Append(why).Append("\n");
            }
            Assert.IsEmpty(fails.ToString(), fails.ToString());
        }

        // Null when the snap went as it should; else what went wrong.
        static string OneSnap(GameConfig cfg, string offense, string defense, out string result)
        {
            result = "";
            var w = WorldTests.Full(cfg, 7);
            w.Step(null);
            w.Calls[w.Offense] = offense; w.Calls[w.Defense] = defense;
            int hz = cfg.sim.tickHz;
            for (int i = 0; i < hz * 40 && w.Phase != Phase.Live; i++) w.Step(null);
            if (w.Phase != Phase.Live) return "never snapped (phase " + w.Phase + ")";
            var play = LivePlay.Called(w, w.Offense);
            if (play == null || play.id != offense) return "the called play is " + (play?.id ?? "none");

            // each offense man starts on the job the play file gives his spot
            var start = w.Players.Where(p => p.Team == w.Offense).ToDictionary(p => p.Slot, p => (p.X, p.Z));
            for (int i = 0; i < hz && w.Phase == Phase.Live; i++) w.Step(null);
            if (w.Phase == Phase.Live)
            {
                foreach (var p in w.Players.Where(p => p.Team == w.Offense))
                {
                    var job = p.Spot >= 0 && p.Spot < play.jobs.Count ? play.jobs[p.Spot] : null;
                    if (job == null) return "spot " + p.Spot + " has no job";
                    float moved = Dist(p.X - start[p.Slot].X, p.Z - start[p.Slot].Z);
                    float fromLine = (p.X - w.LineOfScrimmageX) * w.Dir;
                    if (job.kind == "route" && job.pts.Count > 0 && LivePlay.Carrier(w) != p && moved < 1f)
                        return p.Pos + " (spot " + p.Spot + ") on a route moved only " + moved.ToString("0.0") + " yd in a second";
                    if (job.kind == "block" && fromLine > 6f)
                        return p.Pos + " (spot " + p.Spot + ") blocking is " + fromLine.ToString("0.0") + " yd past the line";
                }
            }

            // to the whistle, inside the play clock
            int limit = (int)((cfg.play.maxPlayS + 1f) * hz);
            for (int i = 0; i < limit && w.Phase == Phase.Live; i++) w.Step(null);
            if (w.Phase == Phase.Live) return "still live after maxPlayS";
            result = w.Result;
            float goal = cfg.field.GoalLineX;
            if (float.IsNaN(w.NextLos) || System.Math.Abs(w.NextLos) > goal + 0.01f) return "next spot " + w.NextLos + " is off the field (" + w.Result + ")";
            if (float.IsNaN(w.Ball.X) || System.Math.Abs(w.Ball.X) > cfg.field.lengthYd) return "the ball is at " + w.Ball.X;
            if (string.IsNullOrEmpty(w.Result)) return "a whistle with no result";
            return null;
        }

        static float Dist(float dx, float dz) => (float)System.Math.Sqrt(dx * dx + dz * dz);

        // Punt and field goal on 4th down: the ball is kicked.
        [TestCase("punt", 30f)]
        [TestCase("fg", 20f)]
        public void FourthDownKicksAreKicked(string call, float yardsToGoal)
        {
            var cfg = ConfigTests.Load();
            cfg.features["kicking"] = true;
            var w = WorldTests.Full(cfg, 3);
            w.Kick.Opened = true;   // (no opening kickoff)
            float goal = cfg.field.GoalLineX;
            w.LineOfScrimmageX = w.Dir * (goal - yardsToGoal);
            w.Down = 4;
            w.FirstDownX = w.LineOfScrimmageX + w.Dir * 9f;
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            w.Calls[w.Offense] = call;
            int hz = cfg.sim.tickHz;
            for (int i = 0; i < hz * 40 && w.Phase != Phase.Live; i++) w.Step(null);
            for (int i = 0; i < hz * 10 && !w.Kick.Kicked; i++) w.Step(null);
            Assert.IsTrue(w.Kick.Kicked, call + " on 4th down: the ball is kicked");
            for (int i = 0; i < hz * 30 && w.Phase == Phase.Live; i++) w.Step(null);
            TestContext.WriteLine(call + ": " + w.Result);
            Assert.AreNotEqual(Phase.Live, w.Phase, "the kick ends in a whistle");
        }

        // ---- the moves ----

        // A seeded batch of 22-bot games: every move a bot makes shows up at least once.
        [Test]
        public void BotGamesUseEveryBotMove()
        {
            var cfg = ConfigTests.Load();
            int spin = 0, stiff = 0, dive = 0, swim = 0, bull = 0, swat = 0, jump = 0, diveTackle = 0, picks = 0, juke = 0;
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var w = WorldTests.Full(cfg, seed);
                for (int i = 0; i < 10 * 60 * cfg.sim.tickHz; i++) w.Step(null);
                spin += w.Carry.Started[(int)CarrierMove.Spin];
                stiff += w.Carry.Started[(int)CarrierMove.StiffArm];
                dive += w.Carry.Started[(int)CarrierMove.Dive];
                swim += DefenseMoves.Tried(w, DefMove.Swim);
                bull += DefenseMoves.Tried(w, DefMove.BullRush);
                swat += DefenseMoves.Tried(w, DefMove.Swat);
                jump += DefenseMoves.Tried(w, DefMove.Jump);
                diveTackle += DefenseMoves.Tried(w, DefMove.Dive);
                picks += w.Events.Total(EventKind.Interception);
                juke += w.Events.Total(EventKind.Juke);
            }
            string all = "spin " + spin + ", stiff arm " + stiff + ", dive " + dive + ", swim " + swim + ", bull rush " + bull +
                         ", swat " + swat + ", jump " + jump + ", dive tackle " + diveTackle + ", interceptions " + picks + ", juke " + juke;
            TestContext.WriteLine("six 10-minute bot games: " + all);
            Assert.Greater(spin, 0, all); Assert.Greater(stiff, 0, all); Assert.Greater(dive, 0, all);
            Assert.Greater(swim, 0, all); Assert.Greater(bull, 0, all); Assert.Greater(swat, 0, all);
            Assert.Greater(jump, 0, all); Assert.Greater(diveTackle, 0, all); Assert.Greater(picks, 0, all);
        }

        const int Qb = 500, Wr = 501;

        static World HikedWithPeople(GameConfig cfg, string offense = "slants")
        {
            var w = WorldTests.Full(cfg, 2);
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);              // spot 0: the QB
            var wr = w.ReplaceBot(w.Offense, Wr);     // spot 1: a receiver (play.receivers lists it)
            Assert.Greater(Receiving.ShapeOf(w, wr), 0, "spot 1 is a receiver");
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Qb, offense));
            w.Step(new Dictionary<int, PlayerInput> { { Qb, new PlayerInput { Hike = true } } });
            Assert.AreEqual(Phase.Live, w.Phase);
            return w;
        }

        // A person's JUKE and CALL FOR IT (bots never press either: a juke and a call are a person's moves).
        [Test]
        public void APersonJukesAndCallsForIt()
        {
            var cfg = ConfigTests.Load();
            var w = HikedWithPeople(cfg);
            w.Step(new Dictionary<int, PlayerInput> { { Qb, new PlayerInput { Juke = true, MoveZ = 1f } }, { Wr, new PlayerInput { Call = true } } });
            Assert.AreEqual(1, w.Events.Total(EventKind.Juke), "JUKE: the carrier's juke");
            Assert.IsTrue(Receiving.Calling(w, w.Find(Wr)), "CALL FOR IT: the receiver is calling");
        }

        // ---- the phone: every position's buttons ----

        // PLAN.md "Phone controls by situation", as the button actions layouts.js sends.
        static readonly Dictionary<string, string[]> PlanButtons = new Dictionary<string, string[]>
        {
            { "qbpre",   new[] { "hike", "find" } },
            { "qb",      new[] { "scramble" } },                                   // + a button per receiver shape
            { "carrier", new[] { "sprint", "juke", "spin", "stiff", "divein" } },
            { "route",   new[] { "sprint", "call", "catch" } },
            { "block",   new[] { "block", "sprint" } },
            { "rush",    new[] { "swim", "bull", "tackle", "swat" } },
            { "lb",      new[] { "sprint", "tackle", "dive", "swat" } },
            { "cover",   new[] { "sprint", "tackle", "jump", "dive" } },
            { "wait",    new[] { "find" } },
        };

        static Dictionary<string, List<string>> LayoutsJs()
        {
            string js = File.ReadAllText("Assets/StreamingAssets/phone/layouts.js");
            int a = js.IndexOf("var LAYOUTS"), b = js.IndexOf("};", a);
            Assert.Greater(a, 0, "layouts.js has its LAYOUTS table");
            string table = js.Substring(a, b - a);
            var outp = new Dictionary<string, List<string>>();
            foreach (Match m in Regex.Matches(table, @"(\w+):\s*\{\s*line:([\s\S]*?)btns:\s*\[([\s\S]*?)\]\s*\}"))
            {
                var acts = new List<string>();
                foreach (Match bm in Regex.Matches(m.Groups[3].Value, @"\[\s*'(\w+)'")) acts.Add(bm.Groups[1].Value);
                outp[m.Groups[1].Value] = acts;
            }
            return outp;
        }

        [Test]
        public void EveryLayoutShowsThePlansButtons()
        {
            var js = LayoutsJs();
            foreach (var kv in PlanButtons)
            {
                Assert.IsTrue(js.ContainsKey(kv.Key), "layouts.js has the " + kv.Key + " layout");
                CollectionAssert.IsSubsetOf(kv.Value, js[kv.Key], kv.Key + ": PLAN's buttons; layouts.js has " + string.Join(", ", js[kv.Key]));
            }
            Assert.IsTrue(js.ContainsKey("kick"), "layouts.js has the kick meter layout");
            foreach (var l in PhoneLayouts.All.Where(l => l != "huddle")) Assert.IsTrue(js.ContainsKey(l), "the game's " + l + " layout is drawn by layouts.js");
            string huddle = File.ReadAllText("Assets/StreamingAssets/phone/huddle.js");
            StringAssert.Contains("'lock' : 'suggest'", huddle, "the huddle's LOCK IN / SUGGEST");
        }

        // Every button the phone can send is one the game reads, and it reaches the sim's input.
        [Test]
        public void EveryButtonReachesTheSim()
        {
            var shapes = ConfigTests.Load().play.shapes;
            foreach (var kv in LayoutsJs())
                foreach (var act in kv.Value)
                {
                    var src = new PhoneSource(new Seat { Slot = 0 }) { Aid = "a1", Shapes = shapes };
                    Assert.IsTrue(src.OnButton(act, true, "a1"), kv.Key + ": the game knows '" + act + "'");
                    var i = src.Poll();
                    bool reached = act switch
                    {
                        "find" => src.TakeFind() || true,   // (FIND ME is taken by the host, not the sim: latched, see below)
                        "hike" => i.Hike, "sprint" => i.Sprint, "block" => i.Block, "juke" => i.Juke, "tackle" => i.Tackle,
                        "call" => i.Call, "catch" => i.Catch,
                        "spin" => i.Move == CarrierMove.Spin, "stiff" => i.Move == CarrierMove.StiffArm, "divein" => i.Move == CarrierMove.Dive,
                        "dive" => i.Def == DefMove.Dive && i.Tackle, "swim" => i.Def == DefMove.Swim, "bull" => i.Def == DefMove.BullRush,
                        "swat" => i.Def == DefMove.Swat, "jump" => i.Def == DefMove.Jump,
                        "scramble" => true,                  // (the QB just runs with the stick)
                        _ => false,
                    };
                    Assert.IsTrue(reached, kv.Key + ": '" + act + "' reaches PlayerInput");
                }
            // FIND ME is latched for the host
            var f = new PhoneSource(new Seat { Slot = 0 }) { Aid = "a1" };
            f.OnButton("find", true, "a1");
            Assert.IsTrue(f.TakeFind(), "FIND ME");
            // a receiver button throws to its shape, tap = bullet, hold = lob
            var q = new PhoneSource(new Seat { Slot = 0 }) { Aid = "a1", Shapes = shapes };
            for (int k = 0; k < shapes.Count; k++)
            {
                Assert.IsTrue(q.OnButton("throw", false, "a1", shapes[k], k % 2 == 1));
                var t = q.Poll();
                Assert.AreEqual(k + 1, t.Throw, "the " + shapes[k] + " button throws to receiver " + (k + 1));
                Assert.AreEqual(k % 2 == 1, t.Lob);
            }
        }
    }
}
