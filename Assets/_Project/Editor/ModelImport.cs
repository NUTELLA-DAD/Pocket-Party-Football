using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PocketFootball.Editor
{
    // Rigged character models (Resources/Models) and their colour maps import with fixed settings, so they need no
    // Inspector setup. The rigs use Unity's Humanoid bone names (Hips, Spine, LeftUpperArm ...) but import as Generic
    // with no avatar: ModelSkin poses the bones directly, and this project has no Animation module (an avatar would
    // need it; animation type None would drop the skinning). No materials are imported either (ModelSkin gives the
    // model the toon material and its colour map).
    public sealed class ModelImport : AssetPostprocessor
    {
        const string ModelsDir = "Assets/_Project/Resources/Models/";
        const int MaxTextureSize = 2048;

        public override uint GetVersion() => 2;   // (bump to reimport the models after changing these rules)

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsDir)) return;
            var m = (ModelImporter)assetImporter;
            m.animationType = ModelImporterAnimationType.Generic;
            m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importTangents = ModelImporterTangents.None;   // (OnPostprocessModel writes its own)
        }

        // The toon outline pushes each vertex out along its tangent when tangent.w != 0 (FigureKit stores a smooth
        // outline direction there), else along its normal. Give the model the same: tangent = the normal averaged
        // over every vertex at that spot, so the ink shell stays closed across hard edges and UV seams.
        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ModelsDir)) return;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var pos = mesh.vertices;
                var nrm = mesh.normals;
                if (nrm.Length != pos.Length) continue;
                var sum = new Dictionary<Vector3Int, Vector3>();
                Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 1e4f);
                for (int i = 0; i < pos.Length; i++)
                {
                    var k = Key(pos[i]);
                    sum[k] = (sum.TryGetValue(k, out var s) ? s : Vector3.zero) + nrm[i];
                }
                var tan = new Vector4[pos.Length];
                for (int i = 0; i < pos.Length; i++)
                {
                    var n = sum[Key(pos[i])];
                    n = n.sqrMagnitude > 1e-12f ? n.normalized : nrm[i];
                    tan[i] = new Vector4(n.x, n.y, n.z, 1f);
                }
                mesh.tangents = tan;
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ModelsDir)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.textureShape = TextureImporterShape.Texture2D;
            t.sRGBTexture = true;
            t.mipmapEnabled = true;
            t.maxTextureSize = MaxTextureSize;
            t.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
