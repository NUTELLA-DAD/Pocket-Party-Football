using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.season (Sim/Season.cs): a club's season of recruits, coins, boosts and coaching; FEATURES.jerseyNames
    // (View/JerseyNames.cs): their surnames across their shoulders.
    public sealed class SeasonTests
    {
        static GameConfig Cfg() => ConfigTests.Load(ConfigTests.NoHuddle);

        static string Club(GameConfig cfg) => cfg.teams[0].@short;

        static PlayerLine Line(int recruit, int tds = 0, int picks = 0, int sacks = 0, int yards = 0) =>
            new PlayerLine { Team = 0, Recruit = recruit, Tds = tds, Picks = picks, Sacks = sacks, RushYd = yards };

        [Test]
        public void TheSwitchesAndTheirEnvOverrides()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("season"), "FEATURES.season is on in game.json");
            Assert.IsTrue(cfg.Feature("jerseyNames"), "FEATURES.jerseyNames is on in game.json");
            Assert.AreEqual("NO_SEASON", ConfigLoader.EnvName("season"));
            Assert.AreEqual("NO_JERSEY_NAMES", ConfigLoader.EnvName("jerseyNames"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_SEASON", "1" } }).Feature("season"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_JERSEY_NAMES", "1" } }).Feature("jerseyNames"));
            Assert.IsTrue(Season.On(cfg));
        }

        [Test]
        public void GameJsonsSeasonHangsTogether()
        {
            var cfg = ConfigTests.Load();
            var c = cfg.season;
            Assert.Greater(c.startCoins, 0);
            Assert.Greater(c.coinsWin, c.coinsLoss, "a win pays more than a loss");
            Assert.Greater(c.offers, 0);
            Assert.GreaterOrEqual(c.firstNames.Count, 20, "enough first names");
            Assert.GreaterOrEqual(c.lastNames.Count, 20, "enough surnames");
            foreach (var line in new[] { Season.PlaybookLine, Season.RecoveryLine, Season.SprintLine })
            {
                Assert.IsTrue(c.coaching.ContainsKey(line), "coaching line " + line);
                Assert.Greater(c.coaching[line].costs.Count, 0, line + " has levels to buy");
            }
            var ids = new HashSet<string>();
            foreach (var p in cfg.plays) ids.Add(p.id);
            foreach (var level in c.coaching[Season.PlaybookLine].plays)
                foreach (var id in level) Assert.IsTrue(ids.Contains(id), "playbook play " + id + " exists in Config/plays");
            Assert.Greater(c.coaching[Season.RecoveryLine].perLevel, 0f);
            Assert.Greater(c.coaching[Season.SprintLine].perLevel, 0f);
            Assert.Greater(c.jerseyName.letterCm, 0f);
            Assert.Greater(c.jerseyName.maxWidthCm, 0f);
            // every letter in the name pools can be painted on a jersey
            foreach (var name in c.firstNames) foreach (char ch in name) Assert.IsTrue(Glyphs.Has(ch), name + ": no glyph for '" + ch + "'");
            foreach (var name in c.lastNames) foreach (char ch in name) Assert.IsTrue(Glyphs.Has(ch), name + ": no glyph for '" + ch + "'");
            Assert.IsFalse(string.IsNullOrEmpty(c.walkOn));
        }

        [Test]
        public void ANewSeasonHasAScheduleAndAnEleven()
        {
            var cfg = Cfg();
            var s = Season.Start(cfg, Club(cfg), 7);
            Assert.IsNotNull(s);
            Assert.AreEqual(Club(cfg), s.club);
            int others = TeamPick.League(cfg).Count - 1;
            Assert.AreEqual(others, s.opponents.Count, "weeks 0: every other team once");
            Assert.AreEqual(others, new HashSet<string>(s.opponents).Count, "each of them once");
            Assert.IsFalse(s.opponents.Contains(s.club));
            Assert.AreEqual(cfg.players.perTeam, s.lineup.Count);
            int max = Season.StatMax(cfg);
            var surnames = new HashSet<string>();
            foreach (var r in s.lineup)
            {
                Assert.IsNotNull(r, "a drafted eleven: no walk-ons yet");
                Assert.IsTrue(r.Rated);
                Assert.That(r.speed, Is.InRange(1, max)); Assert.That(r.size, Is.InRange(1, max)); Assert.That(r.hands, Is.InRange(1, max));
                Assert.IsTrue(r.name.Contains(" "), r.name + " has a first name and a surname");
                Assert.AreEqual(r.name.Substring(r.name.LastIndexOf(' ') + 1), r.Surname);
                Assert.That(r.skin, Is.InRange(0, cfg.marks.skinTones.Count - 1));
                Assert.That(r.hair, Is.InRange(0, cfg.marks.hairColors.Count - 1));
                surnames.Add(r.Surname);
            }
            Assert.AreEqual(s.lineup.Count, surnames.Count, "no two recruits share a surname");
            Assert.AreEqual(cfg.season.offers, s.offers.Count);
            Assert.AreEqual(cfg.season.startCoins, s.coins);
            foreach (var kv in cfg.season.coaching) Assert.AreEqual(0, s.Level(kv.Key));
            var next = s.Next();
            Assert.AreEqual(0, next.Week);
            Assert.IsTrue(next.ClubHome, "the first game is at home");
            Assert.AreEqual(s.club, next.Home);
            Assert.AreEqual(s.opponents[0], next.Away);
            Assert.AreEqual(Season.Start(cfg, Club(cfg), 7).Save(), s.Save(), "the same seed draws the same season");
            Assert.AreNotEqual(Season.Start(cfg, Club(cfg), 8).Save(), s.Save());
            Assert.IsNull(Season.Start(cfg, "ZZZ", 1), "no such club");
        }

        [Test]
        public void SaveAndLoadRoundTrip()
        {
            var cfg = Cfg();
            var s = Season.Start(cfg, Club(cfg), 3);
            s.Record(cfg, 21, 14, new[] { Line(0, tds: 2) });
            s.Boost(cfg, 1, Season.Speed);
            string text = s.Save();
            var back = Season.Load(text, cfg);
            Assert.IsNotNull(back);
            Assert.AreEqual(text, back.Save());
            Assert.AreEqual(s.coins, back.coins);
            Assert.AreEqual(s.lineup[1].speed, back.lineup[1].speed);
            Assert.AreEqual(1, back.week);
            Assert.IsNull(Season.Load("not a season", cfg));
            Assert.IsNull(Season.Load("", cfg));
            Assert.IsNull(Season.Load(text.Replace("\"club\": \"" + s.club + "\"", "\"club\": \"ZZZ\""), cfg), "a club not in the league");
            Assert.IsNull(Season.Load(text.Replace("\"week\": 1", "\"week\": 5"), cfg), "a week the scores don't account for");
        }

        [Test]
        public void GamesGoIntoTheRecordAndPayCoins()
        {
            var cfg = Cfg();
            var c = cfg.season;
            var s = Season.Start(cfg, Club(cfg), 5);
            int coins = s.coins;
            int earned = s.Record(cfg, 21, 14, new[] { Line(0, tds: 3, yards: 80), Line(4, picks: 1, sacks: 2) });
            Assert.AreEqual(c.coinsWin + 3 * c.coinsPerTd + c.coinsPerPick + 2 * c.coinsPerSack, earned);
            Assert.AreEqual(coins + earned, s.coins);
            Assert.AreEqual(1, s.week);
            Assert.AreEqual("1-0", s.RecordText);
            Assert.AreEqual(3, s.lineup[0].tds); Assert.AreEqual(80, s.lineup[0].yards);
            Assert.AreEqual(1, s.lineup[4].picks); Assert.AreEqual(2, s.lineup[4].sacks);
            foreach (var r in s.lineup) Assert.AreEqual(1, r.games);
            Assert.IsFalse(s.Next().ClubHome, "then away");
            Assert.AreEqual(c.coinsLoss, s.Record(cfg, 7, 10, null));
            Assert.AreEqual("1-1", s.RecordText);
            Assert.AreEqual(c.coinsTie, s.Record(cfg, 7, 7, new PlayerLine[0]));
            Assert.AreEqual("1-1-1", s.RecordText);
            while (!s.Over) s.Record(cfg, 14, 0, null);
            Assert.IsNull(s.Next());
            Assert.AreEqual(0, s.Record(cfg, 1, 0, null), "nothing to record once the season is over");
            var ids = new List<int>(); foreach (var r in s.lineup) ids.Add(r.id);
            Assert.IsTrue(s.NextSeason(cfg));
            Assert.AreEqual(1, s.seasons);
            Assert.AreEqual(1, s.history.Count);
            Assert.IsTrue(s.history[0].StartsWith("SEASON 1: "));
            Assert.AreEqual(0, s.week);
            Assert.IsFalse(s.Over);
            Assert.AreEqual(0, s.scores.Count);
            for (int i = 0; i < ids.Count; i++) Assert.AreEqual(ids[i], s.lineup[i].id, "the eleven carry over");
            Assert.IsFalse(s.NextSeason(cfg), "only once the season is over");
        }

        [Test]
        public void TheFrontOfficeSpendsCoins()
        {
            var cfg = Cfg();
            var c = cfg.season;
            var s = Season.Start(cfg, Club(cfg), 11);
            // signing: free up to signFree points, then signCostPerPoint a point
            var offer = s.offers[0];
            Assert.AreEqual(Math.Max(0, offer.Total - c.signFree) * c.signCostPerPoint, s.SignCost(cfg, offer));
            int spot = s.BestSpot(cfg, offer);
            Assert.That(spot, Is.InRange(0, s.lineup.Count - 1));
            s.coins = 0;
            if (s.SignCost(cfg, offer) > 0) Assert.IsFalse(s.Sign(cfg, 0, spot), "no coins, no signing");
            s.coins = 100000;
            int before = s.coins;
            Assert.IsTrue(s.Sign(cfg, 0, spot));
            Assert.AreSame(offer, s.lineup[spot]);
            Assert.AreEqual(before - s.SignCost(cfg, offer), s.coins);
            Assert.AreEqual(c.offers, s.offers.Count, "a new face took his place on the table");
            Assert.AreNotSame(offer, s.offers[0]);
            Assert.IsFalse(s.Sign(cfg, 99, spot));
            // cutting
            Assert.IsTrue(s.Cut(spot));
            Assert.IsNull(s.lineup[spot]);
            Assert.IsFalse(s.Cut(spot), "nobody there to cut");
            Assert.AreEqual(-1, s.BoostCost(cfg, null, Season.Speed), "a walk-on can't be boosted");
            Assert.IsFalse(s.Boost(cfg, spot, Season.Speed));
            // boosting: base, then more each time, up to the ceiling
            int other = spot == 0 ? 1 : 0;
            var r = s.lineup[other];
            int max = Season.StatMax(cfg);
            r.SetStat(Season.Hands, 1); r.boosts = 0;
            Assert.AreEqual(c.boostCostBase, s.BoostCost(cfg, r, Season.Hands));
            before = s.coins;
            Assert.IsTrue(s.Boost(cfg, other, Season.Hands));
            Assert.AreEqual(2, r.hands);
            Assert.AreEqual(before - c.boostCostBase, s.coins);
            Assert.AreEqual(c.boostCostBase + c.boostCostStep, s.BoostCost(cfg, r, Season.Hands), "the second boost costs more");
            Assert.AreEqual(2, r.Rating().hands, "the sim sees the new number");
            r.SetStat(Season.Hands, max);
            Assert.AreEqual(-1, s.BoostCost(cfg, r, Season.Hands));
            Assert.IsFalse(s.Boost(cfg, other, Season.Hands));
            s.coins = 0;
            r.SetStat(Season.Hands, 1);
            Assert.IsFalse(s.Boost(cfg, other, Season.Hands), "no coins, no boost");
            // coaching
            s.coins = 100000;
            var line = c.coaching[Season.RecoveryLine];
            Assert.AreEqual(line.costs[0], s.UpgradeCost(cfg, Season.RecoveryLine));
            Assert.IsTrue(s.Upgrade(cfg, Season.RecoveryLine));
            Assert.AreEqual(1, s.Level(Season.RecoveryLine));
            while (s.UpgradeCost(cfg, Season.RecoveryLine) >= 0) Assert.IsTrue(s.Upgrade(cfg, Season.RecoveryLine));
            Assert.AreEqual(line.costs.Count, s.Level(Season.RecoveryLine));
            Assert.IsFalse(s.Upgrade(cfg, Season.RecoveryLine), "maxed");
            Assert.AreEqual(-1, s.UpgradeCost(cfg, "no such line"));
            // scouting
            var table = new List<Recruit>(s.offers);
            before = s.coins;
            Assert.IsTrue(s.Scout(cfg));
            Assert.AreEqual(before - c.scoutCost, s.coins);
            Assert.AreEqual(c.offers, s.offers.Count);
            for (int i = 0; i < table.Count; i++) Assert.AreNotSame(table[i], s.offers[i]);
            s.coins = c.scoutCost - 1;
            Assert.IsFalse(s.Scout(cfg));
        }

        // A lone player on a spot, wearing (or not) the club's recruit there, sprinting flat out for a second.
        static Player Sprinter(GameConfig cfg, Season s, int team, int spot, bool wear)
        {
            var w = new World(cfg, 1);
            w.Season = s.Live(cfg);
            var p = w.AddPlayer(0, team, true, spot);
            if (wear) w.SetRecruit(p, spot);
            p.X = 0; p.Z = 0;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f, true));
            return p;
        }

        static float Speed(Player p) => (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);

        [Test]
        public void ARecruitPlaysOnHisOwnNumbers()
        {
            var cfg = Cfg();
            cfg.features["ratings"] = true;
            var s = Season.Start(cfg, Club(cfg), 2);
            int max = Season.StatMax(cfg);
            s.lineup[1].SetStat(Season.Speed, max);
            float quick = Speed(Sprinter(cfg, s, 0, 1, true));
            s.lineup[1].SetStat(Season.Speed, 1);
            float slow = Speed(Sprinter(cfg, s, 0, 1, true));
            float plain = Speed(Sprinter(cfg, s, 0, 1, false));
            Assert.Greater(quick, plain, "a 10 SPEED recruit beats the position's own number");
            Assert.Less(slow, plain, "a 1 SPEED recruit trails it");
            // the visitors wear names but play on their positions' numbers
            var visitor = Sprinter(cfg, s, 1, 1, true);
            Assert.AreEqual(1, visitor.Recruit);
            Assert.AreEqual(Speed(Sprinter(cfg, s, 1, 1, false)), Speed(visitor), 1e-4f);
            var live = s.Live(cfg);
            Assert.AreEqual(0, live.Club);
            Assert.IsNotNull(live.Roster[1][1]);
            Assert.IsFalse(live.Roster[1][1].Rated);
            Assert.IsFalse(string.IsNullOrEmpty(live.Roster[1][1].name));
            // a cut spot: a walk-on, the position's numbers
            s.Cut(1);
            Assert.AreEqual(plain, Speed(Sprinter(cfg, s, 0, 1, true)), 1e-4f);
        }

        [Test]
        public void CoachingLengthensSprintsAndSpeedsRecovery()
        {
            var cfg = Cfg();
            var s = Season.Start(cfg, Club(cfg), 4);
            float plain = Sprinter(cfg, s, 0, 2, true).Stamina;
            float visitor = Sprinter(cfg, s, 1, 2, true).Stamina;
            s.coaching[Season.SprintLine] = Season.MaxLevel(cfg, Season.SprintLine);
            float coached = Sprinter(cfg, s, 0, 2, true).Stamina;
            Assert.Greater(coached, plain, "SPRINT coaching: less stamina spent in a second of sprinting");
            Assert.AreEqual(visitor, Sprinter(cfg, s, 1, 2, true).Stamina, 1e-5f, "the visitors get no coaching");
            // recovery: a tired man standing still for a second
            float Rest(int level)
            {
                s.coaching[Season.RecoveryLine] = level;
                var w = new World(cfg, 1);
                w.Season = s.Live(cfg);
                var p = w.AddPlayer(0, 0, true, 2);
                w.SetRecruit(p, 2);
                p.Stamina = 0.2f;
                for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 0f, 0f));
                return p.Stamina;
            }
            float slow = Rest(0), fast = Rest(Season.MaxLevel(cfg, Season.RecoveryLine));
            Assert.Greater(fast, slow, "RECOVERY coaching: stamina comes back faster");
            Assert.AreEqual(1f, SeasonRules.Recovery(new World(cfg, 1), null), "no season: as it was");
        }

        [Test]
        public void ThePlaybookGrowsWithTheCoaches()
        {
            var cfg = Cfg();
            var s = Season.Start(cfg, Club(cfg), 6);
            var plain = new World(cfg, 1);
            int offense = plain.Offense, defense = plain.Defense;
            int fullOff = Huddle.Playbook(plain, offense).Count, fullDef = Huddle.Playbook(plain, defense).Count;
            var levels = cfg.season.coaching[Season.PlaybookLine].plays;
            var seen = new List<int>();
            for (int level = 0; level <= Season.MaxLevel(cfg, Season.PlaybookLine); level++)
            {
                s.coaching[Season.PlaybookLine] = level;
                var w = new World(cfg, 1);
                w.Season = s.Live(cfg);
                var allowed = new HashSet<string>();
                for (int i = 0; i <= level && i < levels.Count; i++) allowed.UnionWith(levels[i]);
                var book = Huddle.Playbook(w, w.Season.Club);
                foreach (var p in book) Assert.IsTrue(allowed.Contains(p.id) || Kicking.IsKickPlay(w, p), "level " + level + " may not call " + p.id);
                seen.Add(book.Count);
                // the other team calls everything
                int other = w.Season.Club == offense ? defense : offense;
                Assert.AreEqual(other == offense ? fullOff : fullDef, Huddle.Playbook(w, other).Count);
            }
            for (int i = 1; i < seen.Count; i++) Assert.Greater(seen[i], seen[i - 1], "each level adds plays");
            Assert.AreEqual(fullOff, seen[seen.Count - 1], "the top level is the whole book");
            Assert.IsTrue(SeasonRules.InBook(plain, 0, cfg.plays[0]), "no season: everything");
        }

        [Test]
        public void ARecruitStaysWithHisSpotWhenPeopleComeAndGo()
        {
            var cfg = Cfg();
            var s = Season.Start(cfg, Club(cfg), 9);
            var w = WorldTests.Full(cfg);
            w.Season = s.Live(cfg);
            ulong bare = w.Hash();
            foreach (var p in w.Players) w.SetRecruit(p, p.Spot);
            foreach (var p in w.Players) Assert.AreEqual(p.Spot, p.Recruit);
            Assert.AreNotEqual(bare, w.Hash(), "the recruits are part of the hash in a season game");
            // a person takes the lowest bot's place: the recruit comes with it
            Player lowest = null;
            foreach (var p in w.Players) if (p.Team == 0 && (lowest == null || p.Spot < lowest.Spot)) lowest = p;
            int recruit = lowest.Recruit, spot = lowest.Spot;
            var human = w.ReplaceBot(0, 500);
            Assert.AreEqual(recruit, human.Recruit);
            Assert.AreSame(s.lineup[recruit], SeasonRules.RecruitOf(w, human));
            Assert.AreEqual(s.lineup[recruit].name, SeasonRules.NameOf(w, human));
            // the person moves to the other team: he wears that bot's recruit, and the one he left goes back on the spot
            Player lowest1 = null;
            foreach (var p in w.Players) if (p.Team == 1 && !p.Human && (lowest1 == null || p.Spot < lowest1.Spot)) lowest1 = p;
            int theirs = lowest1.Recruit;
            var moved = w.ReplaceBot(1, 500);
            Assert.AreEqual(theirs, moved.Recruit);
            var back = w.AddPlayer(600, 0, false, spot, recruit);
            Assert.AreEqual(recruit, back.Recruit);
            Assert.AreSame(s.lineup[recruit], SeasonRules.RecruitOf(w, back));
            // a player who changes teams by AddPlayer leaves his recruit behind
            var w3 = new World(cfg, 1);
            w3.Season = s.Live(cfg);
            var a = w3.AddPlayer(0, 0, true, 0, 0);
            Assert.AreEqual(0, a.Recruit);
            Assert.AreSame(a, w3.AddPlayer(0, 1, true));
            Assert.AreEqual(-1, a.Recruit);
            Assert.AreSame(a, w3.AddPlayer(0, 1, true, -1, 3));
            Assert.AreEqual(3, a.Recruit, "the same team: a recruit can be put on him");
            w3.SetRecruit(a, 99);
            Assert.AreEqual(-1, a.Recruit, "no such recruit");
            // without a season nothing changes
            var w2 = WorldTests.Full(cfg);
            Assert.IsNull(SeasonRules.RecruitOf(w2, w2.Players[0]));
            Assert.IsNull(SeasonRules.NameOf(w2, w2.Players[0]));
            Assert.AreEqual(-1, w2.ReplaceBot(0, 500).Recruit);
        }

        [Test]
        public void EveryLetterHasAGlyphAndANameFitsTheBack()
        {
            for (char ch = 'A'; ch <= 'Z'; ch++) Assert.IsTrue(Glyphs.Has(ch), "glyph " + ch);
            Assert.IsTrue(Glyphs.Has('-'));
            Assert.Greater(Glyphs.Of('W').Length, 0);
            var cfg = ConfigTests.Load();
            var jc = cfg.season.jerseyName;
            float pen = jc.penFrac * jc.letterCm;
            foreach (var name in new[] { "FOX", "OKAFOR", "BRANNIGAN", "UNDERWOOD", "LINDQVIST" })
            {
                var mesh = JerseyNames.Build(name, jc, Color.white);
                Assert.IsNotNull(mesh, name);
                Assert.Greater(mesh.vertexCount, 0);
                var b = mesh.bounds;
                Assert.LessOrEqual(b.size.z, jc.maxWidthCm + pen, name + " fits across the shoulders");
                Assert.LessOrEqual(b.size.y, jc.letterCm + pen, name + " is letterCm tall");
                Assert.Less(Mathf.Abs(b.center.z), 0.5f, name + " is centred");
                Assert.AreEqual(0f, b.size.x, 1e-4f, "flat, in the plane of the back");
                foreach (var n in mesh.normals) Assert.AreEqual(Vector3.left, n, "faces out of the back");
            }
            Assert.IsNull(JerseyNames.Build("", jc, Color.white));
            Assert.IsNull(JerseyNames.Build("@@", jc, Color.white), "no glyphs, no mesh");
        }

        [Test]
        public void TheMenuStillFitsWithTheSeasonButton()
        {
            var cfg = ConfigTests.Load();
            var t = cfg.title;
            int items = 7;   // PLAY, TUTORIAL, COUCH CUP, SEASON, ROSTER, HOW TO PLAY, SETTINGS
            float below = t.playHeightFrac + (items - 1) * t.otherHeightFrac + (items + 1) * t.gapFrac;
            float fit = Mathf.Max(0.2f, t.menuBottomFrac - t.logoTopFrac - below);
            float end = t.logoTopFrac + Mathf.Min(t.logoHeightFrac, fit) + t.gapFrac + below;
            Assert.LessOrEqual(end, 0.955f, "seven buttons and the logo end above the hint line");
            Assert.GreaterOrEqual(t.otherHeightFrac, 0.07f, "the buttons stay readable");
        }
    }
}
