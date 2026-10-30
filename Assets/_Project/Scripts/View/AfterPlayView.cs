using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.afterPlay on the TV (Sim/AfterPlay.cs moves the men; this only poses them), on top of what PlayersView
    // drew this frame (call it right after PlayersView.Render, like CarrierMovesView):
    //   Huff      bent over, hands on his knees, his back heaving in two stepped keys (afterPlay.huffKeyFrames apart)
    //   HighFive  his right hand goes up as he gets near the man (afterPlay.raiseYd), then the slap
    //   Linger, and a wanderer once he got there: standing, his head turning to look about in stepped keys
    // Walking back is PlayersView's own walk. Key frames step at anim.fps counted in sim ticks (no wobble).
    public static class AfterPlayView
    {
        static readonly HashSet<int> posed = new HashSet<int>(), now = new HashSet<int>();
        static readonly List<int> ended = new List<int>();

        public static void Apply(World w, PlayersView view)
        {
            now.Clear();
            if (AfterPlay.On(w))
                foreach (var p in w.Players)
                {
                    if (view.PoseOverride.ContainsKey(p.Slot)) continue;
                    var fig = view.Figure(p.Slot);
                    if (fig == null || AfterPlay.MoodOf(w, p.Slot, out var r) == AfterMood.None) continue;
                    if (PoseOne(w, p, r, fig)) now.Add(p.Slot);
                }
            // a pose that just ended: told its pose is gone, so PlayersView's next one always lands (the arms too)
            ended.Clear();
            foreach (int slot in posed) if (!now.Contains(slot)) ended.Add(slot);
            foreach (int slot in ended)
            {
                var fig = view.Figure(slot);
                if (fig != null) { fig.Pose((FootballFigure.PoseKind)(-1), 1); fig.Pose((FootballFigure.PoseKind)(-1), 0); }
            }
            posed.Clear();
            foreach (int slot in now) posed.Add(slot);
        }

        static int Frame(World w, int ticks) => ticks <= 0 ? 0 : (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz);

        static bool Still(Player p) => p.VX * p.VX + p.VZ * p.VZ <= 0.25f;

        static bool PoseOne(World w, Player p, AfterPlay.Rec r, FootballFigure fig)
        {
            var c = w.Cfg.afterPlay;
            if (w.Tick < r.Start || (r.Down && w.Phase == Phase.Whistle)) return false;   // (still down from the tackle: Moves has him)
            int since = w.Tick - r.Start;
            switch (r.Mood)
            {
                case AfterMood.Huff:
                    if (since >= r.Hold || !Still(p)) return false;
                    Huff(fig, Frame(w, since) / Mathf.Max(1, c.huffKeyFrames) % 2);
                    return true;
                case AfterMood.HighFive:
                    var mate = w.Find(r.Partner);
                    bool slap = r.Fived >= 0 && w.Tick < r.Fived + Mathf.RoundToInt(c.fiveHoldS * w.Cfg.sim.tickHz);
                    bool near = r.Fived < 0 && mate != null && LivePlay.Dist(p, mate) <= c.raiseYd;
                    if (!slap && !near) return false;
                    HandUp(fig, slap && Frame(w, w.Tick - r.Fived) >= 1);
                    return true;
                case AfterMood.Linger:
                    if (since >= r.Hold || !Still(p)) return false;
                    LookAbout(w, p, fig, since);
                    return true;
                case AfterMood.Wander:
                    if (r.Arrived < 0 || w.Tick - r.Arrived >= r.Hold || !Still(p)) return false;
                    LookAbout(w, p, fig, w.Tick - r.Arrived);
                    return true;
            }
            return false;
        }

        // Bent over with his hands on his knees; key 1: the breath in, chest up a little.
        static void Huff(FootballFigure fig, int key)
        {
            fig.Pose((FootballFigure.PoseKind)(-1), 1);
            fig.Pose((FootballFigure.PoseKind)(-1), 0);
            float hy = fig.Hips.localPosition.y;   // (at rest: the hips' own height)
            float drop = key == 0 ? 12f : 10f, lean = key == 0 ? 44f : 36f;
            fig.Hips.localPosition = new Vector3(0f, hy - drop, 0f);
            fig.Torso.localRotation = Quaternion.Euler(0f, 0f, -lean);
            fig.Head.localRotation = Quaternion.Euler(0f, 0f, 26f);   // (looking up the field, not at the grass)
            fig.ArmL.localRotation = Quaternion.Euler(-14f, 0f, 50f);
            fig.ArmR.localRotation = Quaternion.Euler(14f, 0f, 50f);
            fig.ForeL.localRotation = Quaternion.Euler(0f, 0f, 10f);
            fig.ForeR.localRotation = Quaternion.Euler(0f, 0f, 10f);
            fig.ThighL.localRotation = Quaternion.Euler(-8f, 0f, 24f);
            fig.ThighR.localRotation = Quaternion.Euler(8f, 0f, 24f);
            fig.ShinL.localRotation = Quaternion.Euler(0f, 0f, -30f);
            fig.ShinR.localRotation = Quaternion.Euler(0f, 0f, -30f);
        }

        // The right hand up for the high five; slap: the hands meet, the elbow gives a little.
        static void HandUp(FootballFigure fig, bool slap)
        {
            fig.ArmR.localRotation = Quaternion.Euler(14f, 0f, slap ? 150f : 168f);
            fig.ForeR.localRotation = Quaternion.Euler(0f, 0f, slap ? 26f : 8f);
        }

        // Standing about: his head turns to one side, back, to the other, a key every lookKeyFrames, the order his own.
        static void LookAbout(World w, Player p, FootballFigure fig, int since)
        {
            var c = w.Cfg.afterPlay;
            int key = Frame(w, since) / Mathf.Max(1, c.lookKeyFrames) + p.Slot;
            float yaw = (key % 4) switch { 0 => 0f, 1 => c.lookDeg, 2 => 0f, _ => -c.lookDeg };
            if (p.Slot % 2 == 1) yaw = -yaw;
            fig.Head.localRotation = Quaternion.Euler(0f, yaw, 0f) * fig.Head.localRotation;
        }
    }
}
