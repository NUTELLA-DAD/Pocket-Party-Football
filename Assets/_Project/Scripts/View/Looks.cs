using UnityEngine;

namespace PocketFootball.View
{
    // The shared look: the toon material (Resources/Toon.mat, made by ProjectSetup) and small mesh helpers.
    // Every code-made mesh carries vertex colours; the colour's alpha is the toon shader's flag
    // (1 = matte with an ink outline, 0.75 = shiny, 0.5 = shiny without outline, 0.25 = paint: no outline, no shadow).
    public static class Looks
    {
        public const float Matte = 1f, Shiny = 0.75f, Gloss = 0.5f, Paint = 0.25f;
        static Material toon;

        public static Material Toon(Material fallback)
        {
            if (toon == null) toon = Resources.Load<Material>("Toon");
            return toon != null ? toon : fallback;
        }

        static Color Lin(Color c, float flag)
        {
            var l = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            l.a = flag;
            return l;
        }

        // A flat rectangle lying on the ground (facing up), centred at `at` (metres), size in metres; uv 0..1.
        public static GameObject Flat(Transform parent, string name, Vector3 at, Vector2 size, Color color, Material mat, float flag = Paint)
        {
            var m = new Mesh { name = name };
            float x = size.x * 0.5f, z = size.y * 0.5f;
            m.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            var c = Lin(color, flag);
            m.colors = new[] { c, c, c, c };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // A mesh from a FigureKit, as a plain (unskinned) object.
        public static GameObject Static(Transform parent, string name, FigureKit k, Material mat, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.BuildStatic(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static Color VertexColor(Color c, float flag) => Lin(c, flag);
    }

    // Hard-edged pieces (boxes, slabs, wedges) with vertex colours, gathered into one mesh. Splits into several
    // meshes when it grows past 60k vertices.
    public sealed class Blocks
    {
        readonly System.Collections.Generic.List<Vector3> pos = new System.Collections.Generic.List<Vector3>(), nrm = new System.Collections.Generic.List<Vector3>();
        readonly System.Collections.Generic.List<Color> col = new System.Collections.Generic.List<Color>();
        readonly System.Collections.Generic.List<int> tris = new System.Collections.Generic.List<int>();
        public float Flag = Looks.Matte;

        public int Count => pos.Count;

        // A quad; facing: the way it should face (its winding is fixed to match), or none to keep the given order.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3? facing = null)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (facing.HasValue && Vector3.Dot(n, facing.Value) < 0f) { var t = b; b = d; d = t; n = -n; }
            int s = pos.Count;
            var vc = Looks.VertexColor(color, Flag);
            foreach (var p in new[] { a, b, c, d }) { pos.Add(p); nrm.Add(n); col.Add(vc); }
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
        }

        // An axis-aligned box (rotated by q about its centre).
        public void Box(Vector3 c, Vector3 half, Color color, Quaternion? q = null, bool bottom = false)
        {
            var r = q ?? Quaternion.identity;
            Vector3 P(float x, float y, float z) => c + r * Vector3.Scale(half, new Vector3(x, y, z));
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), color, r * Vector3.up);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), color, r * Vector3.back);
            Quad(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), color, r * Vector3.forward);
            Quad(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), color, r * Vector3.left);
            Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), color, r * Vector3.right);
            if (bottom) Quad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), color, r * Vector3.down);
        }

        public GameObject Build(Transform parent, string name, Material mat, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            const int Max = 60000;
            for (int start = 0, part = 0; start < tris.Count; part++)
            {
                // whole quads (6 indices, 4 vertices each) per chunk
                int end = Mathf.Min(tris.Count, start + Max / 4 * 6);
                var m = new Mesh { name = name + part };
                int v0 = tris[start], v1 = tris[end - 1] + 1;
                for (int i = start; i < end; i++) { v0 = Mathf.Min(v0, tris[i]); v1 = Mathf.Max(v1, tris[i] + 1); }
                m.SetVertices(pos.GetRange(v0, v1 - v0));
                m.SetNormals(nrm.GetRange(v0, v1 - v0));
                m.SetColors(col.GetRange(v0, v1 - v0));
                var t = new int[end - start];
                for (int i = 0; i < t.Length; i++) t[i] = tris[start + i] - v0;
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                var c = new GameObject(name + part);
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = m;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                start = end;
            }
            return go;
        }
    }
}
