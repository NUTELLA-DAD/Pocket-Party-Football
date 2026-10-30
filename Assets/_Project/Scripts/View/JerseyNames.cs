using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.jerseyNames (NO_JERSEY_NAMES=1: plain backs): a season recruit's surname across his shoulders, over the
    // number on his back. The letters are the game's own glyphs (Glyphs, the stroke font that paints the numbers), each
    // stroke laid as a thick ribbon in a flat mesh with the team's number colour as its vertex colour and the Paint
    // flag (a matte decal: no outline, casts no shadow), hung on the figure's Torso bone so it leans and twists with
    // him. Sizes and the place on the back come from game.json season.jerseyName (rig cm, like FootballFigure).
    // The figure faces +x, so the back is toward -x and someone behind him reads left to right along -z.
    public static class JerseyNames
    {
        public static bool On(GameConfig cfg) => cfg.Feature("jerseyNames");

        public static GameObject Wear(FootballFigure fig, GameConfig cfg, TeamConfig team, string surname, Material mat)
        {
            if (fig == null || fig.Torso == null || !On(cfg) || string.IsNullOrEmpty(surname) || mat == null) return null;
            var jc = cfg.season.jerseyName;
            var fill = Colors.Hex(string.IsNullOrEmpty(team?.numberFill) ? "#ffffff" : team.numberFill);
            var mesh = Build(surname, jc, Looks.VertexColor(fill, Looks.Paint));
            if (mesh == null) return null;
            var go = new GameObject("Name " + surname);
            go.transform.SetParent(fig.Torso, false);
            go.transform.localPosition = new Vector3(-jc.backCm, jc.aboveTorsoCm, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        // The name as one flat mesh in the plane x = 0, centred on (0, 0, 0): text runs along -z (the reader's right),
        // letters stand up +y, the face looks toward -x. Letters are letterCm tall and shrink to fit maxWidthCm.
        // Null for text with no glyphs in it.
        public static Mesh Build(string text, JerseyNameConfig jc, Color vertexColor)
        {
            if (string.IsNullOrEmpty(text) || jc == null || jc.letterCm <= 0f) return null;
            var strokes = Glyphs.Text(text, jc.letterCm, jc.gapFrac * jc.letterCm, out float width);
            if (strokes.Count == 0 || width <= 0f) return null;
            float k = jc.maxWidthCm > 0f && width > jc.maxWidthCm ? jc.maxWidthCm / width : 1f;
            float pen = Mathf.Max(0.1f, jc.penFrac * jc.letterCm * k), half = pen * 0.5f;
            float x0 = -width * k * 0.5f, y0 = -jc.letterCm * k * 0.5f;   // (Glyphs.Text makes the letters letterCm tall)
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var tris = new List<int>();
            Vector3 facing = Vector3.left;
            Vector3 At(Vector2 p) => new Vector3(0f, y0 + p.y * k, -(x0 + p.x * k));   // text x -> -z, text y -> +y
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, facing) < 0f) { var t = b; b = d; d = t; n = -n; }
                int s = pos.Count;
                foreach (var v in new[] { a, b, c, d }) { pos.Add(v); nrm.Add(facing); col.Add(vertexColor); }
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }
            foreach (var stroke in strokes)
            {
                if (stroke.Length == 1)
                {
                    var c = At(stroke[0]);
                    Quad(c + new Vector3(0, -half, half), c + new Vector3(0, half, half), c + new Vector3(0, half, -half), c + new Vector3(0, -half, -half));
                    continue;
                }
                for (int i = 0; i + 1 < stroke.Length; i++)
                {
                    Vector3 a = At(stroke[i]), b = At(stroke[i + 1]);
                    var dir = b - a;
                    if (dir.sqrMagnitude < 1e-8f) continue;
                    dir.Normalize();
                    var side = Vector3.Cross(dir, facing).normalized * half;   // across the ribbon, in the plane
                    var cap = dir * half;   // square caps, so joints and ends close up
                    Quad(a - cap - side, a - cap + side, b + cap + side, b + cap - side);
                }
            }
            if (pos.Count == 0) return null;
            var m = new Mesh { name = "Name " + text };
            m.SetVertices(pos);
            m.SetNormals(nrm);
            m.SetColors(col);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
