using PocketFootball.Sim;

namespace PocketFootball.View
{
    // The defender moves on the TV (WS-03, Sim/DefenseMoves.cs), built from the figure's existing key poses so
    // FootballFigure stays as it is:
    //   SWIM       the far arm comes over the blocker (Reach), then he runs on
    //   BULL RUSH  two hard shoves (Block, both frames)
    //   SWAT       arms up (Reach) while it is live, the arms come down (Catch, then Ready) when it hits
    //   JUMP       arms high (Catch)
    //   DIVE       laid out flat in the air (Dive), then face down on the grass (Prone), up on one knee for the last
    //              anim.getUpS before he is back up (Kneel)
    // Frames count sim ticks at anim.fps (stepped, no wobble). Read-only, like Moves.
    public static class DefenseMovesView
    {
        static int FrameOf(World w, int ticks) => ticks <= 0 ? 0 : (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz);

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
