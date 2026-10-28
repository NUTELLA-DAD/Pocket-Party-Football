using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The rigged model every player wears over his FootballFigure (the invisible pose rig): the team's "model" in game.json
    // (a Humanoid FBX under Resources, Unity's own bone names); a position listed in the team's "posModels" wears its
    // own model (FEATURES.posModels, NO_POS_MODELS=1: every position wears the team's model). The figure keeps its 11
    // bones and every pose (FootballFigure.Get, the moves, the celebrations). Once a frame, after everything has posed the figure, the model's bones copy the
    // figure's bones' turns. The model's arms are first lowered to hang like the figure's at rest, so a pose reads
    // the same on both. HandL/HandR move onto the model's gloves, so the ball sits in its hands.
    [DefaultExecutionOrder(10000)]
    public sealed class ModelSkin : MonoBehaviour
    {
        const float ArmOutDeg = 12f;   // the lowered arms hang this far out from the side (the shoulder pads are wide)
        const float PalmAlongHand = 0.55f;   // the ball sits this far past the wrist, in forearm lengths
        const float PalmIn = 0.45f;          // and this far in front of the palm, in forearm lengths (cupped in the glove)
        const float RaisedSplayDeg = 35f;   // an arm straight up swings this far out, clear of the big helmet
        // The model's arms are short under a big helmet, so a few of the figure's arm angles read badly on it
        // (rig gauntlet, docs/progress/rig): a lowered arm's extra spread past SpreadFreeDeg is cut to SpreadKeep (the
        // line-up's stiff wide arms), a bent forearm under a lowered arm stops FoldCapDeg above level (the ball stayed on the
        // facemask in Secure and Carry), and the shoulder (with its pad) lifts ClavicleShare of the arm's rise past
        // ClavicleFromDeg, so a raised arm doesn't crush the pad.
        const float SpreadFreeDeg = 18f, SpreadKeep = 0.45f, FoldCapDeg = 15f, ClavicleFromDeg = -20f, ClavicleShare = 0.22f;
        // The figure has no feet, so the model's feet follow its shins: a foot near the grass turns FootLevel of the
        // way back to its standing pitch (soles flat in the stances, not toes-up), unless the body is falling over.
        const float FootLevel = 0.7f, FootNearCm = 22f, FootLevelMaxTiltDeg = 30f;

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        FootballFigure fig;
        Transform[] from, to;
        Quaternion[] offset;   // model bone = figure bone * offset (world turns)
        Vector3 hipOffset;     // the model's hips in the figure's Hips space
        Transform shoulderL, shoulderR, handL, handR;
        Transform[] feet, toes;
        float[] footPitch;     // each foot's pitch (toes above the heel, degrees) standing at rest
        Quaternion[] footRest, toeRest;
        Quaternion shoulderRestL, shoulderRestR;
        // FEATURES.handCurl: the finger bones (left then right: fingers, fingertips, thumb, thumb tip), each one's rest
        // turn and the axis it curls toward the palm about (its own space); none on a model without them
        Transform[] fingers;
        Quaternion[] fingerRest;
        Vector3[] fingerAxis;
        int[] fingerKey;       // which of the four curls each one takes (0 fingers, 1 fingertips, 2 thumb, 3 thumb tip)
        bool[] fingerLeft;
        float[] curlOpen, curlRelaxed, curlShut;

        bool fixArms = true;   // the helmeted players' arm fixes (Adapt, Splay); the cheerleaders have no helmet

        public static bool Wear(FootballFigure fig, TeamConfig team, string pos, GameConfig cfg, Material kit)
        {
            team.ModelFor(pos, cfg.Feature("posModels"), out string model, out string modelTexture);
            if (string.IsNullOrEmpty(model)) { Debug.LogError("[model] team " + team.@short + " has no \"model\" in game.json; its players don't show"); return false; }
            var skin = Wear(fig, model, modelTexture, kit, true);
            if (skin != null && cfg.Feature("handCurl")) skin.Hands(cfg.anim);
            return skin != null;
        }

        // Any rigged Humanoid model (Resources path) and colour map on the figure; null if it's missing (logged).
        // fixArms: the arm fixes for a model with a big helmet and short arms.
        public static ModelSkin Wear(FootballFigure fig, string model, string modelTexture, Material kit, bool fixArms)
        {
            if (string.IsNullOrEmpty(model)) return null;
            if (!prefabs.TryGetValue(model, out var prefab))
            {
                prefabs[model] = prefab = Resources.Load<GameObject>(model);
                if (prefab == null) Debug.LogWarning("[model] no model at Resources/" + model + "; the player doesn't show");
            }
            if (prefab == null) return null;   // (logged once, above)

            var holder = fig.transform;
            var pivot = new GameObject("Model").transform;
            pivot.SetParent(holder, false);
            var inst = Instantiate(prefab, pivot, false);
            inst.name = prefab.name;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();

            string[] names =
            {
                "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm",
                "LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg", "LeftHand", "RightHand", "LeftFoot", "LeftToes",
                "LeftShoulder", "RightShoulder", "RightFoot", "RightToes",
            };
            var b = new Dictionary<string, Transform>();
            foreach (var n in names) b[n] = Find(inst.transform, n);
            foreach (var kv in b)
                if (kv.Value == null || smr == null)
                {
                    Debug.LogError("[model] " + prefab.name + " has no " + (smr == null ? "skinned mesh" : "bone " + kv.Key) + "; the player doesn't show");
                    DestroyNow(pivot.gameObject);
                    return null;
                }

            // the figure at rest (every bone straight, hips at their own height) while the two are lined up
            fig.Pose((FootballFigure.PoseKind)(-1), 0);

            // face the figure's way (+x): the toes point forward
            Vector3 fwd = holder.InverseTransformDirection(b["LeftToes"].position - b["LeftFoot"].position);
            fwd.y = 0f;
            pivot.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(fwd, Vector3.right, Vector3.up), Vector3.up);
            // the figure's height, feet on its origin
            Bounds hb = BoundsIn(holder, smr);
            pivot.localScale = Vector3.one * FootballFigure.RigHeight / Mathf.Max(hb.size.y, 1e-4f);
            hb = BoundsIn(holder, smr);
            pivot.localPosition = new Vector3(-hb.center.x, -hb.min.y, -hb.center.z);

            // the finger bones' curl axes, in the T-pose (palms down: each curls about its own length x down)
            Vector3 down = -holder.up;
            var fingerBones = new List<Transform>();
            var fingerAxes = new List<Vector3>();
            var fingerKeys = new List<int>();
            var fingerSides = new List<bool>();
            foreach (var side in new[] { "Left", "Right" })
                foreach (var digit in new[] { "Fingers", "Thumb" })
                {
                    Transform root = Find(inst.transform, side + digit), tip = Find(inst.transform, side + digit + "Tip");
                    if (root == null || tip == null) continue;
                    Vector3 along = tip.position - root.position;
                    Vector3 axis = Vector3.Cross(along, down);
                    if (axis.sqrMagnitude < 1e-12f) continue;
                    // (the tip carries on the way its bone points: it has no bone past it to measure by)
                    int key = digit == "Thumb" ? 2 : 0;
                    foreach (var bone in new[] { root, tip })
                    {
                        fingerBones.Add(bone); fingerAxes.Add(Quaternion.Inverse(bone.rotation) * axis.normalized);
                        fingerKeys.Add(bone == root ? key : key + 1); fingerSides.Add(side == "Left");
                    }
                }

            // the arms down by the sides, like the figure's at rest (the T-pose's palms face down; they turn with the arm)
            var palm = new Dictionary<string, Vector3>();
            foreach (var side in new[] { ("Left", 1f), ("Right", -1f) })
            {
                Transform up = b[side.Item1 + "UpperArm"], lo = b[side.Item1 + "LowerArm"];
                Vector3 want = Quaternion.AngleAxis(-side.Item2 * ArmOutDeg, holder.right) * down;
                var turn = Quaternion.FromToRotation(lo.position - up.position, want);
                up.rotation = turn * up.rotation;
                palm[side.Item1] = turn * down;
            }

            // the model's own leg, for the stride's reach (FEATURES.strideCycle): the legs' turns are the figure's, so a
            // foot lands where the model's leg puts it
            fig.Legs = LegOf(holder, b["LeftUpperLeg"], b["LeftLowerLeg"], b["LeftFoot"], b["LeftToes"]);

            var skin = inst.AddComponent<ModelSkin>();
            skin.fig = fig;
            skin.fixArms = fixArms;
            skin.from = new[] { fig.Hips, fig.Torso, fig.Head, fig.ArmL, fig.ForeL, fig.ArmR, fig.ForeR, fig.ThighL, fig.ShinL, fig.ThighR, fig.ShinR };
            skin.to = new[]
            {
                b["Hips"], b["Spine"], b["Head"], b["LeftUpperArm"], b["LeftLowerArm"], b["RightUpperArm"], b["RightLowerArm"],
                b["LeftUpperLeg"], b["LeftLowerLeg"], b["RightUpperLeg"], b["RightLowerLeg"],
            };
            skin.offset = new Quaternion[skin.from.Length];
            for (int i = 0; i < skin.from.Length; i++) skin.offset[i] = Quaternion.Inverse(skin.from[i].rotation) * skin.to[i].rotation;
            skin.hipOffset = fig.Hips.InverseTransformPoint(b["Hips"].position);
            skin.shoulderL = b["LeftShoulder"]; skin.shoulderR = b["RightShoulder"];
            skin.handL = b["LeftHand"]; skin.handR = b["RightHand"];
            skin.feet = new[] { b["LeftFoot"], b["RightFoot"] };
            skin.toes = new[] { b["LeftToes"], b["RightToes"] };
            skin.footPitch = new float[2];
            skin.footRest = new[] { skin.feet[0].localRotation, skin.feet[1].localRotation };
            skin.toeRest = new[] { skin.toes[0].localRotation, skin.toes[1].localRotation };
            for (int i = 0; i < 2; i++) skin.footPitch[i] = Pitch(skin.toes[i].position - skin.feet[i].position, holder.up);
            skin.shoulderRestL = skin.shoulderL.localRotation; skin.shoulderRestR = skin.shoulderR.localRotation;
            skin.fingers = fingerBones.ToArray();
            skin.fingerAxis = fingerAxes.ToArray();
            skin.fingerKey = fingerKeys.ToArray();
            skin.fingerLeft = fingerSides.ToArray();
            skin.fingerRest = new Quaternion[skin.fingers.Length];
            for (int i = 0; i < skin.fingers.Length; i++) skin.fingerRest[i] = skin.fingers[i].localRotation;

            // the ball goes in the model's gloves, turned as it was in the figure's hands
            fig.HandL = Glove(b["LeftHand"], b["LeftLowerArm"], fig.HandL.rotation, palm["Left"]);
            fig.HandR = Glove(b["RightHand"], b["RightLowerArm"], fig.HandR.rotation, palm["Right"]);

            string key = model + "/" + modelTexture + "/" + kit.GetInstanceID();   // (two teams, or two positions, can share a model)
            if (!mats.TryGetValue(key, out var m))
            {
                var tex = string.IsNullOrEmpty(modelTexture) ? null : Resources.Load<Texture2D>(modelTexture);
                if (tex == null) Debug.LogWarning("[model] no colour map at Resources/" + modelTexture + "; the model shows untextured");
                mats[key] = m = new Material(kit) { name = "Toon (" + prefab.name + ")" };
                if (tex != null) m.SetTexture("_MainTex", tex);
                m.SetColor("_PlayerColor", Color.white);
            }
            smr.sharedMaterial = m;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            smr.updateWhenOffscreen = false;
            var lb = smr.localBounds;
            smr.localBounds = new Bounds(lb.center, lb.size * 2f);   // (room for the dives and falls)

            fig.Pose(FootballFigure.PoseKind.Ready, 0);
            skin.Follow();
            return skin;
        }

        void LateUpdate() => Follow();

        // A player who never moves again (the sideline crowd), posed once: the model is baked into a plain mesh in the
        // pose (one bake shared by every player in the same pose) and the skinning and the rig go away.
        static readonly Dictionary<string, Mesh> frozen = new Dictionary<string, Mesh>();
        public static void Freeze(FootballFigure fig, FootballFigure.PoseKind pose)
        {
            var skin = fig.GetComponentInChildren<ModelSkin>();
            var smr = skin != null ? skin.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr == null) return;
            fig.Pose(pose, 0);
            skin.Follow();
            string key = smr.sharedMesh.GetInstanceID() + "/" + pose;
            if (!frozen.TryGetValue(key, out var mesh) || mesh == null)
            {
                frozen[key] = mesh = new Mesh { name = smr.sharedMesh.name + " (" + pose + ")" };
                smr.BakeMesh(mesh, true);   // (in the renderer's own space, its scale taken out; it comes back from the transform)
                mesh.RecalculateBounds();
            }
            var go = new GameObject(smr.name);
            go.transform.SetParent(smr.transform, false);
            go.transform.SetParent(fig.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = smr.sharedMaterial;
            r.shadowCastingMode = smr.shadowCastingMode;
            var gone = new List<GameObject>();   // (the rig's bones and the "Model" pivot with the skinned mesh)
            foreach (Transform t in fig.transform) if (t != go.transform) gone.Add(t.gameObject);
            foreach (var g in gone) DestroyNow(g);
            if (Application.isPlaying) Destroy(fig);
            else DestroyImmediate(fig);
        }

        // The model's bones copy the figure's pose (every frame; tests call it directly).
        public void Follow()
        {
            if (fig == null) return;
            to[0].position = fig.Hips.TransformPoint(hipOffset);
            shoulderL.localRotation = shoulderRestL;
            shoulderR.localRotation = shoulderRestR;
            feet[0].localRotation = footRest[0];
            feet[1].localRotation = footRest[1];
            toes[0].localRotation = toeRest[0];
            toes[1].localRotation = toeRest[1];
            for (int i = 0; i < from.Length; i++) to[i].rotation = from[i].rotation * offset[i];
            if (fixArms)
            {
                Adapt(to[3], to[4], handL, shoulderL, 1f);
                Adapt(to[5], to[6], handR, shoulderR, -1f);
                Splay(3, fig.ArmL, fig.ForeL, 1f);
                Splay(5, fig.ArmR, fig.ForeR, -1f);
            }
            LevelFeet();
            Point(0, fig.ToeL);
            Point(1, fig.ToeR);
            Bend(0, fig.BallL);
            Bend(1, fig.BallR);
            Curl();
        }

        // FEATURES.handCurl: the curls to take from game.json (off, the fingers stay as modelled)
        void Hands(AnimConfig a)
        {
            curlOpen = a.handCurlOpenDeg; curlRelaxed = a.handCurlRelaxedDeg; curlShut = a.handCurlShutDeg;
            Follow();
        }

        // Each hand's fingers curled to its grip (FootballFigure.GripL/R; a hand holding the ball shut).
        void Curl()
        {
            if (curlRelaxed == null || curlOpen == null || curlShut == null || fingers == null) return;
            for (int i = 0; i < fingers.Length; i++)
            {
                float grip = fingerLeft[i] ? (fig.HoldL ? 1f : fig.GripL) : (fig.HoldR ? 1f : fig.GripR);
                int k = fingerKey[i];
                if (k >= curlRelaxed.Length || k >= curlOpen.Length || k >= curlShut.Length) continue;
                float deg = grip >= 0f ? Mathf.Lerp(curlRelaxed[k], curlShut[k], grip) : Mathf.Lerp(curlRelaxed[k], curlOpen[k], -grip);
                fingers[i].localRotation = fingerRest[i] * Quaternion.AngleAxis(deg, fingerAxis[i]);
            }
        }

        // The model's knee and elbow bends as drawn (degrees between the two bones, after every fix), for the motion
        // reel's CSV: to check the figure's bends reach the model.
        public void Bends(out float kneeL, out float kneeR, out float elbowL, out float elbowR)
        {
            float At(Transform a, Transform b, Transform c) => Vector3.Angle(b.position - a.position, c.position - b.position);
            kneeL = At(to[7], to[8], feet[0]); kneeR = At(to[9], to[10], feet[1]);
            elbowL = At(to[3], to[4], handL); elbowR = At(to[5], to[6], handR);
        }

        // FEATURES.footFlex: the toes bent deg up (+) at the ball of the foot, about the foot's crosswise axis.
        void Bend(int i, float deg)
        {
            if (deg == 0f) return;
            Vector3 axis = Vector3.Cross(toes[i].position - feet[i].position, fig.transform.up);
            if (axis.sqrMagnitude < 1e-12f) return;
            toes[i].rotation = Quaternion.AngleAxis(deg, axis.normalized) * toes[i].rotation;
        }

        // The stride's push-off and heel strike: the foot pitched deg more (- = toes down), about its own crosswise axis.
        void Point(int i, float deg)
        {
            if (deg == 0f) return;
            Vector3 d = toes[i].position - feet[i].position;
            Vector3 axis = Vector3.Cross(d, fig.transform.up);
            if (axis.sqrMagnitude < 1e-12f) return;
            feet[i].rotation = Quaternion.AngleAxis(deg, axis.normalized) * feet[i].rotation;
        }

        // A model's leg standing, in the figure's units (rig cm, +x forward): the hip's height, the bones' lengths and
        // their angles from straight down, the ankle's height.
        static StrideLegs LegOf(Transform holder, Transform hip, Transform knee, Transform ankle, Transform ball)
        {
            Vector3 h = holder.InverseTransformPoint(hip.position), k = holder.InverseTransformPoint(knee.position), a = holder.InverseTransformPoint(ankle.position);
            Vector3 t = holder.InverseTransformPoint(ball.position);
            float foot = Vector3.Distance(a, t);
            float Down(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, from.y - to.y) * Mathf.Rad2Deg;
            return new StrideLegs
            {
                HipY = h.y, Thigh = Vector3.Distance(h, k), Shin = Vector3.Distance(k, a), Ankle = a.y,
                ThighRest = Down(h, k), ShinRest = Down(k, a),
                Foot = foot, FootDropDeg = foot > 1e-4f ? Mathf.Asin(Mathf.Clamp((a.y - t.y) / foot, -1f, 1f)) * Mathf.Rad2Deg : 0f,
            };
        }

        void LevelFeet()
        {
            var holder = fig.transform;
            if (Vector3.Angle(fig.Hips.up, holder.up) > FootLevelMaxTiltDeg) return;
            // level with the grass, not with the figure: a leaning figure (FEATURES.momentumLean) tips its holder over
            Vector3 grassUp = holder.parent != null ? holder.parent.up : Vector3.up;
            for (int i = 0; i < 2; i++)
            {
                if (holder.InverseTransformPoint(feet[i].position).y > FootNearCm) continue;
                Vector3 d = toes[i].position - feet[i].position;
                float delta = Pitch(d, grassUp) - footPitch[i];
                Vector3 axis = Vector3.Cross(d, grassUp);
                if (axis.sqrMagnitude < 1e-12f) continue;
                feet[i].rotation = Quaternion.AngleAxis(-FootLevel * delta, axis.normalized) * feet[i].rotation;
            }
        }

        static float Pitch(Vector3 d, Vector3 up) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized, up), -1f, 1f)) * Mathf.Rad2Deg;

        // The arm fixes for the model's proportions (see SpreadFreeDeg): narrow a wide arm, cap a folded forearm, lift
        // the shoulder under a raised arm. Angles are measured against the figure's torso.
        void Adapt(Transform upper, Transform lower, Transform hand, Transform shoulder, float side)
        {
            Vector3 up = fig.Torso.up, outward = fig.Torso.forward * side;   // (the figure's left is its +z)
            Vector3 d = (lower.position - upper.position).normalized;
            float lat = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, outward), -1f, 1f)) * Mathf.Rad2Deg;
            float rise = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, up), -1f, 1f)) * Mathf.Rad2Deg;
            bool upright = Vector3.Angle(fig.Hips.up, fig.transform.up) <= FootLevelMaxTiltDeg;
            if (lat > SpreadFreeDeg && rise < 0f && upright)   // (not a raised arm, nor arms flung out lying on the grass)
            {
                Vector3 axis = Vector3.Cross(d, outward);
                float cut = (lat - SpreadFreeDeg) * (1f - SpreadKeep);
                if (axis.sqrMagnitude > 1e-12f) upper.rotation = Quaternion.AngleAxis(-cut, axis.normalized) * upper.rotation;
            }

            Vector3 du = (lower.position - upper.position).normalized, df = (hand.position - lower.position).normalized;
            float eu = Mathf.Asin(Mathf.Clamp(Vector3.Dot(du, up), -1f, 1f)) * Mathf.Rad2Deg;
            float ef = Mathf.Asin(Mathf.Clamp(Vector3.Dot(df, up), -1f, 1f)) * Mathf.Rad2Deg;
            if (eu < 0f && Vector3.Angle(du, df) > 60f && ef > FoldCapDeg)
            {
                Vector3 axis = Vector3.Cross(df, up);
                if (axis.sqrMagnitude > 1e-12f) lower.rotation = Quaternion.AngleAxis(-(ef - FoldCapDeg), axis.normalized) * lower.rotation;
            }

            float lift = Mathf.Max(0f, eu - ClavicleFromDeg) * ClavicleShare;
            if (lift > 0f)
            {
                Quaternion keep = upper.rotation;
                Vector3 lat0 = (upper.position - shoulder.position).normalized;
                Vector3 axis = Vector3.Cross(lat0, du);
                if (axis.sqrMagnitude > 1e-12f)
                {
                    shoulder.rotation = Quaternion.AngleAxis(Mathf.Min(lift, Vector3.Angle(lat0, du)), axis.normalized) * shoulder.rotation;
                    upper.rotation = keep;   // (the arm keeps its direction; only its root rises)
                }
            }
        }

        // A raised arm swings out to the side, more the higher it goes: the model's shoulders sit inside its helmet's
        // width (the figure's pads are wider), so a straight copy of a hands-up pose would bury the arm in the helmet.
        void Splay(int upper, Transform arm, Transform fore, float side)
        {
            Vector3 dir = (fore.position - arm.position).normalized;
            float raised = Vector3.Dot(dir, fig.Torso.up);
            if (raised <= 0f) return;
            Vector3 outward = fig.Torso.forward * side;   // (the figure's left is its +z)
            var q = Quaternion.FromToRotation(dir, Vector3.RotateTowards(dir, outward, RaisedSplayDeg * Mathf.Deg2Rad * raised, 0f));
            to[upper].rotation = q * to[upper].rotation;   // (the forearm, its child, comes with it)
        }

        static Transform Glove(Transform hand, Transform lower, Quaternion turn, Vector3 palm)
        {
            var g = new GameObject("Glove").transform;
            g.SetParent(hand, false);
            Vector3 fore = hand.position - lower.position;
            g.position = hand.position + fore * PalmAlongHand + palm.normalized * (fore.magnitude * PalmIn);
            g.rotation = turn;
            return g;
        }

        // the model's bounds at rest, in the holder's space (at rest the skinned mesh sits where its renderer does)
        static Bounds BoundsIn(Transform holder, SkinnedMeshRenderer smr)
        {
            var mb = smr.sharedMesh.bounds;
            var toHolder = holder.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var hb = new Bounds(toHolder.MultiplyPoint3x4(mb.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                hb.Encapsulate(toHolder.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z)));
            return hb;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        static void DestroyNow(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
