using UnityEditor;

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

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsDir)) return;
            var m = (ModelImporter)assetImporter;
            m.animationType = ModelImporterAnimationType.Generic;
            m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
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
