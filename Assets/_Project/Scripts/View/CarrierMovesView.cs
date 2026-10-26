using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // WS-04: the carrier moves and fumbles on the TV, on top of what PlayersView and Moves already drew this frame
    // (call it right after PlayersView.Render). It builds every move from FootballFigure's existing poses, so the
    // figure code is untouched:
    //   SPIN       the juke's plant and push-off, the whole body turning round in stepped key frames
    //   STIFF ARM  the block's shove pose: arm out, legs driving
    //   DIVE       the dive, then flat on the grass if he came up short
    //   FUMBLE     the carrier bobbles it and goes down, so does the man who hit him; on the grass while it is loose
    // Key frames step at anim.fps counted in sim ticks (no wobble). FEATURES.moves off: nothing (plain poses).
    public static class CarrierMovesView
    {
        public static void Apply(World w, PlayersView view)
        {
            if (!w.Cfg.Feature("moves")) return;
            var s = w.Carry;
            if (s.Move != CarrierMove.None && !view.PoseOverride.ContainsKey(s.Slot))
            {
                var fig = view.Figure(s.Slot);
                if (fig != null)
                {
                    int frame = Frame(w, w.Tick - s.StartTick);
                    switch (s.Move)
                    {
                        case CarrierMove.Spin:
                            fig.Pose(FootballFigure.PoseKind.Juke, frame % 2);
                            // all the way round over the move's key frames, one step per key
                            int keys = Mathf.Max(1, Frame(w, w.Cfg.carrier.spinTicks));
                            fig.transform.localRotation *= Quaternion.Euler(0f, 360f * (frame % keys) / keys, 0f);
                            break;
                        case CarrierMove.StiffArm: fig.Pose(FootballFigure.PoseKind.Block, 1); break;
                        case CarrierMove.Dive: fig.Pose(FootballFigure.PoseKind.Dive, 0); break;
                    }
                }
            }
            // a dive short of the goal line: he stays down until the next huddle
            if (s.DownSlot >= 0 && w.Phase == Phase.Whistle && w.Result == "DOWN") Down(view, s.DownSlot, -1);
            if (Fumbles.Loose(w))
            {
                int frame = Frame(w, s.LooseTicks);
                if (frame < 2) Pose(view, s.FumbledBy, FootballFigure.PoseKind.Bobble, frame);
                else Down(view, s.FumbledBy, frame);
                Down(view, s.HitBy, frame);
            }
        }

        static int Frame(World w, int ticks) => ticks <= 0 ? 0 : (int)(ticks * w.Cfg.anim.fps / w.Cfg.sim.tickHz);

        static void Pose(PlayersView view, int slot, FootballFigure.PoseKind kind, int frame)
        {
            if (slot < 0 || view.PoseOverride.ContainsKey(slot)) return;
            var fig = view.Figure(slot);
            if (fig != null) fig.Pose(kind, frame);
        }

        // Going down (frame 0 and 1), then on his front on the grass. frame < 0: already down.
        static void Down(PlayersView view, int slot, int frame)
        {
            if (frame >= 0 && frame < 2) Pose(view, slot, FootballFigure.PoseKind.FallFront, frame);
            else Pose(view, slot, FootballFigure.PoseKind.Prone, 0);
        }
    }
}
