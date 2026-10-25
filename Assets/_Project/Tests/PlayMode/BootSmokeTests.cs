using System.Collections;
using NUnit.Framework;
using PocketFootball.Boot;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // The Boot scene builds the field and the sideline camera from data, with no errors (an error log fails the test).
    public sealed class BootSmokeTests
    {
        // the game's phone server listens on 127.0.0.1 in tests (no firewall prompt)
        [SetUp] public void Loopback() { PartyHost.BindOverride = System.Net.IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; }

        [UnityTest]
        public IEnumerator BootSceneBuildsFieldAndCamera()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 60; i++) yield return null;

            var boot = GameBootstrap.Instance;
            Assert.IsNotNull(boot, "GameBootstrap did not start");
            Assert.IsNotNull(boot.Field, "no field");
            Assert.Greater(boot.Field.transform.childCount, 40);

            var cam = Camera.main;
            Assert.IsNotNull(cam, "no main camera");
            Assert.Less(cam.transform.position.z, 0f, "the camera should stand on the near sideline");
            var mid = cam.WorldToViewportPoint(Vector3.zero);
            Assert.That(mid.x, Is.InRange(0.4f, 0.6f), "midfield should be centred");
            Assert.That(mid.y, Is.InRange(0.1f, 0.9f));
            Assert.Greater(mid.z, 0f);
        }
    }
}
