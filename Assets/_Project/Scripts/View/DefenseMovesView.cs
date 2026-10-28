using PocketFootball.Sim;

namespace PocketFootball.View
{
    // The defender moves on the TV (WS-03, Sim/DefenseMoves.cs), built from the figure's existing key poses so
    // FootballFigure stays as it is:
    //   SWIM       the far arm comes over the blocker (Reach), then he runs on
    //   BULL RUSH  two hard shoves (Block, both frames)
    //   SWAT       arms up (Reach) while it is live, the arms come down (Catch, then Ready) when it hits
    //   JUMP       arms high (Catch), and off the grass in a stepped hop (LiftYd, FEATURES.jumpAnytime)
    //   DIVE       laid out flat in the air (Dive), then face down on the grass (Prone), up on one knee for the last
    //              anim.getUpS before he is back up (Kneel)
    // Frames count sim ticks at anim.fps (stepped, no wobble). Read-only, like Moves.
    public static class DefenseMovesView
    {
        static int FrameOf(World w, int ticks) => ticks <= 0 ? 0 : (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz);

        // FEATURES.jumpAnytime: how high (yd) a jumper's feet are off the grass. A hop over the jump's swatActiveS, up and
        // down in stepped keys at anim.fps (no wobble); 0 for everyone else.
        public static float LiftYd(World w, Player p)
        {
            if (!DefenseMoves.On(w) || !DefenseMoves.JumpAnytime(w)) return 0f;
            if (!DefenseMoves.Of(w).BySlot.TryGetValue(p.Slot, out var d) || d.Move != DefMove.Jump) return 0f;
            int keys = FrameOf(w, Huddle.Ticks(w, w.Cfg.defense.swatActiveS)) + 1;
            float t = Clamp01((FrameOf(w, d.Ticks) + 1f) / keys);
            return w.Cfg.defense.jumpLiftYd * 4f * t * (1f - t);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        // False: no defender move to show for him (run, stand and Moves as usual).
        public static bool PoseFor(World w, Player p, out FootballFigure.PoseKind kind, out int frame)
        {
            kind = FootballFigure.PoseKind.Ready;
            frame = 0;
            if (!DefenseMoves.On(w) || !DefenseMoves.Of(w).BySlot.TryGetValue(p.Slot, out var d)) return false;
            if (d.DownTicks > 0)
            {
                kind = d.DownTicks <= Huddle.Ticks(w, w.Cfg.anim.getUpS) ? FootballFigure.PoseKind.Kneel : FootballFigure.PoseKind.Prone;
                return true;
            }
            switch (d.Move)
            {
                case DefMove.Dive: kind = FootballFigure.PoseKind.Dive; return true;
                case DefMove.Swat: kind = FootballFigure.PoseKind.Reach; frame = FrameOf(w, d.Ticks) % 2; return true;
                case DefMove.Jump: kind = FootballFigure.PoseKind.Catch; return true;
            }
            int f = FrameOf(w, d.ShownTicks);
            switch (d.Shown)
            {
                case DefMove.Swim: kind = f == 0 ? FootballFigure.PoseKind.Reach : FootballFigure.PoseKind.Run; frame = f % 2; return true;
                case DefMove.BullRush: kind = FootballFigure.PoseKind.Block; frame = f % 2; return true;
                case DefMove.Swat:
                case DefMove.Jump:
                    if (!d.ShownWon) return false;
                    kind = f == 0 ? FootballFigure.PoseKind.Catch : FootballFigure.PoseKind.Ready;
                    return true;
            }
            return false;
        }
    }
}
