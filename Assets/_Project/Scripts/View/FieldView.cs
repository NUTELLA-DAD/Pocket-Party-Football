using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // Builds the flat field from game.json: surround, end zones, grass stripes, yard lines, sideline border, hash marks.
    // World units are metres; the config is in yards.
    public static class FieldView
    {
        const float LineLift = 0.01f, MarkLift = 0.015f, SurroundDrop = -0.01f;

        public static GameObject Build(GameConfig cfg, Material baseMat, Transform parent = null)
        {
            var f = cfg.field;
            var root = new GameObject("Field");
            if (parent != null) root.transform.SetParent(parent, false);
            var mats = new Dictionary<string, Material>();
            Material Mat(string hex)
            {
                if (!mats.TryGetValue(hex, out var m))
                {
                    m = new Material(baseMat) { name = "Field " + hex };
                    m.SetColor("_BaseColor", Colors.Hex(hex));
                    m.SetFloat("_Smoothness", 0f);
                    mats[hex] = m;
                }
                return m;
            }

            float y = f.yardM;
            float border = f.sidelineBorderYd;
            // the ground round the field
            Quad(root, "Surround", 0, 0, f.lengthYd + 2 * f.surroundYd, f.widthYd + 2 * f.surroundYd, SurroundDrop, y, Mat(f.surroundColor));
            // the white border just outside the sidelines and end lines
            Quad(root, "Border", 0, 0, f.lengthYd + 2 * border, f.widthYd + 2 * border, SurroundDrop * 0.5f, y, Mat(f.lineColor));
            // end zones
            Quad(root, "EndZoneWest", -f.GoalLineX - f.endZoneYd * 0.5f, 0, f.endZoneYd, f.widthYd, 0, y, Mat(f.endZoneColor));
            Quad(root, "EndZoneEast", f.GoalLineX + f.endZoneYd * 0.5f, 0, f.endZoneYd, f.widthYd, 0, y, Mat(f.endZoneColor));
            // the grass between the goal lines
            var stripes = FieldLayout.Stripes(f);
            bool striped = cfg.Feature("fieldStripes");
            for (int i = 0; i < stripes.Count; i++)
            {
                var s = stripes[i];
                string col = striped && i % 2 == 1 ? f.grassAltColor : f.grassColor;
                Quad(root, "Grass" + i, (s[0] + s[1]) * 0.5f, 0, s[1] - s[0], f.widthYd, 0, y, Mat(col));
            }
            // yard lines and end lines
            foreach (float x in FieldLayout.YardLineXs(f))
                Quad(root, "YardLine", x, 0, f.lineWidthYd, f.widthYd, LineLift, y, Mat(f.lineColor));
            // sidelines
            Quad(root, "SidelineNear", 0, -f.HalfWidth, f.lengthYd, f.lineWidthYd, LineLift, y, Mat(f.lineColor));
            Quad(root, "SidelineFar", 0, f.HalfWidth, f.lengthYd, f.lineWidthYd, LineLift, y, Mat(f.lineColor));
            // hash marks
            foreach (var h in FieldLayout.HashMarks(f))
                Quad(root, "Hash", h[0], h[1], f.lineWidthYd, f.hashLengthYd, MarkLift, y, Mat(f.lineColor));
            return root;
        }

        // A flat quad centred at (xYd, zYd), sized in yards, facing up.
        static void Quad(GameObject root, string name, float xYd, float zYd, float lenXYd, float lenZYd, float lift, float yardM, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(xYd * yardM, lift, zYd * yardM);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(lenXYd * yardM, lenZYd * yardM, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    public static class Colors
    {
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
