using PocketFootball.View;
using UnityEditor;
using UnityEngine;

namespace PocketFootball.Editor
{
    // The football model (Resources/Ball: an OBJ and its colour map) imports with fixed settings, so it needs no
    // Inspector setup. The toon shader reads its flags from the vertex colours and its outline direction from the
    // tangents (ToonCore.hlsl); an OBJ carries neither, so they are written here: white and shiny, and the smooth normal.
    public sealed class BallImport : AssetPostprocessor
    {
        const string BallDir = "Assets/_Project/Resources/Ball/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(BallDir)) return;
            var m = (ModelImporter)assetImporter;
            m.animationType = ModelImporterAnimationType.None;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.None;
        }

        void OnPostprocessModel(GameObject g)
        {
            if (!assetPath.StartsWith(BallDir)) return;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var n = mesh.normals;
                var t = new Vector4[n.Length];
                var c = new Color[n.Length];
                for (int i = 0; i < n.Length; i++)
                {
                    t[i] = new Vector4(n[i].x, n[i].y, n[i].z, 1f);
                    c[i] = new Color(1f, 1f, 1f, FigureKit.Shiny);
                }
                mesh.tangents = t;
                mesh.colors = c;
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BallDir)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.textureShape = TextureImporterShape.Texture2D;
            t.sRGBTexture = true;
            t.mipmapEnabled = true;
            t.maxTextureSize = 1024;
            t.textureCompression = TextureImporterCompression.Compressed;
            t.wrapModeU = TextureWrapMode.Repeat;   // the map goes once round the ball
            t.wrapModeV = TextureWrapMode.Clamp;
        }
    }
}
