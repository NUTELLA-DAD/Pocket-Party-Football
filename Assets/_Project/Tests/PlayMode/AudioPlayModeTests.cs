using System.Collections;
using System.Text;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // WS-09 in the real game: with FEATURES.audio off nothing is made and nothing plays; switched on, a bot game
    // starts every sound at least once on the TV. No errors on the way.
    public sealed class AudioPlayModeTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = System.Net.IPAddress.Loopback; PartyHost.PortOverride = 0; Lobby.HoldOverride = false; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Lobby.HoldOverride = null; Time.timeScale = 1f; }

        static string Missing(Audio a)
        {
            var sb = new StringBuilder();
            foreach (var s in AudioCues.All) if (a.Shots(s) == 0) sb.Append(s).Append(' ');
            return sb.ToString();
        }

        static bool AllBut(Audio a, params Sound[] skip)
        {
            foreach (var s in AudioCues.All)
                if (a.Shots(s) == 0 && System.Array.IndexOf(skip, s) < 0) return false;
            return true;
        }

        [UnityTest]
        public IEnumerator BotGamePlaysEverySound()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var cfg = GameBootstrap.Instance.Config;
            cfg.features["clock"] = false;   // a game that never ends, so the bots keep playing however long this takes
            cfg.features["botQuickCall"] = false;   // (the huddle clock's tick needs a full huddle; botQuickCall cuts an all-bot one to bots.coachCallS)

            // on (as game.json has it): the bots play (sped up) until every sound has been started
            Assert.IsTrue(cfg.Feature("audio"), "FEATURES.audio is on in game.json");
            yield return null;
            var audio = host.GetComponent<Audio>();
            Assert.IsNotNull(audio, "PartyHost drives the sound");
            Assert.IsNotNull(Object.FindAnyObjectByType<AudioListener>(), "someone is listening");

            Time.timeScale = 8f;
            float giveUp = Time.realtimeSinceStartup + 150f;
            while (!AllBut(audio, Sound.Sting, Sound.Crowd) && Time.realtimeSinceStartup < giveUp) yield return null;
            Assert.IsTrue(AllBut(audio, Sound.Sting, Sound.Crowd), "bot football played these within the time: missing " + Missing(audio));

            // a touchdown can take a while with bots, so tackles stop working until one comes (this game only)
            if (audio.Shots(Sound.Sting) == 0)
            {
                cfg.play.tackleBase = 0f; cfg.play.tackleSpeedBonus = 0f; cfg.play.tackleFromBehindBonus = 0f;
                giveUp = Time.realtimeSinceStartup + 150f;
                while (!AllBut(audio) && Time.realtimeSinceStartup < giveUp) yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsTrue(AllBut(audio), "every sound played at least once: missing " + Missing(audio));

            // and off again: nothing new starts
            cfg.features["audio"] = false;
            var before = new int[AudioCues.All.Length];
            foreach (var s in AudioCues.All) before[(int)s] = audio.Shots(s);
            Time.timeScale = 8f;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Time.timeScale = 1f;
            foreach (var s in AudioCues.All) Assert.AreEqual(before[(int)s], audio.Shots(s), s + " played with the switch off");
        }
    }
}
