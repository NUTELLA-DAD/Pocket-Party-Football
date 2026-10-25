using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // One capsule per player in team colours (grey stand-ins until the M4 art). Reads the sim, never writes it.
    // Positions are blended between the last two sim ticks so 30 Hz data moves smoothly at 60 fps.
    public sealed class PlayersView
    {
        readonly GameConfig cfg;
        readonly Material baseMat;
        readonly Transform root;
        readonly Dictionary<int, Transform> bodies = new Dictionary<int, Transform>();
        readonly Dictionary<int, int> teamOf = new Dictionary<int, int>();
        readonly Dictionary<int, Vector2> prev = new Dictionary<int, Vector2>();
        readonly List<Material> teamMats = new List<Material>();
        readonly List<int> gone = new List<int>();

        public PlayersView(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            this.baseMat = baseMat;
            root = new GameObject("Players").transform;
            foreach (var t in cfg.teams)
            {
                var m = new Material(baseMat) { name = "Team " + t.@short };
                m.SetColor("_BaseColor", Colors.Hex(t.color));
                m.SetFloat("_Smoothness", 0.2f);
                teamMats.Add(m);
            }
        }

        public int Count => bodies.Count;

        // Call just before World.Step: remembers where everyone was, for the blend.
        public void BeforeStep(World w)
        {
            prev.Clear();
            foreach (var p in w.Players) prev[p.Slot] = new Vector2(p.X, p.Z);
        }

        // alpha: 0 = the previous tick, 1 = the latest.
        public void Render(World w, float alpha)
        {
            float yd = cfg.field.yardM;
            foreach (var p in w.Players)
            {
                var body = Body(p);
                Vector2 now = new Vector2(p.X, p.Z);
                Vector2 was = prev.TryGetValue(p.Slot, out var v) ? v : now;
                Vector2 at = Vector2.Lerp(was, now, alpha);
                body.localPosition = new Vector3(at.x * yd, cfg.view.playerHeightM * 0.5f, at.y * yd);
            }
            gone.Clear();
            foreach (var kv in bodies) if (w.Find(kv.Key) == null) gone.Add(kv.Key);
            foreach (int slot in gone) { Object.Destroy(bodies[slot].gameObject); bodies.Remove(slot); teamOf.Remove(slot); }
        }

        public Vector3 HeadPosition(int slot) =>
            bodies.TryGetValue(slot, out var t) ? t.position + Vector3.up * cfg.view.playerHeightM * 0.5f : Vector3.zero;

        Transform Body(Player p)
        {
            if (!bodies.TryGetValue(p.Slot, out var t))
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Player " + p.Slot;
                Object.Destroy(go.GetComponent<Collider>());
                t = go.transform;
                t.SetParent(root, false);
                float r = cfg.view.playerRadiusM * 2f;
                t.localScale = new Vector3(r, cfg.view.playerHeightM * 0.5f, r);
                bodies[p.Slot] = t;
            }
            if (!teamOf.TryGetValue(p.Slot, out int team) || team != p.Team)
            {
                teamOf[p.Slot] = p.Team;
                if (p.Team >= 0 && p.Team < teamMats.Count) t.GetComponent<MeshRenderer>().sharedMaterial = teamMats[p.Team];
            }
            return t;
        }
    }
}
