using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // Every play file follows docs/PLAYS_FORMAT.md.
    public sealed class PlaysTests
    {
        static readonly string[] Kinds = { "route", "block", "zone", "man", "rush" };

        static IEnumerable<string> Ids()
        {
            foreach (var p in ConfigTests.Load().plays) yield return p.id;
        }

        [Test]
        public void TheM1PlaysAreThere()
        {
            var ids = new List<string>(Ids());
            foreach (var id in new[] { "dive", "slants", "cover3", "mblitz" }) CollectionAssert.Contains(ids, id);
            CollectionAssert.AllItemsAreUnique(ids);
            var sorted = new List<string>(ids);
            sorted.Sort(string.CompareOrdinal);
            CollectionAssert.AreEqual(sorted, ids, "plays are sorted by id");
        }

        [TestCaseSource(nameof(Ids))]
        public void PlayIsWellFormed(string id)
        {
            var cfg = ConfigTests.Load();
            var p = cfg.plays.Find(x => x.id == id);
            StringAssert.IsMatch("^[a-z0-9_]+$", p.id);
            Assert.IsNotEmpty(p.name);
            Assert.That(p.side, Is.EqualTo("offense").Or.EqualTo("defense"));
            int spots = (p.side == "offense" ? cfg.formation.offense : cfg.formation.defense).Count;
            Assert.AreEqual(spots, p.jobs.Count, "one job per formation spot");
            for (int i = 0; i < p.jobs.Count; i++)
            {
                var j = p.jobs[i];
                string at = p.id + " job " + i + ": ";
                CollectionAssert.Contains(Kinds, j.kind, at + "unknown kind");
                Assert.IsNotEmpty(j.text, at + "no text");
                if (j.kind == "route" || j.kind == "block" || j.kind == "zone") Assert.IsNotEmpty(j.pts, at + "needs points");
                if (j.kind == "zone") Assert.Greater(j.radius, 0f, at + "zone needs a radius");
                if (j.kind == "man")
                {
                    Assert.AreEqual("defense", p.side, at + "only the defense covers a man");
                    Assert.That(j.target, Is.InRange(0, cfg.formation.offense.Count - 1), at + "target is an offense spot");
                }
                foreach (var pt in j.pts)
                {
                    Assert.AreEqual(2, pt.Length, at + "points are [ahead, z]");
                    Assert.LessOrEqual(Math.Abs(pt[1]), cfg.field.HalfWidth, at + "point off the field");
                }
            }
        }

        [Test]
        public void BothSidesHavePlays()
        {
            var w = WorldTests.Full(ConfigTests.Load());
            Assert.IsNotEmpty(Huddle.Playbook(w, w.Offense));
            Assert.IsNotEmpty(Huddle.Playbook(w, w.Defense));
            foreach (var p in Huddle.Playbook(w, w.Offense)) Assert.AreEqual("offense", p.side);
            foreach (var p in Huddle.Playbook(w, w.Defense)) Assert.AreEqual("defense", p.side);
        }
    }
}
