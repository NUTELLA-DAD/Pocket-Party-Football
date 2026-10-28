using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.lobbyScreen (Boot/Lobby.cs): the TV's join screen. Every team in the league has its emblem card picture
    // for the TV and for the phones' side buttons, and the screen's numbers are in game.json.
    public sealed class LobbyScreenTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("lobbyScreen"), "FEATURES.lobbyScreen is on in game.json");
            Assert.AreEqual("NO_LOBBY_SCREEN", ConfigLoader.EnvName("lobbyScreen"));
            var off = ConfigTests.Load(new Dictionary<string, string> { { "NO_LOBBY_SCREEN", "1" } });
            Assert.IsFalse(off.Feature("lobbyScreen"));
        }

        [Test]
        public void TheScreensNumbersAreSet()
        {
            var l = ConfigTests.Load().lobby;
            Assert.That(l.tint, Is.InRange(0f, 1f));
            Assert.That(l.qrFrac, Is.GreaterThan(0.2f).And.LessThan(0.6f), "the QR code: a big part of the screen height");
            Assert.That(l.emblemFrac, Is.GreaterThan(0.1f).And.LessThan(0.5f));
            Assert.That(l.teamApplyS, Is.GreaterThan(0f));
            Assert.That(l.listRows, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EveryTeamHasItsEmblemOnTheTvAndThePhones()
        {
            var cfg = ConfigTests.Load();
            foreach (var t in TeamPick.League(cfg))
            {
                Assert.IsNotNull(Resources.Load<Texture2D>("Emblems/" + t.@short), "Resources/Emblems/" + t.@short + ".png (tools/teams/wordmarks.py)");
                string phone = Path.Combine(Application.streamingAssetsPath, "phone", "emblems", t.@short + ".png");
                Assert.IsTrue(File.Exists(phone), "the phones' copy: " + phone);
            }
        }
    }
}
