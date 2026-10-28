using System;
using System.Collections.Generic;
using PocketFootball.Sim;

namespace PocketFootball.View
{
    // What a big play leaves on the players' faces and jerseys, read from the shared event list (like Celebrations):
    // FEATURES.faceReactions: the scoring team squints happily after a touchdown, an interceptor after his pick, a hitter
    //   after a BIG HIT or a sack; the man hit or sacked winces (Mood: +1 happy, -1 wince, 0 none; on or off, no blend).
    // FEATURES.grassStains: every time a man goes down (tackled, or making the tackle) he picks up more grass and dirt,
    //   up to reactions.stainMax, for the rest of the game (Stain, drawn by the toon shader where the marks map allows).
    // Counted in sim ticks. Pure C#: the EditMode tests drive it with a World.
    public sealed class Reactions
    {
        struct Face { public float Mood; public int End; }

        readonly SortedDictionary<int, Face> faces = new SortedDictionary<int, Face>();
        readonly SortedDictionary<int, float> stains = new SortedDictionary<int, float>();
        readonly List<GameEvent> fresh = new List<GameEvent>();
        int nextSeq, lastTick = -1;

        static int Ticks(World w, float s) => Math.Max(1, (int)Math.Round(s * w.Cfg.sim.tickHz));

        // Call after the World stepped (once a frame is enough: it reads every event since the last call).
        public void Observe(World w)
        {
            if (w.Tick < lastTick || w.Events.Next < nextSeq) { faces.Clear(); stains.Clear(); nextSeq = 0; }   // a new game
            lastTick = w.Tick;
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            var c = w.Cfg.reactions;
            foreach (var e in fresh)
            {
                switch (e.Kind)
                {
                    case EventKind.Touchdown:
                        foreach (var p in w.Players) if (p.Team == e.Team) Feel(w, e, p.Slot, 1f, c.touchdownS);
                        break;
                    case EventKind.Interception:
                        Feel(w, e, e.Slot, 1f, c.pickS);
                        break;
                    case EventKind.Sack:
                        Feel(w, e, e.Slot, 1f, c.sackS);
                        Feel(w, e, e.Other, -1f, c.sackS);
                        Dirty(w, e.Other, c.stainTackled + c.stainBigHit);
                        Dirty(w, e.Slot, c.stainTackler);
                        break;
                    case EventKind.Tackle:
                        bool big = e.Text == "BIG HIT";
                        if (big) { Feel(w, e, e.Slot, 1f, c.bigHitS); Feel(w, e, e.Other, -1f, c.bigHitS); }
                        Dirty(w, e.Other, c.stainTackled + (big ? c.stainBigHit : 0f));
                        Dirty(w, e.Slot, c.stainTackler);
                        break;
                }
            }
        }

        void Feel(World w, GameEvent e, int slot, float mood, float seconds)
        {
            if (slot < 0 || !w.Cfg.Feature("faceReactions")) return;
            faces[slot] = new Face { Mood = mood, End = e.Tick + Ticks(w, seconds) };
        }

        void Dirty(World w, int slot, float add)
        {
            if (slot < 0 || !w.Cfg.Feature("grassStains")) return;
            stains.TryGetValue(slot, out float s);
            stains[slot] = Math.Min(w.Cfg.reactions.stainMax, s + add);
        }

        // A face and grass held for a photo (Boot/PhotoScenes "grit").
        public void Hold(int slot, float mood, float stain)
        {
            faces[slot] = new Face { Mood = mood, End = int.MaxValue };
            stains[slot] = stain;
        }

        // +1 happy, -1 wincing, 0 his usual face.
        public float Mood(World w, int slot) =>
            w.Cfg.Feature("faceReactions") && faces.TryGetValue(slot, out var f) && w.Tick < f.End ? f.Mood : 0f;

        // FEATURES.nameBars (drawn by Boot/PartyHost): what a person's bar says (the name, "..." while the phone is away,
        // (BOT) while a bot plays for him) and how solid it is (see-through during a live play, so it doesn't hide it).
        public static string BarLabel(string name, bool connected, bool botHelp) =>
            (string.IsNullOrEmpty(name) ? "?" : name) + (connected ? "" : " ...") + (botHelp ? " (BOT)" : "");

        public static float BarAlpha(GameConfig cfg, Phase phase) => phase == Phase.Live ? cfg.reactions.barLiveAlpha : 0.9f;

        // How much grass he has on him, 0..stainMax.
        public float Stain(World w, int slot) =>
            w.Cfg.Feature("grassStains") && stains.TryGetValue(slot, out float s) ? s : 0f;
    }
}
