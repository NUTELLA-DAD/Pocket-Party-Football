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
    // FEATURES.smoothMotion: the sim moves 30 times a second, the TV draws 60 or more. Between two ticks a turning
    // man's facing is blended like his position (not snapped once a tick), and the camera follows where the carrier
    // is drawn. NO_SMOOTH_MOTION=1: the facing snaps once a tick as before.
    public sealed class SmoothMotionTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; Lobby.HoldOverride = false; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Lobby.HoldOverride = null; }

        // which way a figure faces on the grass (degrees; its local +x is forward)
        static float Heading(PlayersView v, int slot)
        {
            var f = Vector3.ProjectOnPlane(v.Figure(slot).transform.right, Vector3.up);
            return Mathf.Atan2(-f.z, f.x) * Mathf.Rad2Deg;
        }

        // Draws the same tick at alpha 0, 0.5 and 1 and finds a man whose facing moves between them (a turn this tick).
        static bool FindBlendedTurn(PartyHost host, out string detail)
        {
            var w = host.World;
            foreach (var p in w.Players)
            {
                host.Players.Render(w, 0f); float a = Heading(host.Players, p.Slot);
                host.Players.Render(w, 0.5f); float m = Heading(host.Players, p.Slot);
                host.Players.Render(w, 1f); float b = Heading(host.Players, p.Slot);
                float whole = Mathf.DeltaAngle(a, b), half = Mathf.DeltaAngle(a, m);
                if (Mathf.Abs(whole) > 4f && Mathf.Abs(half) > 1f && Mathf.Abs(whole - half) > 1f && Mathf.Sign(half) == Mathf.Sign(whole))
                { detail = $"slot {p.Slot}: {a:0.0} -> {m:0.0} -> {b:0.0}"; return true; }
            }
            detail = "";
            return false;
        }

        [UnityTest]
        public IEnumerator ATurnBlendsBetweenTicks()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            var cfg = GameBootstrap.Instance.Config;
            Assert.IsTrue(cfg.Feature("smoothMotion"), "FEATURES.smoothMotion is on in game.json");
            Assert.IsNotNull(Object.FindAnyObjectByType<FrameMeter>(), "the frame meter is up");
            host.World.BotsWander = true;   // the bots run about and turn
            bool found = false; string detail = "";
            float giveUp = Time.realtimeSinceStartup + 20f;
            while (!found && Time.realtimeSinceStartup < giveUp) { yield return null; found = FindBlendedTurn(host, out detail); }
            Assert.IsTrue(found, "some man turning was drawn part way round between two ticks");
            Debug.Log("[smooth] " + detail);
        }

        [UnityTest]
        public IEnumerator OffTheFacingSnapsOnceATick()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            GameBootstrap.Instance.Config.features["smoothMotion"] = false;   // (as NO_SMOOTH_MOTION=1 would)
            host.World.BotsWander = true;
            bool found = false; string detail = "";
            float until = Time.realtimeSinceStartup + 5f;
            while (!found && Time.realtimeSinceStartup < until) { yield return null; found = FindBlendedTurn(host, out detail); }
            Assert.IsFalse(found, "with smoothMotion off nobody's facing moves between ticks: " + detail);
        }

        [UnityTest]
        public IEnumerator ShownPositionIsBetweenTheTicks()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 20; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            w.BotsWander = true;
            for (int i = 0; i < 60; i++) yield return null;
            Player p = null;
            foreach (var q in w.Players) if (Mathf.Abs(q.VX) > 1f) { p = q; break; }
            Assume.That(p, Is.Not.Null, "someone is running");
            host.Players.Render(w, 1f);
            Assert.AreEqual(p.X, host.Players.ShownX(p), 1e-4f, "alpha 1: the latest tick");
            host.Players.Render(w, 0f);
            float was = host.Players.ShownX(p);
            host.Players.Render(w, 0.5f);
            Assert.AreEqual((was + p.X) * 0.5f, host.Players.ShownX(p), 1e-3f, "alpha 0.5: half way from the tick before");
        }
    }
}
