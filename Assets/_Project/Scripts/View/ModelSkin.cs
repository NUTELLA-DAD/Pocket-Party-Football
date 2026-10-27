using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // A rigged model worn over a FootballFigure (FEATURES.modelPlayers, NO_MODEL_PLAYERS=1 turns it off): a team with
    // "model" in game.json (a Humanoid FBX under Resources, Unity's own bone names) shows it instead of the figure
    // built from code; a position listed in the team's "posModels" wears its own model (FEATURES.posModels,
    // NO_POS_MODELS=1: every position wears the team's model). The figure keeps its 11 bones and every pose (FootballFigure.Get, the moves, the celebrations);
    // only its mesh is hidden. Once a frame, after everything has posed the figure, the model's bones copy the
    // figure's bones' turns. The model's arms are first lowered to hang like the figure's at rest, so a pose reads
    // the same on both. HandL/HandR move onto the model's gloves, so the ball sits in its hands.
    [DefaultExecutionOrder(10000)]
    public sealed class ModelSkin : MonoBehaviour
    {
        const float ArmOutDeg = 12f;   // the lowered arms hang this far out from the side (the shoulder pads are wide)
        const float PalmAlongHand = 0.45f;   // the ball sits this far past the wrist, in forearm lengths
        const float RaisedSplayDeg = 35f;   // an arm straight up swings this far out, clear of the big helmet

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        FootballFigure fig;
        Transform[] from, to;
        Quaternion[] offset;   // model bone = figure bone * offset (world turns)
        Vector3 hipOffset;     // the model's hips in the figure's Hips space

        public static bool Wear(FootballFigure fig, TeamConfig team, string pos, GameConfig cfg, Material kit)
        {
            if (!cfg.Feature("modelPlayers")) return false;
            team.ModelFor(pos, cfg.Feature("posModels"), out string model, out string modelTexture);
            if (string.IsNullOrEmpty(model)) return false;
            if (!prefabs.TryGetValue(model, out var prefab))
            {
                prefabs[model] = prefab = Resources.Load<GameObject>(model);
                if (prefab == null) Debug.LogWarning("[model] no model at Resources/" + model + "; the player keeps the figure built from code");
            }
            if (prefab == null) return false;

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
            };
            var b = new Dictionary<string, Transform>();
            foreach (var n in names) b[n] = Find(inst.transform, n);
            foreach (var kv in b)
                if (kv.Value == null || smr == null)
                {
                    Debug.LogWarning("[model] " + prefab.name + " has no " + (smr == null ? "skinned mesh" : "bone " + kv.Key) + "; the team keeps the figure built from code");
                    DestroyNow(pivot.gameObject);
                    return false;
                }

            // the figure at rest (every bone straight, hips at their own height) while the two are lined up
            fig.Pose((FootballFigure.PoseKind)(-1), 0);
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            smr.enabled = true;

            // face the figure's way (+x): the toes point forward
            Vector3 fwd = holder.InverseTransformDirection(b["LeftToes"].position - b["LeftFoot"].position);
            fwd.y = 0f;
            pivot.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(fwd, Vector3.right, Vector3.up), Vector3.up);
            // the figure's height, feet on its origin
            Bounds hb = BoundsIn(holder, smr);
            pivot.localScale = Vector3.one * FootballFigure.RigHeight / Mathf.Max(hb.size.y, 1e-4f);
            hb = BoundsIn(holder, smr);
            pivot.localPosition = new Vector3(-hb.center.x, -hb.min.y, -hb.center.z);

            // the arms down by the sides, like the figure's at rest
            Vector3 down = -holder.up;
            foreach (var side in new[] { ("Left", 1f), ("Right", -1f) })
            {
                Transform up = b[side.Item1 + "UpperArm"], lo = b[side.Item1 + "LowerArm"];
                Vector3 want = Quaternion.AngleAxis(-side.Item2 * ArmOutDeg, holder.right) * down;
                up.rotation = Quaternion.FromToRotation(lo.position - up.position, want) * up.rotation;
            }

            var skin = inst.AddComponent<ModelSkin>();
            skin.fig = fig;
            skin.from = new[] { fig.Hips, fig.Torso, fig.Head, fig.ArmL, fig.ForeL, fig.ArmR, fig.ForeR, fig.ThighL, fig.ShinL, fig.ThighR, fig.ShinR };
            skin.to = new[]
            {
                b["Hips"], b["Spine"], b["Head"], b["LeftUpperArm"], b["LeftLowerArm"], b["RightUpperArm"], b["RightLowerArm"],
                b["LeftUpperLeg"], b["LeftLowerLeg"], b["RightUpperLeg"], b["RightLowerLeg"],
            };
            skin.offset = new Quaternion[skin.from.Length];
            for (int i = 0; i < skin.from.Length; i++) skin.offset[i] = Quaternion.Inverse(skin.from[i].rotation) * skin.to[i].rotation;
            skin.hipOffset = fig.Hips.InverseTransformPoint(b["Hips"].position);

            // the ball goes in the model's gloves, turned as it was in the figure's hands
            fig.HandL = Glove(b["LeftHand"], b["LeftLowerArm"], fig.HandL.rotation);
            fig.HandR = Glove(b["RightHand"], b["RightLowerArm"], fig.HandR.rotation);

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
            return true;
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            if (fig == null) return;
            to[0].position = fig.Hips.TransformPoint(hipOffset);
            for (int i = 0; i < from.Length; i++) to[i].rotation = from[i].rotation * offset[i];
            Splay(3, fig.ArmL, fig.ForeL, 1f);
            Splay(5, fig.ArmR, fig.ForeR, -1f);
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

        static Transform Glove(Transform hand, Transform lower, Quaternion turn)
        {
            var g = new GameObject("Glove").transform;
            g.SetParent(hand, false);
            g.position = hand.position + (hand.position - lower.position) * PalmAlongHand;
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
