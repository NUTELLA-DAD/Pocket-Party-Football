using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // WS-15: the game log and tools/playtest-report.ps1. Done when a 10-minute bot game writes a log and the report
    // lists every play. (PlayMode, because the log lives in the Boot assembly; the first test needs no scene.)
    public sealed class GameLogTests
    {
        static GameConfig Load()
        {
            var cfg = ConfigLoader.Parse(File.ReadAllText("Assets/_Project/Config/game.json"), k => null);
            var plays = new List<string>();
            foreach (var f in Directory.GetFiles("Assets/_Project/Config/plays", "*.json")) plays.Add(File.ReadAllText(f));
            ConfigLoader.AddPlays(cfg, plays);
            return cfg;
        }

        static string[] Fields(string line) => line.Split('\t');
        static IEnumerable<string[]> Rows(string log, string what)
        {
            foreach (var l in log.Split('\n'))
            {
                if (l.Length == 0 || l[0] == '#') continue;
                var f = Fields(l.TrimEnd('\r'));
                if (f.Length >= 3 && f[2] == what) yield return f;
            }
        }
        static string Get(string[] f, string key)
        {
            for (int i = 3; i < f.Length; i++) if (f[i].StartsWith(key + "=")) return f[i].Substring(key.Length + 1);
            return null;
        }

        [Test]
        public void TenMinuteBotGameLogsEveryPlayAndTheReportListsThem()
        {
            var cfg = Load();
            var w = new World(cfg, 1);
            for (int i = 0; i < cfg.players.perTeam * cfg.teams.Count; i++) w.AddPlayer(100 + i, i % cfg.teams.Count, false);
            // one person joins the defense and never touches the phone: a bot plays for them
            var roster = new Roster(cfg.net.maxPlayers);
            var dana = roster.JoinPhone(1, "Dana", "", "dana", out _);
            var sw = new StringWriter();
            var log = new GameLogWriter(sw, cfg, s => roster[s]?.Name);
            log.Header("2026-10-26 20:00:00", 1, "test", "");
            log.Observe(w, roster, 0f);
            dana.Team = w.Defense;
            Assert.IsNotNull(w.ReplaceBot(dana.Team, dana.Slot));

            int ticks = 10 * 60 * cfg.sim.tickHz;
            for (int t = 0; t < ticks; t++)
            {
                w.Step(null);
                if (t % 2 == 0 || t == ticks - 1) log.Observe(w, roster, w.Tick / (float)cfg.sim.tickHz);   // the TV reads a frame at a time
            }
            log.End(w, w.Tick / (float)cfg.sim.tickHz);
            string text = sw.ToString();

            var plays = new List<string[]>(Rows(text, "PLAY"));
            TestContext.WriteLine(w.PlaysRun + " plays, " + w.YardsGained + " yards");
            Assert.GreaterOrEqual(w.PlaysRun, 20, "a 10-minute bot game has plays");
            Assert.AreEqual(w.PlaysRun, plays.Count, "one PLAY line a play");
            int yards = 0;
            for (int i = 0; i < plays.Count; i++)
            {
                Assert.AreEqual((i + 1).ToString(), Get(plays[i], "n"), "plays numbered in order");
                Assert.IsNotEmpty(Get(plays[i], "result"));
                Assert.IsNotEmpty(Get(plays[i], "call"), "the offense's call is in the log");
                yards += int.Parse(Get(plays[i], "yards"));
            }
            Assert.AreEqual(w.YardsGained, yards, "the plays' yards add up to the sim's own");

            foreach (EventKind k in System.Enum.GetValues(typeof(EventKind)))
                Assert.AreEqual(w.Events.Total(k), new List<string[]>(Rows(text, GameLogWriter.ToWord(k))).Count, "one line per " + k + " event");
            Assert.AreEqual(w.Events.Total(EventKind.Whistle), plays.Count, "a PLAY line for every whistle");
            Assert.IsEmpty(new List<string[]>(Rows(text, "LOST")), "no events missed");
            Assert.IsNotEmpty(new List<string[]>(Rows(text, "JOIN")), "the join");
            Assert.IsNotEmpty(new List<string[]>(Rows(text, "TEAM")), "the team pick");
            var bots = new List<string[]>(Rows(text, "BOT"));
            Assert.IsNotEmpty(bots, "a bot took over for the idle person");
            Assert.AreEqual("DANA", Get(bots[0], "name"));
            Assert.AreEqual(1, new List<string[]>(Rows(text, "END")).Count);

            // the report, the way the laptop runs it
            string dir = Path.GetFullPath(Path.Combine(".logs", "games"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "test-bot-game.txt"), report = Path.Combine(dir, "test-bot-game-report.md");
            File.WriteAllText(file, text);
            if (File.Exists(report)) File.Delete(report);
            string shell = Application.platform == RuntimePlatform.WindowsEditor ? "powershell.exe" : "pwsh";
            var psi = new ProcessStartInfo(shell, "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.GetFullPath("tools/playtest-report.ps1") + "\" -Log \"" + file + "\" -Out \"" + report + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            Process proc;
            try { proc = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { Assert.Ignore("no " + shell + " here to run the report"); return; }
            string stdout = proc.StandardOutput.ReadToEnd(), stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            TestContext.WriteLine(stdout);
            Assert.AreEqual(0, proc.ExitCode, "playtest-report.ps1 failed: " + stderr);
            Assert.IsTrue(File.Exists(report), "the report is saved next to the log");
            string md = File.ReadAllText(report);
            Assert.AreEqual(w.PlaysRun, Regex.Matches(md, @"^\| \d+ \|", RegexOptions.Multiline).Count, "the report lists every play");
            StringAssert.Contains("Plays: " + w.PlaysRun + ", " + w.YardsGained + " yards", md);
            StringAssert.Contains("DANA", md, "who got a bot");
        }

        [SetUp] public void Loopback() { PartyHost.BindOverride = System.Net.IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Time.timeScale = 1f; }

        // The real game writes the file: a phone joins, the bots play to a whistle, both are in the log.
        [UnityTest]
        public IEnumerator TheGameWritesALogNextToItself()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            Assert.IsTrue(host.World.Cfg.Feature("gamelog"), "FEATURES.gamelog is on in game.json");
            Assert.IsNotNull(GameLog.Path, "a log file was opened");
            StringAssert.Contains(".logs", GameLog.Path, "in the editor the log goes to .logs/games");

            var phone = new FakePhone();
            yield return phone.JoinAndPick(host.Server.Port, host.RoomCode, "Kay", host.World.Defense);
            int plays = host.World.PlaysRun;
            Time.timeScale = 4f;
            float giveUp = Time.realtimeSinceStartup + 120f;
            while (host.World.PlaysRun == plays && Time.realtimeSinceStartup < giveUp) yield return null;
            Time.timeScale = 1f;
            yield return null;
            phone.Dispose();
            Assert.Greater(host.World.PlaysRun, plays, "a play within two minutes of bot football");

            string text;
            using (var fs = new FileStream(GameLog.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs)) text = r.ReadToEnd();
            var joins = new List<string[]>(Rows(text, "JOIN"));
            Assert.IsNotEmpty(joins, "the join is logged");
            Assert.AreEqual("KAY", Get(joins[0], "name"));
            Assert.IsNotEmpty(new List<string[]>(Rows(text, "TEAM")), "the team pick is logged");
            Assert.IsNotEmpty(new List<string[]>(Rows(text, "HIKE")), "the snap is logged");
            Assert.IsNotEmpty(new List<string[]>(Rows(text, "PLAY")), "the play is logged");
        }
    }
}
