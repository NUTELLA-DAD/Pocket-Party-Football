using UnityEditor;

namespace PocketFootball.Editor
{
    // Rigged character models (Art/Models) import as Humanoid with their own avatar, so they need no Inspector setup.
    // The rigs use Unity's own bone names (Hips, Spine, Chest, LeftUpperArm ...), so the avatar maps itself.
    public sealed class ModelImport : AssetPostprocessor
    {
        const string ModelsDir = "Assets/_Project/Art/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsDir)) return;
            var m = (ModelImporter)assetImporter;
            m.animationType = ModelImporterAnimationType.Human;
            m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
