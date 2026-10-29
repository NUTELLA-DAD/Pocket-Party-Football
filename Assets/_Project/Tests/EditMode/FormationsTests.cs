using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.formations: each play names a formation (formation.sets), and the team lines up in it once the calls
    // are in. The huddle stays in the base line-up; NO_FORMATIONS=1 keeps the base line-up all game.
    public sealed class FormationsTests
    {
        const int Qb = 500, Mlb = 600;

        static World Called(string offense, string defense, Dictionary<string, string> env = null)
        {
            var w = WorldTests.Full(ConfigTests.Load(env));
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Defense, Mlb);
            Assert.IsTrue(Huddle.Lock(w, Qb, offense));
            Assert.IsTrue(Huddle.Lock(w, Mlb, defense));
            Assert.AreEqual(Phase.Set, w.Phase);
            return w;
        }

        // Where a team's spot stands now, as [back, z] from the line on its own side.
        static float[] At(World w, int team, int spot)
        {
            var p = LivePlay.SpotOf(w, team, spot);
            float own = team == w.Offense ? -w.Dir : w.Dir;
            return new[] { (p.X - w.LineOfScrimmageX) * own, p.Z };
        }

        [Test]
        public void EverySetFitsTheBaseLineUpAndEveryPlayHasOne()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("formations"));
            foreach (var kv in cfg.formation.sets)
            {
                var set = kv.Value;
                Assert.That(set.side, Is.EqualTo("offense").Or.EqualTo("defense"), kv.Key);
                Assert.IsNotEmpty(set.name, kv.Key);
                bool offense = set.side == "offense";
                Assert.AreEqual((offense ? cfg.formation.offense : cfg.formation.defense).Count, set.spots.Count, kv.Key + ": one spot per player");
                for (int i = 0; i < set.spots.Count; i++)
                {
                    Assert.AreEqual(2, set.spots[i].Length, kv.Key + " spot " + i + " is [back, z]");
                    Assert.GreaterOrEqual(set.spots[i][0], 1f, kv.Key + " spot " + i + " stays on its own side");
                    Assert.LessOrEqual(System.Math.Abs(set.spots[i][1]), cfg.field.HalfWidth - 2f, kv.Key + " spot " + i + " is in bounds");
                    for (int j = 0; j < i; j++)
                    {
                        float dx = set.spots[i][0] - set.spots[j][0], dz = set.spots[i][1] - set.spots[j][1];
                        Assert.GreaterOrEqual(dx * dx + dz * dz, 1f, kv.Key + ": spots " + j + " and " + i + " stand on each other");
                    }
                }
            }
            foreach (var p in cfg.plays)
            {
                Assert.IsNotEmpty(p.formation, p.id + " names its formation");
                Assert.IsTrue(cfg.formation.sets.ContainsKey(p.formation), p.id + ": unknown formation " + p.formation);
                Assert.AreEqual(p.side, cfg.formation.sets[p.formation].side, p.id + ": formation for the other side");
            }
        }

        [Test]
        public void TheOffenseHasManyFormationsAndTheDefenseToo()
        {
            var cfg = ConfigTests.Load();
            var off = new HashSet<string>(); var def = new HashSet<string>();
            foreach (var p in cfg.plays) (p.side == "offense" ? off : def).Add(p.formation);
            Assert.GreaterOrEqual(off.Count, 5);
            Assert.GreaterOrEqual(def.Count, 5);
        }

        [Test]
        public void TheCalledPlayPicksWhereTheTeamLinesUp()
        {
            var dive = Called("dive", "goalline");
            var verts = Called("verts", "prevent");
            // I-Form: the QB under center, the RB 7 deep behind him. Spread: the QB in the gun and the TE split out wide.
            Assert.AreEqual(2f, At(dive, dive.Offense, 0)[0], 1e-3f);
            Assert.AreEqual(7f, At(dive, dive.Offense, 1)[0], 1e-3f);
            Assert.AreEqual(5f, At(verts, verts.Offense, 0)[0], 1e-3f);
            Assert.AreEqual(-12f, At(verts, verts.Offense, 5)[1], 1e-3f);
            // Goal line: corners pressed up close. Prevent: the safeties 20 deep.
            Assert.AreEqual(1.5f, At(dive, dive.Defense, 1)[0], 1e-3f);
            Assert.AreEqual(20f, At(verts, verts.Defense, 3)[0], 1e-3f);
            // the ball is with the QB wherever he stands
            Assert.AreEqual(LivePlay.Qb(dive).Slot, dive.Ball.Carrier);
        }

        [Test]
        public void TheHuddleIsTheBaseLineUp()
        {
            var w = WorldTests.Full(ConfigTests.Load());
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            var b = w.Cfg.formation.offense[1];
            Assert.AreEqual(b.back, At(w, w.Offense, 1)[0], 1e-3f);
            Assert.AreSame(w.Cfg.formation.offense, w.Spots(w.Offense));
        }

        [Test]
        public void NoFormationsKeepsTheBaseLineUp()
        {
            var w = Called("dive", "goalline", new Dictionary<string, string> { { "NO_FORMATIONS", "1" } });
            Assert.AreEqual(w.Cfg.formation.offense[1].back, At(w, w.Offense, 1)[0], 1e-3f);
            Assert.AreEqual(w.Cfg.formation.defense[1].back, At(w, w.Defense, 1)[0], 1e-3f);
        }

        [Test]
        public void AnAudibleShiftsIntoTheNewFormation()
        {
            var w = Called("dive", "cover2");
            Assert.AreEqual(7f, At(w, w.Offense, 1)[0], 1e-3f);
            Assert.IsTrue(Audibles.Call(w, Qb, "verts"));
            Assert.AreEqual(-12f, At(w, w.Offense, 5)[1], 1e-3f, "the TE splits out for Spread");
            Assert.AreEqual(5f, At(w, w.Offense, 0)[0], 1e-3f, "the QB backs into the gun");
            Assert.AreEqual(LivePlay.Qb(w).Slot, w.Ball.Carrier);
        }

        // Routes start from the spot the formation gives: the first point of every receiver's route is past his spot
        // (forward or across), never behind it, so the playbook picture and the field agree.
        [Test]
        public void RoutesLeaveFromTheFormationSpot()
        {
            var cfg = ConfigTests.Load();
            foreach (var p in cfg.plays)
            {
                if (p.side != "offense") continue;
                var spots = Formation.For(cfg, p, true);
                Assert.IsNotNull(spots, p.id);
                foreach (int r in new[] { 2, 3 })   // the wide receivers
                {
                    var j = p.jobs[r];
                    if (j.kind != "route") continue;
                    Assert.Less(System.Math.Abs(j.pts[0][1] - spots[r].z), 6f, p.id + ": spot " + r + " starts its route near where he lines up");
                }
            }
        }
    }
}
