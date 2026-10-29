using System.Collections.Generic;
using UnityEngine;

namespace PocketFootball.View
{
    // A ball's flight path drawn as one smooth ribbon turned to face the camera: bright in the middle and soft at its
    // edges, fading in from the ball (so it never hides it) and out where it meets the grass. The lab's flight path
    // and the throw stick's aim both draw with it. Fill Path with up to Max points, then Draw(n). A tinted, wider or
    // flat one (lying on the grass instead of facing the camera) draws the kick's arc (FEATURES.kickArc).
    public sealed class ArcRibbon
    {
        public const int Max = 160;
        const float Core = 0.9f, FadeInM = 0.6f;
        // across the ribbon: (share of its half width, alpha share): a bright middle that melts away at both edges
        static readonly float[] Across = { -1f, -0.45f, 0f, 0.45f, 1f }, AcrossA = { 0f, 0.65f, 1f, 0.65f, 0f };
        readonly Color tint = new Color(1f, 1f, 1f);
        readonly float halfM = 0.16f, core = Core;
        readonly bool flat;

        public readonly Vector3[] Path = new Vector3[Max];
        readonly Mesh mesh;
        readonly GameObject go;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<int> t = new List<int>();

        public ArcRibbon(Transform parent, string name, Material baseMat, Color tint, float halfM, float core, bool flat) : this(parent, name, baseMat)
        {
            this.tint = tint;
            this.halfM = halfM;
            this.core = core;
            this.flat = flat;
        }

        public ArcRibbon(Transform parent, string name, Material baseMat)
        {
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Looks.Glow(baseMat);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
        }

        // Draws the first n points of Path (fewer than 2 hides it).
        public void Draw(int n)
        {
            v.Clear(); c.Clear(); t.Clear();
            var cam = Camera.main;
            if (n >= 2 && cam != null)
            {
                int k = Across.Length;
                float run = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) run += (Path[i] - Path[i - 1]).magnitude;
                    Vector3 along = (Path[Mathf.Min(i + 1, n - 1)] - Path[Mathf.Max(i - 1, 0)]).normalized;
                    Vector3 side = Vector3.Cross(along, flat ? Vector3.up : cam.transform.position - Path[i]).normalized;
                    float fade = Mathf.Clamp01(run / FadeInM) * Mathf.Clamp01((n - 1 - i) / 6f);
                    for (int j = 0; j < k; j++)
                    {
                        v.Add(Path[i] + side * (Across[j] * halfM));
                        c.Add(Looks.VertexColor(tint, core * AcrossA[j] * fade));
                    }
                    if (i == 0) continue;
                    int s0 = (i - 1) * k, s1 = i * k;
                    for (int j = 0; j + 1 < k; j++)
                    {
                        t.Add(s0 + j); t.Add(s1 + j); t.Add(s1 + j + 1);
                        t.Add(s0 + j); t.Add(s1 + j + 1); t.Add(s0 + j + 1);
                    }
                }
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            go.SetActive(t.Count > 0);
        }

        public void Hide() => go.SetActive(false);
    }
}
