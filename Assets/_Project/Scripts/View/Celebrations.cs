using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // A short celebration on a score (WS-12, FEATURES.stats with stats.celebrate): the scorer spikes the ball, then
    // Moves.cs's own Celebrate (the hop and fist pump) takes over; up to stats.maxCheer teammates within stats.cheerYd
    // jump up and down, and as many of the other team's men hang their heads, for stats.celebrateS. New states on
    // top of Moves.cs, which is not changed: PostGame.Drive puts these poses on the live figures after PlayersView drew.
    // Read from the shared event list (a Touchdown event), counted in sim ticks and stepped at anim.fps (no wobble).
    // Pure C# apart from Apply: the EditMode tests drive it with a World.
    public enum Cheer { None, Spike, Leap, Slump }

    public struct CheerNow
    {
        public Cheer Kind;
        public int Frame;        // key frame since it began (anim.fps)
    }

    public sealed class Celebrations
    {
        sealed class Rec { public Cheer Kind; public int Start, End; }

        readonly SortedDictionary<int, Rec> bySlot = new SortedDictionary<int, Rec>();
        readonly List<GameEvent> fresh = new List<GameEvent>();
        readonly List<Player> near = new List<Player>();
        readonly List<int> done = new List<int>();
        int nextSeq;
        Phase phase;

        public static bool On(GameConfig cfg) => Stats.On(cfg) && cfg.stats.celebrate;

        public int Count => bySlot.Count;

        static int Ticks(World w, float s) => Math.Max(1, (int)Math.Round(s * w.Cfg.sim.tickHz));

        // Call after the World stepped (once a frame is enough: it reads every event since the last call).
        public void Observe(World w)
        {
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            if (!On(w.Cfg)) { bySlot.Clear(); return; }
            var c = w.Cfg.stats;
            foreach (var e in fresh)
            {
                if (e.Kind != EventKind.Touchdown || e.Slot < 0) continue;
                bySlot.Clear();
                bySlot[e.Slot] = new Rec { Kind = Cheer.Spike, Start = e.Tick, End = e.Tick + Ticks(w, c.spikeS) };
                Crowd(w, e, true, Cheer.Leap);
                Crowd(w, e, false, Cheer.Slump);
            }
            // a new huddle: everyone back to the game
            if (phase == Phase.Whistle && w.Phase != Phase.Whistle) bySlot.Clear();
            phase = w.Phase;
            done.Clear();
            foreach (var kv in bySlot) if (w.Tick >= kv.Value.End) done.Add(kv.Key);
            foreach (int slot in done) bySlot.Remove(slot);
        }

        // The nearest maxCheer men within cheerYd of the score, on the scorer's team or the other one.
        void Crowd(World w, GameEvent e, bool mates, Cheer kind)
        {
            var c = w.Cfg.stats;
            near.Clear();
            foreach (var p in w.Players)
            {
                if (p.Slot == e.Slot || (p.Team == e.Team) != mates) continue;
                float dx = p.X - e.X, dz = p.Z - e.Z;
                if (dx * dx + dz * dz <= c.cheerYd * c.cheerYd) near.Add(p);
            }
            near.Sort((a, b) =>
            {
                float da = (a.X - e.X) * (a.X - e.X) + (a.Z - e.Z) * (a.Z - e.Z), db = (b.X - e.X) * (b.X - e.X) + (b.Z - e.Z) * (b.Z - e.Z);
                return da != db ? da.CompareTo(db) : a.Slot.CompareTo(b.Slot);
            });
            for (int i = 0; i < near.Count && i < c.maxCheer; i++)
                bySlot[near[i].Slot] = new Rec { Kind = kind, Start = e.Tick, End = e.Tick + Ticks(w, c.celebrateS) };
        }

        public CheerNow Of(World w, int slot)
        {
            if (!bySlot.TryGetValue(slot, out var r) || w.Tick >= r.End) return default;
            int ticks = Math.Max(0, w.Tick - r.Start);
            return new CheerNow { Kind = r.Kind, Frame = (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz) };
        }

        // ---- the poses: bone angles in degrees, the same way FootballFigure's own poses are written ----
        struct P
        {
            public float drop, lean, head, armL, armLx, foreL, armR, armRx, foreR, thighL, thighLx, shinL, thighR, thighRx, shinR;
        }

        static P Get(CheerNow c)
        {
            switch (c.Kind)
            {
                // the spike: 0-1 the ball held high, 2 on it is slammed into the grass
                case Cheer.Spike: return c.Frame < 2
                        ? new P { drop = 2, lean = -8, head = -18, armL = 40, armLx = 30, foreL = 60, armR = 178, armRx = 10, foreR = 6, thighL = 10, thighLx = 6, shinL = -12, thighR = -6, thighRx = 6, shinR = -10 }
                        : new P { drop = 14, lean = 34, head = -12, armL = 20, armLx = 35, foreL = 50, armR = 20, armRx = 14, foreR = 8, thighL = 40, thighLx = 8, shinL = -50, thighR = -18, thighRx = 8, shinR = -20 };
                // a teammate jumps: 0 in the air, both arms up; 1 landed, knees bent
                case Cheer.Leap: return c.Frame % 2 == 0
                        ? new P { drop = -12, lean = -4, head = -16, armL = 170, armLx = 28, foreL = 12, armR = 170, armRx = 28, foreR = 12, thighL = 24, thighLx = 8, shinL = -50, thighR = 24, thighRx = 8, shinR = -50 }
                        : new P { drop = 12, lean = 14, head = -8, armL = 120, armLx = 30, foreL = 30, armR = 120, armRx = 30, foreR = 30, thighL = 34, thighLx = 10, shinL = -54, thighR = 34, thighRx = 10, shinR = -54 };
                // the other team: hands on the helmet, head down (held still)
                case Cheer.Slump: return new P { drop = 4, lean = 16, head = 26, armL = 150, armLx = 40, foreL = 120, armR = 150, armRx = 40, foreR = 120, thighLx = 5, thighRx = 5 };
            }
            return default;
        }

        // Puts the pose on a figure after PlayersView posed it. FootballFigure remembers its last pose, so it is told
        // its pose is gone (and stands it at rest, which is where the hip height is read): PlayersView's next pose
        // then always lands, and once the celebration ends the figure goes straight back to the game.
        public static void Apply(FootballFigure fig, CheerNow c)
        {
            if (fig == null || c.Kind == Cheer.None) return;
            fig.Pose((FootballFigure.PoseKind)(-1), 1);
            fig.Pose((FootballFigure.PoseKind)(-1), 0);
            float hy = fig.Hips.localPosition.y;   // (at rest: the hips' own height)
            var p = Get(c);
            fig.Hips.localPosition = new Vector3(0f, hy - p.drop, 0f);
            fig.Torso.localRotation = Quaternion.Euler(0f, 0f, -p.lean);
            fig.Head.localRotation = Quaternion.Euler(0f, 0f, -p.head);
            fig.ArmL.localRotation = Quaternion.Euler(-p.armLx, 0f, p.armL);
            fig.ArmR.localRotation = Quaternion.Euler(p.armRx, 0f, p.armR);
            fig.ForeL.localRotation = Quaternion.Euler(0f, 0f, p.foreL);
            fig.ForeR.localRotation = Quaternion.Euler(0f, 0f, p.foreR);
            fig.ThighL.localRotation = Quaternion.Euler(-p.thighLx, 0f, p.thighL);
            fig.ThighR.localRotation = Quaternion.Euler(p.thighRx, 0f, p.thighR);
            fig.ShinL.localRotation = Quaternion.Euler(0f, 0f, p.shinL);
            fig.ShinR.localRotation = Quaternion.Euler(0f, 0f, p.shinR);
        }
    }
}
