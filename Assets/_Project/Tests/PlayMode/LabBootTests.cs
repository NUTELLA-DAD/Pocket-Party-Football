using System.Collections;
using NUnit.Framework;
using PocketFootball.Boot;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // The practice lab (PP_LAB=1, like Lab.bat's -lab) boots with the first drill on the field, its hitboxes drawn,
    // and TAB-style switching to the next drill works (no error logs).
    public sealed class LabBootTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = System.Net.IPAddress.Loopback; PartyHost.PortOverride = 0; System.Environment.SetEnvironmentVariable("PP_LAB", "1"); }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; System.Environment.SetEnvironmentVariable("PP_LAB", null); Time.timeScale = 1f; }

        [UnityTest]
        public IEnumerator LabBootsAndSwitchesDrills()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 30; i++) yield return null;

            var lab = LabMode.Instance;
            Assert.IsNotNull(lab, "the lab started");
            Assert.AreSame(lab, GameBootstrap.Instance.Lab);
            var w = GameBootstrap.Instance.Host.World;
            var d = lab.Lab.Drill;
            Assert.AreEqual("throw", d.id, "the first drill");
            Assert.AreEqual(d.offense.Count + d.defense.Count, w.Players.Count, "only the drill's players");
            Assert.IsNotNull(GameObject.Find("LabOverlay"), "the hitboxes are drawn");

            lab.Lab.Next(1);
            for (int i = 0; i < 10; i++) yield return null;
            d = lab.Lab.Drill;
            Assert.AreEqual(d.offense.Count + d.defense.Count, w.Players.Count, d.id + ": the next drill's players");
            Assert.AreEqual(d.offense.Count + d.defense.Count, GameBootstrap.Instance.Host.Players.Count, "a figure for each");
        }
    }
}
