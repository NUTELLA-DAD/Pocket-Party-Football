using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The title screen's camera angles (FEATURES.titleScreen): where a shot stands and what it looks at, for a ball at
    // (xYd, zYd) and an offense going dir (+1 = toward +X). Pure maths, so the EditMode tests can check every shot in
    // game.json without a window. (A broadcast shot has no pose of its own: the game's sideline camera films it.)
    public static class TitleShots
    {
        public struct ShotPose { public Vector3 position, lookAt; public float fovDeg; }

        public static ShotPose Pose(GameConfig cfg, TitleShot s, float xYd, float zYd, float dir)
        {
            float m = cfg.field.yardM;
            float d = dir < 0f ? -1f : 1f;
            float near = cfg.camera.sideline < 0f ? -1f : 1f;   // the TV camera's sideline (-Z by default)
            var ball = new Vector3(xYd, 0f, zYd);
            var t = cfg.title;
            var at = new Vector3(Mathf.Clamp(xYd - d * s.backYd, -t.maxEndYd, t.maxEndYd), Mathf.Max(s.heightYd, 0.5f),
                                 Mathf.Clamp(zYd + near * s.sideYd, -t.maxSideYd, t.maxSideYd));
            var look = ball + new Vector3(d * s.aheadYd, t.lookUpYd, 0f);
            if ((look - at).sqrMagnitude < 1e-4f) look += new Vector3(d, 0f, 0f);   // (never looks at itself)
            return new ShotPose { position = at * m, lookAt = look * m, fovDeg = s.fovDeg };
        }

        // The next shot: any but the one on now (Random.value in 0..1 picks it), so every cut changes the angle.
        public static int Next(int count, int current, float pick)
        {
            if (count <= 1) return 0;
            if (current < 0) return Mathf.Min((int)(pick * count), count - 1);
            int k = Mathf.Min((int)(pick * (count - 1)), count - 2);
            return k >= current ? k + 1 : k;
        }
    }
}
