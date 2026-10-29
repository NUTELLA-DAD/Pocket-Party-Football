using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using Item = PocketFootball.View.PauseMenuModel.Item;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.pauseMenu (View/PauseMenuModel.cs, drawn by Boot/PauseMenu.cs): Esc in the game opens CONTINUE,
    // SETTINGS, MAIN MENU and QUIT GAME. The two that end the game need a second press.
    public sealed class PauseMenuTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("pauseMenu"), "FEATURES.pauseMenu is on in game.json");
            Assert.AreEqual("NO_PAUSE_MENU", ConfigLoader.EnvName("pauseMenu"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_PAUSE_MENU", "1" } }).Feature("pauseMenu"));
        }

        [Test]
        public void TheButtonsInOrder()
        {
            var cfg = ConfigTests.Load();
            cfg.features["titleScreen"] = true;
            CollectionAssert.AreEqual(new[] { Item.Continue, Item.Settings, Item.MainMenu, Item.Quit }, new PauseMenuModel(cfg).Items);
            cfg.features["titleScreen"] = false;   // no title to go back to: no MAIN MENU
            CollectionAssert.AreEqual(new[] { Item.Continue, Item.Settings, Item.Quit }, new PauseMenuModel(cfg).Items);
        }

        [Test]
        public void ContinueIsFirstAndOnePressDoesIt()
        {
            var m = new PauseMenuModel(ConfigTests.Load());
            Assert.AreEqual(Item.Continue, m.Picked);
            Assert.AreEqual("CONTINUE", m.Label(0));
            Assert.AreEqual(Item.Continue, m.Press());
            m.Move(+1);
            Assert.AreEqual(Item.Settings, m.Press());
        }

        [Test]
        public void MainMenuAndQuitAskTwice()
        {
            var m = new PauseMenuModel(ConfigTests.Load());
            m.Move(+2);
            Assert.AreEqual(Item.MainMenu, m.Picked);
            Assert.IsNull(m.Press(), "the first press only asks");
            Assert.AreEqual("SURE? PRESS AGAIN", m.Label(2));
            Assert.AreEqual(Item.MainMenu, m.Press());

            m.Move(+1);
            Assert.AreEqual(Item.Quit, m.Picked);
            Assert.IsNull(m.Press());
            m.Move(-1);   // moving off forgets the question
            Assert.IsFalse(m.Confirming);
            m.Move(+1);
            Assert.IsNull(m.Press());
            Assert.AreEqual(Item.Quit, m.Press());
        }

        [Test]
        public void TheHighlightWrapsAround()
        {
            var m = new PauseMenuModel(ConfigTests.Load());
            m.Move(-1);
            Assert.AreEqual(Item.Quit, m.Picked);
            m.Move(+1);
            Assert.AreEqual(Item.Continue, m.Picked);
            m.Reset();
            Assert.AreEqual(0, m.Pick);
        }
    }
}
