using System.Collections;
using System.Net;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // M1-e: the broadcast camera. Every player stands at least 48 px tall at 1080p; the camera follows the ball down
    // the field; a person off the picture gets an edge portrait (everyone is on screen or at the edge).
    public sealed class CameraFramingTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; }

        static float PixelHeight(Camera cam, PlayersView view, int slot)
        {
            var feet = view.FeetPosition(slot);
            var head = view.HeadPosition(slot);
            var a = cam.WorldToViewportPoint(feet);
            var b = cam.WorldToViewportPoint(head);
            return Mathf.Abs(b.y - a.y) * 1080f;
        }

        [UnityTest]
        public IEnumerator EveryPlayerIsAtLeast48PixelsTall()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            var cam = GameBootstrap.Instance.Rig.Cam;
            float smallest = float.MaxValue;
            foreach (var p in host.World.Players)
            {
                var v = cam.WorldToViewportPoint(host.Players.FeetPosition(p.Slot));
                if (v.x < 0f || v.x > 1f) continue;   // (off the picture: an edge portrait instead)
                smallest = Mathf.Min(smallest, PixelHeight(cam, host.Players, p.Slot));
            }
            Assert.GreaterOrEqual(smallest, 48f, "the camera floor: no figure under 48 px at 1080p");
        }

        [UnityTest]
        public IEnumerator TheCameraFollowsTheBall()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            var cam = GameBootstrap.Instance.Rig.Cam;
            float x0 = cam.transform.position.x;
            // move the whole line of scrimmage 30 yards down the field: the camera must go with it
            w.LineOfScrimmageX += w.Dir * 30f;
            w.LineUp();
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater((cam.transform.position.x - x0) * w.Dir, 20f * w.Cfg.field.yardM, "the camera slid along with the play");
            var v = cam.WorldToViewportPoint(host.Players.BallPosition);
            Assert.That(v.x, Is.InRange(0.15f, 0.85f), "the ball is well inside the picture");
            Assert.AreEqual(0f, cam.transform.rotation.eulerAngles.z, 1e-3f, "never rolls");
        }

        [UnityTest]
        public IEnumerator SomeoneOffThePictureIsAtTheEdge()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            var cam = GameBootstrap.Instance.Rig.Cam;
            // a person way down the field, far off camera
            Player far = null;
            foreach (var p in host.World.Players) { far = p; break; }
            far.Human = true;
            far.X = -host.World.Dir * 55f;
            yield return null;
            var v = cam.WorldToViewportPoint(host.Players.HeadPosition(far.Slot));
            Assert.IsTrue(v.x < 0f || v.x > 1f, "the test player really is off the picture");
            Assert.IsTrue(Hud.IsOffScreen(cam, host.Players.HeadPosition(far.Slot)), "so the TV draws an edge portrait for them");
        }
    }
}
