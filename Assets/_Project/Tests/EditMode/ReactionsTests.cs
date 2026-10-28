using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.faceReactions, grassStains and nameBars (View/Reactions.cs): faces after a big play, grass that builds up
    // each time a man goes down, and the name bar over a person's player.
    public sealed class ReactionsTests
    {
        static World Two(GameConfig cfg, out Player hitter, out Player runner, out Player mate)
        {
            var w = new World(cfg, 1);
            hitter = w.AddPlayer(0, 1, false);
            runner = w.AddPlayer(1, 0, false);
            mate = w.AddPlayer(2, 0, false);
            return w;
        }

        [Test]
        public void ABigHitMakesTheHitterGrinAndTheRunnerWince()
        {
            var cfg = ConfigTests.Load();
            var w = Two(cfg, out var hitter, out var runner, out var mate);
            var r = new Reactions();
            w.Events.Push(w, EventKind.Tackle, hitter, runner, "BIG HIT");
            r.Observe(w);
            Assert.AreEqual(1f, r.Mood(w, hitter.Slot), "the hitter is pleased");
            Assert.AreEqual(-1f, r.Mood(w, runner.Slot), "the runner winces");
            Assert.AreEqual(0f, r.Mood(w, mate.Slot), "nobody else");
            w.Tick += (int)(cfg.reactions.bigHitS * cfg.sim.tickHz) + 1;
            r.Observe(w);
            Assert.AreEqual(0f, r.Mood(w, runner.Slot), "and it passes");
            // a plain tackle: no faces
            w.Events.Push(w, EventKind.Tackle, hitter, runner);
            r.Observe(w);
            Assert.AreEqual(0f, r.Mood(w, runner.Slot));
            // a touchdown: the whole scoring team smiles
            w.Events.Push(w, EventKind.Touchdown, runner.Slot, runner.Team, 0f, 0f);
            r.Observe(w);
            Assert.AreEqual(1f, r.Mood(w, runner.Slot));
            Assert.AreEqual(1f, r.Mood(w, mate.Slot));
            Assert.AreEqual(0f, r.Mood(w, hitter.Slot), "not the other team");
            Assert.AreEqual("NO_FACE_REACTIONS", ConfigLoader.EnvName("faceReactions"));
        }

        [Test]
        public void GrassBuildsUpEachTimeHeGoesDown()
        {
            var cfg = ConfigTests.Load();
            var c = cfg.reactions;
            var w = Two(cfg, out var hitter, out var runner, out var mate);
            var r = new Reactions();
            w.Events.Push(w, EventKind.Tackle, hitter, runner);
            r.Observe(w);
            Assert.AreEqual(c.stainTackled, r.Stain(w, runner.Slot), 1e-5f);
            Assert.AreEqual(c.stainTackler, r.Stain(w, hitter.Slot), 1e-5f);
            Assert.AreEqual(0f, r.Stain(w, mate.Slot), "clean if he stayed up");
            for (int i = 0; i < 40; i++) w.Events.Push(w, EventKind.Tackle, hitter, runner, "BIG HIT");
            r.Observe(w);
            Assert.AreEqual(c.stainMax, r.Stain(w, runner.Slot), 1e-5f, "up to stainMax");
            // a new game starts clean
            var fresh = Two(cfg, out _, out var runner2, out _);
            r.Observe(fresh);
            Assert.AreEqual(0f, r.Stain(fresh, runner2.Slot));
            // switched off: clean
            cfg.features["grassStains"] = false;
            Assert.AreEqual(0f, r.Stain(w, runner.Slot));
            Assert.AreEqual("NO_GRASS_STAINS", ConfigLoader.EnvName("grassStains"));
        }

        [Test]
        public void ANameBarShowsWhoPlaysHim()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual("KENNY", Reactions.BarLabel("KENNY", true, false));
            Assert.AreEqual("KENNY ...", Reactions.BarLabel("KENNY", false, false), "phone away");
            Assert.AreEqual("KENNY (BOT)", Reactions.BarLabel("KENNY", true, true), "a bot plays for him");
            Assert.Less(Reactions.BarAlpha(cfg, Phase.Live), Reactions.BarAlpha(cfg, Phase.Huddle), "see-through during the play");
            Assert.Greater(Reactions.BarAlpha(cfg, Phase.Live), 0f, "but still there");
            Assert.AreEqual("NO_NAME_BARS", ConfigLoader.EnvName("nameBars"));
        }
    }
}
