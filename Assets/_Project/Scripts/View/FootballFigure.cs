using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // A football player's pose rig (rig units = cm, facing +x, left = +z): 11 bones and two hands, no mesh of its own.
    // Every player wears a rigged model over it (ModelSkin); poses are bone angles, stepped (no time-based wobble).
    public sealed class FootballFigure : MonoBehaviour
    {
        public enum PoseKind
        {
            Stand, Ready, Stance, Block, Throw, Run, Carry, Backpedal, Dive, Catch,
            // the moves (Moves.cs): frame picks the key where a move has more than one
            Release, Handoff, Reach, Secure, Bobble, Juke, Whiff, FallFront, FallBack, Prone, Supine, Kneel, Celebrate,
            Lock, Stagger,
            // the cheerleaders on the sideline (Stadium.cs): frame picks one of CheerFrames cheer poses
            Cheer,
        }

        public const int CheerFrames = 8;

        public Transform Hips, Torso, Head, ArmL, ForeL, ArmR, ForeR, ThighL, ShinL, ThighR, ShinR, HandR, HandL;
        float hipY;

        // The players' material: the shared toon look with a warmer, same-hue shade (view.playerShade*), one instance.
        static Material kitMat;
        static Material kitFrom;
        public static Material KitMaterial(Material toon, ViewConfig v)
        {
            if (toon == null || string.IsNullOrEmpty(v.playerShadeHue)) return toon;
            if (kitMat != null && kitFrom == toon) return kitMat;
            kitFrom = toon;
            kitMat = new Material(toon) { name = "Toon (players)" };
            kitMat.SetColor("_ShadeHue", Colors.Hex(v.playerShadeHue));
            kitMat.SetFloat("_ShadeUnify", v.playerShadeUnify);
            if (v.playerOutlinePx > 0f) kitMat.SetFloat("_OutlineWidth", v.playerOutlinePx);
            if (v.playerOutlineTint > 0f) kitMat.SetFloat("_OutlineTint", v.playerOutlineTint);
            if (v.playerRim > 0f) kitMat.SetFloat("_Rim", v.playerRim);
            if (v.playerShadeSaturation > 0f) kitMat.SetFloat("_ShadeSaturation", v.playerShadeSaturation);
            if (!string.IsNullOrEmpty(v.playerShadeLift)) kitMat.SetColor("_ShadeLift", Colors.Hex(v.playerShadeLift));
            if (!string.IsNullOrEmpty(v.playerBounce)) kitMat.SetColor("_BounceColor", Colors.Hex(v.playerBounce));
            return kitMat;
        }

        public const float RigHeight = 182f;   // the figure's height in rig units (cm)

        // The invisible rig every player is posed on: 11 bones and two hands, no mesh. ModelSkin puts a rigged model on it.
        public static FootballFigure Create(Transform parent, string name, float heightM)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localScale = Vector3.one * heightM / RigHeight;
            var fig = holder.gameObject.AddComponent<FootballFigure>();
            float hipY = 60f, kneeY = 31f, neckY = 115f, shY = 108f;
            fig.hipY = hipY;
            float legZ = 11.8f, armZ = 28.6f;
            float elbowY = shY - 22f, handY = shY - 45f;

            Transform Bone(string n, Transform p, Vector3 worldAt)
            {
                var t = new GameObject(n).transform;
                t.SetParent(p, false);
                t.position = holder.TransformPoint(worldAt);
                return t;
            }
            fig.Hips = Bone("Hips", holder, new Vector3(0, hipY, 0));
            fig.Torso = Bone("Torso", fig.Hips, new Vector3(0, hipY + 6, 0));
            fig.Head = Bone("Head", fig.Torso, new Vector3(0, neckY, 0));
            fig.ArmL = Bone("ArmL", fig.Torso, new Vector3(0, shY, armZ));
            fig.ForeL = Bone("ForeL", fig.ArmL, new Vector3(2, elbowY, armZ + 3));
            fig.ArmR = Bone("ArmR", fig.Torso, new Vector3(0, shY, -armZ));
            fig.ForeR = Bone("ForeR", fig.ArmR, new Vector3(2, elbowY, -armZ - 3));
            fig.ThighL = Bone("ThighL", fig.Hips, new Vector3(0, hipY, legZ));
            fig.ShinL = Bone("ShinL", fig.ThighL, new Vector3(0, kneeY, legZ));
            fig.ThighR = Bone("ThighR", fig.Hips, new Vector3(0, hipY, -legZ));
            fig.ShinR = Bone("ShinR", fig.ThighR, new Vector3(0, kneeY, -legZ));
            fig.HandL = Bone("HandL", fig.ForeL, new Vector3(7, handY, armZ + 3));
            fig.HandR = Bone("HandR", fig.ForeR, new Vector3(7, handY, -armZ - 3));
            fig.Pose(PoseKind.Ready, 0);
            return fig;
        }

        // ---- poses: bone angles in degrees. Z turns a limb forward (+) / back (-); X spreads it sideways. ----
        // drop lowers the hips (cm; negative = a hop); tilt tips the whole body over at the hips (+ onto his front,
        // - onto his back), for the falls and lying on the grass.
        struct P
        {
            public float drop, tilt, lean, head, armL, armLx, foreL, armR, armRx, foreR, thighL, thighLx, shinL, thighR, thighRx, shinR;
        }

        // running legs for the moves done on the run (reach, secure): the Run pose's, by stride frame
        static P RunLegs(P p, int frame)
        {
            var r = Get(PoseKind.Run, frame);
            p.thighL = r.thighL; p.thighLx = r.thighLx; p.shinL = r.shinL;
            p.thighR = r.thighR; p.thighRx = r.thighRx; p.shinR = r.shinR;
            return p;
        }

        static P Get(PoseKind k, int frame)
        {
            switch (k)
            {
                case PoseKind.Stand: return new P { armL = 4, armLx = 10, foreL = 12, armR = 4, armRx = 10, foreR = 12, thighLx = 3, thighRx = 3 };
                case PoseKind.Ready: return new P { drop = 13, lean = 24, head = -14, armL = 25, armLx = 18, foreL = 45, armR = 25, armRx = 18, foreR = 45, thighL = 18, thighLx = 9, shinL = -30, thighR = 18, thighRx = 9, shinR = -30 };
                // a lineman's set (concept 01's 68 and 64): hips low and knees wide, chest up so the number shows, gloves out wide
                case PoseKind.Stance: return new P { drop = 23, lean = 26, head = -12, armL = 50, armLx = 48, foreL = 42, armR = 50, armRx = 48, foreR = 42, thighL = 62, thighLx = 30, shinL = -80, thighR = 62, thighRx = 30, shinR = -80 };
                case PoseKind.Block: return frame % 2 == 0
                        ? new P { drop = 20, lean = 42, head = -30, armL = 82, armLx = 10, foreL = 12, armR = 82, armRx = 10, foreR = 12, thighL = 48, thighLx = 10, shinL = -55, thighR = -12, thighRx = 10, shinR = -24 }
                        : new P { drop = 18, lean = 46, head = -32, armL = 92, armLx = 10, foreL = 4, armR = 92, armRx = 10, foreR = 4, thighL = -12, thighLx = 10, shinL = -24, thighR = 48, thighRx = 10, shinR = -55 };   // the shove: arms out, legs driving
                // arms locked with the man in front (FEATURES.blockLock): chest to chest, both arms out level with his shoulder
                // pads, elbows a little bent; 0 and 1 the legs drive and the hands fight for the inside
                case PoseKind.Lock: return frame % 2 == 0
                        ? new P { drop = 18, lean = 34, head = -26, armL = 108, armLx = 4, foreL = 24, armR = 100, armRx = 4, foreR = 30, thighL = 46, thighLx = 10, shinL = -52, thighR = -14, thighRx = 10, shinR = -26 }
                        : new P { drop = 17, lean = 36, head = -28, armL = 100, armLx = 4, foreL = 30, armR = 108, armRx = 4, foreR = 24, thighL = -14, thighLx = 10, shinL = -26, thighR = 46, thighRx = 10, shinR = -52 };
                case PoseKind.Throw: return new P { drop = 4, lean = -6, head = 0, armL = 62, armLx = 20, foreL = 30, armR = -150, armRx = 22, foreR = 70, thighL = 24, thighLx = 6, shinL = -18, thighR = -18, thighRx = 8, shinR = -12 };
                case PoseKind.Carry: return frame % 2 == 0
                        ? new P { drop = 6, lean = 20, head = -10, armL = 30, armLx = 8, foreL = 100, armR = -30, armRx = 12, foreR = 70, thighL = 48, thighLx = 4, shinL = -70, thighR = -28, thighRx = 4, shinR = -40 }
                        : new P { drop = 6, lean = 20, head = -10, armL = 30, armLx = 8, foreL = 100, armR = 35, armRx = 12, foreR = 70, thighL = -28, thighLx = 4, shinL = -40, thighR = 48, thighRx = 4, shinR = -70 };
                case PoseKind.Run: return frame % 2 == 0
                        ? new P { drop = 5, lean = 16, head = -8, armL = -38, armLx = 12, foreL = 75, armR = 45, armRx = 12, foreR = 75, thighL = 46, thighLx = 4, shinL = -66, thighR = -26, thighRx = 4, shinR = -42 }
                        : new P { drop = 5, lean = 16, head = -8, armL = 45, armLx = 12, foreL = 75, armR = -38, armRx = 12, foreR = 75, thighL = -26, thighLx = 4, shinL = -42, thighR = 46, thighRx = 4, shinR = -66 };
                case PoseKind.Backpedal: return new P { drop = 15, lean = 24, head = -16, armL = 30, armLx = 22, foreL = 60, armR = 30, armRx = 22, foreR = 60, thighL = frame % 2 == 0 ? 30 : 6, thighLx = 12, shinL = -40, thighR = frame % 2 == 0 ? 6 : 30, thighRx = 12, shinR = -40 };
                case PoseKind.Dive: return new P { drop = -8, lean = 70, head = -40, armL = 160, armLx = 14, foreL = 10, armR = 160, armRx = 14, foreR = 10, thighL = -30, thighLx = 6, shinL = -30, thighR = -45, thighRx = 6, shinR = -10 };
                case PoseKind.Catch: return new P { drop = 2, lean = -10, head = 18, armL = 165, armLx = 12, foreL = 20, armR = 165, armRx = 12, foreR = 20, thighL = 30, thighLx = 6, shinL = -50, thighR = -10, thighRx = 6, shinR = -20 };
                // the throw: 0 the arm comes over the top, 1 the follow-through across the body
                case PoseKind.Release: return frame == 0
                        ? new P { drop = 6, lean = 18, head = -8, armL = -30, armLx = 20, foreL = 40, armR = 150, armRx = 15, foreR = 20, thighL = 30, thighLx = 6, shinL = -20, thighR = -25, thighRx = 8, shinR = -15 }
                        : new P { drop = 10, lean = 32, head = -14, armL = -40, armLx = 20, foreL = 60, armR = 50, armRx = 30, foreR = 30, thighL = 35, thighLx = 6, shinL = -30, thighR = -30, thighRx = 8, shinR = -40 };
                case PoseKind.Handoff: return new P { drop = 10, lean = 22, head = -10, armL = 60, armLx = 25, foreL = 40, armR = 70, armRx = 25, foreR = 30, thighL = 24, thighLx = 9, shinL = -30, thighR = 10, thighRx = 9, shinR = -28 };
                // hands up for a pass coming in, on the run
                case PoseKind.Reach: return RunLegs(new P { drop = 4, lean = 4, head = -20, armL = 140, armLx = 14, foreL = 25, armR = 140, armRx = 14, foreR = 25 }, frame);
                // caught: the ball pulled in to the chest with both arms
                case PoseKind.Secure: return RunLegs(new P { drop = 8, lean = 20, head = -6, armL = 60, armLx = 6, foreL = 120, armR = 50, armRx = 8, foreR = 110 }, frame);
                // a drop: 0 hands wide as it pops out, 1 head down
                case PoseKind.Bobble: return frame == 0
                        ? new P { drop = 4, lean = -8, head = -20, armL = 150, armLx = 45, foreL = 30, armR = 150, armRx = 45, foreR = 30, thighL = 20, thighLx = 6, shinL = -30, thighR = -6, thighRx = 6, shinR = -20 }
                        : new P { drop = 6, lean = 30, head = 20, armL = -5, armLx = 8, foreL = 10, armR = -5, armRx = 8, foreR = 10, thighL = 8, thighLx = 4, shinL = -10, thighR = 8, thighRx = 4, shinR = -10 };
                // a juke: 0 the plant, low and wide, the free arm out; 1 the push off the other way
                case PoseKind.Juke: return frame == 0
                        ? new P { drop = 18, lean = 22, head = -14, armL = 50, armLx = 55, foreL = 20, armR = -20, armRx = 12, foreR = 90, thighL = 30, thighLx = 30, shinL = -40, thighR = -10, thighRx = 25, shinR = -30 }
                        : new P { drop = 10, lean = 28, head = -16, armL = 30, armLx = 40, foreL = 40, armR = -20, armRx = 12, foreR = 90, thighL = -20, thighLx = 10, shinL = -50, thighR = 50, thighRx = 5, shinR = -70 };
                // a missed tackle: arms everywhere, stumbling steps
                case PoseKind.Whiff: return frame % 2 == 0
                        ? new P { drop = 10, tilt = 25, lean = 0, head = -20, armL = 120, armLx = 40, foreL = 30, armR = -40, armRx = 40, foreR = 50, thighL = 50, thighLx = 6, shinL = -40, thighR = -30, thighRx = 6, shinR = -60 }
                        : new P { drop = 10, tilt = 20, lean = 0, head = -20, armL = -40, armLx = 40, foreL = 50, armR = 120, armRx = 40, foreR = 30, thighL = -30, thighLx = 6, shinL = -60, thighR = 50, thighRx = 6, shinR = -40 };
                // knocked off balance by a glancing blow (FEATURES.hitAngle), the ball still tucked: 0 tipped over to
                // one side, the free arm thrown out, a foot hooked; 1 a wide catch step, still leaning
                case PoseKind.Stagger: return frame == 0
                        ? new P { drop = 10, tilt = 28, lean = 12, head = 10, armL = 110, armLx = 60, foreL = 20, armR = -30, armRx = 12, foreR = 70, thighL = 40, thighLx = 20, shinL = -60, thighR = -15, thighRx = 4, shinR = -20 }
                        : new P { drop = 14, tilt = 18, lean = 20, head = -5, armL = 70, armLx = 70, foreL = 30, armR = -30, armRx = 12, foreR = 70, thighL = -20, thighLx = 30, shinL = -30, thighR = 45, thighRx = 8, shinR = -55 };
                // going down: 0 the hit, 1 halfway to the grass
                case PoseKind.FallFront: return frame == 0
                        ? new P { drop = 6, tilt = 25, head = -20, armL = 120, armLx = 30, foreL = 30, armR = 100, armRx = 30, foreR = 40, thighL = 10, thighLx = 6, shinL = -30, thighR = -20, thighRx = 6, shinR = -50 }
                        : new P { drop = 22, tilt = 50, head = -30, armL = 150, armLx = 18, foreL = 20, armR = 150, armRx = 18, foreR = 20, thighL = 0, thighLx = 6, shinL = -40, thighR = -10, thighRx = 6, shinR = -60 };
                case PoseKind.FallBack: return frame == 0
                        ? new P { drop = 6, tilt = -20, head = 20, armL = 70, armLx = 50, foreL = 40, armR = 70, armRx = 50, foreR = 40, thighL = 30, thighLx = 8, shinL = -20, thighR = 10, thighRx = 8, shinR = -10 }
                        : new P { drop = 22, tilt = -45, head = 25, armL = 60, armLx = 65, foreL = 30, armR = 60, armRx = 65, foreR = 30, thighL = 40, thighLx = 10, shinL = -40, thighR = 20, thighRx = 10, shinR = -30 };
                // on the grass: face down, arms stretched out ahead (the carrier still has the ball), feet kicked up
                case PoseKind.Prone: return new P { drop = 42, tilt = 80, head = -40, armL = 170, armLx = 16, foreL = 10, armR = 170, armRx = 16, foreR = 10, thighL = -10, thighLx = 8, shinL = -50, thighR = -10, thighRx = 12, shinR = -30 };
                // on the grass: flat on his back, arms flung out wide on the grass, knees up
                case PoseKind.Supine: return new P { drop = 46, tilt = -88, head = 20, armL = -25, armLx = 75, foreL = 15, armR = -25, armRx = 75, foreR = 15, thighL = 40, thighLx = 10, shinL = -90, thighR = 30, thighRx = 14, shinR = -70 };
                // getting up: one knee down
                case PoseKind.Kneel: return new P { drop = 30, tilt = 15, head = -10, armL = 60, armLx = 10, foreL = 40, armR = 30, armRx = 14, foreR = 60, thighL = 80, thighLx = 6, shinL = -90, thighR = -10, thighRx = 6, shinR = -100 };
                // a touchdown: 0 a hop with both arms up, 1 the ball held high and a fist pump
                case PoseKind.Celebrate: return frame % 2 == 0
                        ? new P { drop = -6, lean = -6, head = -16, armL = 175, armLx = 20, foreL = 10, armR = 175, armRx = 20, foreR = 10, thighL = 30, thighLx = 6, shinL = -60, thighR = 10, thighRx = 6, shinR = -40 }
                        : new P { drop = 0, lean = 0, head = -10, armL = 40, armLx = 40, foreL = 80, armR = 170, armRx = 15, foreR = 10, thighL = 4, thighLx = 4, shinL = -6, thighR = 4, thighRx = 4, shinR = -6 };
                case PoseKind.Cheer: return CheerPose(frame);
            }
            return default;
        }

        // The cheer poses (X spreads an arm out to the side: 90 straight out, 180 straight up):
        // 0 ready, pom-poms low; 1 high V; 2 low V; 3 T; 4 both arms straight up; 5 a high kick, arms out in a T;
        // 6 a tuck jump with a high V; 7 an L (left arm up, right arm out).
        static P CheerPose(int frame)
        {
            switch (((frame % CheerFrames) + CheerFrames) % CheerFrames)
            {
                case 1: return new P { head = 8, armL = 8, armLx = 140, foreL = 0, armR = 8, armRx = 140, foreR = 0, thighLx = 3, thighRx = 3 };
                case 2: return new P { armL = 10, armLx = 42, foreL = 0, armR = 10, armRx = 42, foreR = 0, thighLx = 3, thighRx = 3 };
                case 3: return new P { armL = 0, armLx = 90, foreL = 0, armR = 0, armRx = 90, foreR = 0, thighLx = 3, thighRx = 3 };
                case 4: return new P { drop = -3, head = 10, armL = 172, armLx = 6, foreL = 0, armR = 172, armRx = 6, foreR = 0, thighLx = 3, thighRx = 3 };
                case 5: return new P { lean = -6, armL = 0, armLx = 90, foreL = 0, armR = 0, armRx = 90, foreR = 0, thighL = 80, thighLx = 4, shinL = -4, thighRx = 3 };
                case 6: return new P { drop = -18, head = 8, armL = 8, armLx = 150, foreL = 0, armR = 8, armRx = 150, foreR = 0, thighL = 40, thighLx = 8, shinL = -95, thighR = 40, thighRx = 8, shinR = -95 };
                case 7: return new P { armL = 6, armLx = 170, foreL = 0, armR = 0, armRx = 90, foreR = 0, thighLx = 3, thighRx = 3 };
            }
            return new P { armL = 6, armLx = 16, foreL = 18, armR = 6, armRx = 16, foreR = 18, thighLx = 3, thighRx = 3 };
        }

        PoseKind current = (PoseKind)(-1);
        int currentFrame = -1;
        P cur;
        float twist, headYaw, headUp, headTip; bool looking;
        float breath, shift;   // Idle

        // FEATURES.idleMotion (Alive.Idle): the hips sway idleSwayCm over the foot he leans on (dropping a little on the
        // other side, that knee easing), the legs angled back so the feet stay put; the chest comes up breathDeg.
        // Idle(0, 0): still. Takes hold at the next Pose.
        public void Idle(float breathDeg, float weightShift) { breath = breathDeg; shift = weightShift; }
        public float SwayCm, RollDeg, KneeDeg;   // (set once from anim.idleSwayCm / idleRollDeg / idleKneeDeg)

        public void Pose(PoseKind kind, int frame)
        {
            if (kind != current || frame != currentFrame)
            {
                current = kind; currentFrame = frame;
                var p = cur = Get(kind, frame);
                ArmL.localRotation = Quaternion.Euler(-p.armLx, 0, p.armL);
                ArmR.localRotation = Quaternion.Euler(p.armRx, 0, p.armR);
                ForeL.localRotation = Quaternion.Euler(0, 0, p.foreL);
                ForeR.localRotation = Quaternion.Euler(0, 0, p.foreR);
            }
            // the hips and legs: the pose's, swayed by Idle (+ shift: his weight on his left foot, the figure's +z)
            float sway = shift * SwayCm, roll = -shift * RollDeg;
            float keep = Mathf.Atan2(sway, Mathf.Max(hipY - cur.drop, 1f)) * Mathf.Rad2Deg - roll;   // (the feet stay put)
            Hips.localPosition = new Vector3(0, hipY - cur.drop, sway);
            Hips.localRotation = Quaternion.Euler(roll, 0, -cur.tilt);
            ThighL.localRotation = Quaternion.Euler(-cur.thighLx + keep, 0, cur.thighL);
            ThighR.localRotation = Quaternion.Euler(cur.thighRx + keep, 0, cur.thighR);
            ShinL.localRotation = Quaternion.Euler(0, 0, cur.shinL - Mathf.Max(0f, -shift) * KneeDeg);
            ShinR.localRotation = Quaternion.Euler(0, 0, cur.shinR - Mathf.Max(0f, shift) * KneeDeg);
            // the chest and head: the pose's, turned by Look, the chest up by the breath and back level over the hips
            Torso.localRotation = Quaternion.Euler(0, twist, 0) * Quaternion.Euler(-roll, 0, 0) * Quaternion.Euler(0, 0, -(cur.lean - breath));
            float head = looking ? Mathf.Clamp(-headUp - cur.lean - cur.tilt, -60f, 35f) : cur.head - headTip;
            Head.localRotation = Quaternion.Euler(0, headYaw, 0) * Quaternion.Euler(0, 0, -head);
        }

        // Turn the chest twistDeg and the head yawDeg more about the spine (+ = toward his right), the head tipped to look
        // upDeg above level (FEATURES.watchBall: following a pass). Look(0, 0, float.NaN): the pose's own chest and head.
        // Takes hold at the next Pose.
        public void Look(float twistDeg, float yawDeg, float upDeg)
        {
            twist = twistDeg; headYaw = yawDeg; headTip = 0f;
            looking = !float.IsNaN(upDeg);
            headUp = looking ? upDeg : 0f;
        }

        // FEATURES.headTrack (Alive.HeadTrack): only the head turns yawDeg (+ = toward his right) and tips tipDeg up
        // from the pose's own (- = down). Takes hold at the next Pose; the next Look puts it back.
        public void Track(float yawDeg, float tipDeg)
        {
            twist = 0f; headYaw = yawDeg; headTip = tipDeg; looking = false; headUp = 0f;
        }

        public PoseKind Current => current;
        public int CurrentFrame => currentFrame;

        // A football: brown, pointed, white laces. Rig units (cm), long axis along +z.
        // The football, lengthM long, centred, its long axis along +z and the laces up. model (FEATURES.modelBall,
        // NO_MODEL_BALL=1): Kenny's Hi3D football slimmed to 1,840 triangles and one colour map (Resources/Ball, its
        // import settings in Editor/BallImport.cs); without it, or if it's missing, the one built from code.
        public static GameObject MakeBall(Transform parent, Material mat, float lengthM, bool model = false)
        {
            if (model)
            {
                var m = ModelBall(parent, mat, lengthM);
                if (m != null) return m;
            }
            var k = new FigureKit();
            k.Flag = FigureKit.Shiny;
            var r = new Vector3(8.5f, 8.5f, 14f);
            k.Ellipsoid(Vector3.zero, r, Quaternion.identity, Colors.Hex("#8a4a24"), 12, 18);
            k.Flag = FigureKit.Matte;
            var top = FigureKit.OnEllipsoid(Vector3.zero, r, Quaternion.identity, Vector3.up, 1f, Vector3.forward);
            k.Stroke(top, new[] { new Vector2(0, -5), new Vector2(0, 5) }, new[] { 1.2f, 1.2f }, 0.3f, Color.white, 2, FigureKit.Paint);
            for (int i = -2; i <= 2; i++) k.Stroke(top, new[] { new Vector2(-1.8f, i * 2.2f), new Vector2(1.8f, i * 2.2f) }, new[] { 0.9f, 0.9f }, 0.35f, Color.white, 1, FigureKit.Paint);
            var go = Looks.Static(parent, "Ball", k, mat);
            go.transform.localScale = Vector3.one * lengthM / 28f;
            return go;
        }

        public const string BallModel = "Ball/Football", BallTexture = "Ball/Football_BaseColor";
        static Mesh ballMesh;
        static Material ballMat, ballMatFrom;

        static GameObject ModelBall(Transform parent, Material mat, float lengthM)
        {
            if (ballMesh == null)
            {
                var src = Resources.Load<GameObject>(BallModel);
                var mf = src != null ? src.GetComponentInChildren<MeshFilter>() : null;
                ballMesh = mf != null ? mf.sharedMesh : null;
                if (ballMesh == null) { Debug.LogWarning("[model] no football at Resources/" + BallModel + "; the ball is built from code"); return null; }
            }
            if (ballMat == null || ballMatFrom != mat)
            {
                var tex = Resources.Load<Texture2D>(BallTexture);
                if (tex == null) Debug.LogWarning("[model] no colour map at Resources/" + BallTexture + "; the football shows untextured");
                ballMatFrom = mat;
                ballMat = new Material(mat) { name = "Toon (football)" };
                if (tex != null) ballMat.SetTexture("_MainTex", tex);
                ballMat.SetColor("_PlayerColor", Color.white);
            }
            var go = new GameObject("Ball");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ballMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ballMat;
            go.transform.localScale = Vector3.one * lengthM / Mathf.Max(ballMesh.bounds.size.z, 1e-4f);
            return go;
        }
    }
}
