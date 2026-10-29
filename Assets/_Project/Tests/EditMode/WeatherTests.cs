using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.weather / sandField (Sim/Weather.cs): rain and snow picked on SETTINGS, the Cacti's sand field.
    public sealed class WeatherTests
    {
        static GameConfig In(string sky, Dictionary<string, string> env = null)
        {
            var e = env != null ? new Dictionary<string, string>(env) : ConfigTests.NoHuddle;
            var cfg = ConfigTests.Load(e);
            cfg.weather.now = sky;
            return cfg;
        }

        static float Speed(Player p) => (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);

        static float TopJog(GameConfig cfg)
        {
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            p.X = 0; p.Z = 0;
            for (int i = 0; i < cfg.sim.tickHz * 2; i++) w.Step(WorldTests.Stick(0, 1f, 0f));
            return Speed(p);
        }

        static int TicksToStop(GameConfig cfg)
        {
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            p.X = 0; p.Z = 0;
            for (int i = 0; i < cfg.sim.tickHz * 2; i++) w.Step(WorldTests.Stick(0, 1f, 0f));
            int n = 0;
            while (Speed(p) > 0f && n < cfg.sim.tickHz * 10) { w.Step(null); n++; }
            return n;
        }

        [Test]
        public void ShipsClearSoNothingChanges()
        {
            var cfg = ConfigTests.Load(ConfigTests.AsShipped);
            Assert.IsTrue(cfg.Feature("weather"));
            Assert.IsTrue(cfg.Feature("sandField"));
            Assert.AreEqual(Sky.Clear, Weather.Now(cfg));
            Assert.IsFalse(Weather.Sand(cfg), "game.json's own two teams play on grass");
            Assert.IsFalse(Weather.Any(cfg));
            var w = new World(cfg, 1);
            Assert.AreEqual(1f, Weather.TopSpeed(w));
            Assert.AreEqual(1f, Weather.Accel(w));
            Assert.AreEqual(1f, Weather.Decel(w));
            Assert.AreEqual(1f, Weather.Fumble(w));
            Assert.AreEqual(1f, Weather.KickSpeed(w));
            Assert.AreEqual(0.9f, Weather.Catch(w, 0.9f));
            Assert.AreEqual(0f, Weather.SlipChance(w));
        }

        [Test]
        public void PicksStepAndRandomRolls()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual("rain", Weather.Step("clear", +1));
            Assert.AreEqual("random", Weather.Step("clear", -1));
            Assert.AreEqual("clear", Weather.Step("random", +1));
            Assert.AreEqual("rain", Weather.Step("nonsense", +1));
            Assert.AreEqual(Sky.Rain, Weather.Resolve(cfg, "RAIN", 0f));
            Assert.AreEqual(Sky.Snow, Weather.Resolve(cfg, "snow", 0f));
            Assert.AreEqual(Sky.Clear, Weather.Resolve(cfg, "hail", 0.5f));
            // RANDOM: every weather comes up for some roll
            var seen = new HashSet<Sky>();
            for (int i = 0; i < 100; i++) seen.Add(Weather.Resolve(cfg, "random", i / 100f));
            Assert.AreEqual(3, seen.Count);
        }

        [Test]
        public void RainAndSnowAreSlowerAndSlide()
        {
            float clear = TopJog(In("clear")), rain = TopJog(In("rain")), snow = TopJog(In("snow"));
            Assert.Less(rain, clear);
            Assert.Less(snow, rain);
            Assert.AreEqual(clear * In("rain").weather.rain.topSpeed, rain, 1e-3f);
            Assert.Greater(TicksToStop(In("rain")), TicksToStop(In("clear")), "a wet field: he slides on before he stops");
        }

        [Test]
        public void NoWeatherSwitchKeepsItClear()
        {
            var cfg = In("snow", new Dictionary<string, string> { { "NO_HUDDLE", "1" }, { "NO_WEATHER", "1" } });
            Assert.AreEqual(Sky.Clear, Weather.Now(cfg));
            Assert.AreEqual(TopJog(In("clear")), TopJog(cfg), 1e-5f);
        }

        [Test]
        public void AWetBallIsHarderToCatchAndHoldOnTo()
        {
            var w = new World(In("rain"), 1);
            Assert.Less(Weather.Catch(w, 0.9f), 0.9f);
            Assert.Greater(Weather.Fumble(w), 1f);
            var s = new World(In("snow"), 1);
            Assert.Less(Weather.Catch(s, 0.9f), 0.9f);
            Assert.Greater(Weather.Fumble(s), 1f);
        }

        [Test]
        public void ColdBallNeedsMorePowerAndStormWindBlowsHarder()
        {
            var env = new Dictionary<string, string> { { "NO_HUDDLE", "1" }, { "NO_KICKING", "0" } };
            var clear = new World(In("clear", env), 7);
            var snow = new World(In("snow", env), 7);
            Assert.Greater(Kicking.FieldGoalPower(snow, 25f), Kicking.FieldGoalPower(clear, 25f));
            var rain = new World(In("rain", env), 7);
            Kicking.Wind(clear, out float cx, out float cz);
            Kicking.Wind(rain, out float rx, out float rz);
            float c = (float)Math.Sqrt(cx * cx + cz * cz), r = (float)Math.Sqrt(rx * rx + rz * rz);
            if (c > 0f) Assert.Greater(r, c);
        }

        [Test]
        public void AHardCutOnAWetFieldCanSlip()
        {
            var cfg = In("rain");
            cfg.weather.rain.slipChance = 1f;
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            w.Phase = Phase.Live;
            p.Sprinting = true;
            p.VX = cfg.players.sprintSpeedYdS; p.VZ = 0f;
            Assert.IsFalse(Weather.Slip(w, p, cfg.players.sprintSpeedYdS, 0f), "straight on: no slip");
            Assert.IsTrue(Weather.Slip(w, p, -cfg.players.sprintSpeedYdS, 0f), "a hard cut back");
            Assert.Greater(p.StumbleTicks, 0);
            bool pushed = false;
            foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Slip) pushed = true;
            Assert.IsTrue(pushed);

            // the same cut on a dry field never slips
            var dry = new World(In("clear"), 1);
            var q = dry.AddPlayer(0, 0, true);
            dry.Phase = Phase.Live;
            q.Sprinting = true;
            q.VX = cfg.players.sprintSpeedYdS;
            Assert.IsFalse(Weather.Slip(dry, q, -cfg.players.sprintSpeedYdS, 0f));
        }

        [Test]
        public void CactiPlayAtHomeOnSand()
        {
            var cfg = In("clear");
            Assert.IsTrue(TeamPick.Apply(cfg, "WHL", "CAC"));
            Assert.IsTrue(Weather.Sand(cfg));
            Assert.Less(TopJog(cfg), TopJog(In("clear")));
            // away, the Cacti play on the home team's grass
            var away = In("clear");
            TeamPick.Apply(away, "CAC", "WHL");
            Assert.IsFalse(Weather.Sand(away));
            // NO_SAND_FIELD=1: grass
            var off = In("clear", new Dictionary<string, string> { { "NO_HUDDLE", "1" }, { "NO_SAND_FIELD", "1" } });
            TeamPick.Apply(off, "WHL", "CAC");
            Assert.IsFalse(Weather.Sand(off));
        }

        [Test]
        public void TheLookChangesTheSkyAndTheGrass()
        {
            var clear = In("clear");
            string grass = clear.field.grassColor;
            float fog = clear.sky.fogStartM;
            Weather.ApplyLook(clear);
            Assert.AreEqual(grass, clear.field.grassColor, "clear on grass: as game.json has it");
            Assert.AreEqual(fog, clear.sky.fogStartM);

            var rain = In("rain");
            Weather.ApplyLook(rain);
            Assert.Less(rain.sky.fogStartM, fog, "the rain closes the haze in");
            Assert.AreNotEqual(grass, rain.field.grassColor, "wet grass is darker");

            var sand = In("rain");
            TeamPick.Apply(sand, "WHL", "CAC");
            Weather.ApplyLook(sand);
            Assert.AreEqual(sand.weatherLook.sand["field"]["grassColor"].ToString(), sand.field.grassColor, "sand stays sand in the rain");
            Assert.IsFalse(sand.Feature("grassBlades"), "no grass blades on sand");
            Assert.IsNotNull(Weather.Look(sand));
        }
    }
}
