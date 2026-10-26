using System.IO;
using PocketFootball.Boot;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PocketFootball.Editor
{
    // Writes the URP asset, player settings, the base material and the Boot scene. Run once (and after changing it):
    // Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod PocketFootball.Editor.ProjectSetup.Apply
    public static class ProjectSetup
    {
        const string SettingsDir = "Assets/_Project/Settings";
        const string UrpPath = SettingsDir + "/URP.asset";
        const string RendererPath = SettingsDir + "/URP_Renderer.asset";
        const string MatDir = "Assets/_Project/Art/Materials";
        const string BaseMatPath = MatDir + "/Base.mat";
        const string ConfigPath = "Assets/_Project/Config/game.json";
        public const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";

        [MenuItem("Pocket Football/Apply Project Setup")]
        public static void Apply()
        {
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(MatDir);
            ApplyPlayerSettings();
            ApplyRenderPipeline();
            var mat = EnsureBaseMaterial();
            ResourceMaterial("Toon", "Assets/_Project/Art/Shaders/Toon.shader");
            ResourceMaterial("Sky", "Assets/_Project/Art/Shaders/Sky.shader");
            BuildBootScene(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[setup] ProjectSetup.Apply done");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "PocketParty";
            PlayerSettings.productName = "Pocket Party Football";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            // Input System only (the old Input Manager is off). 0 = old, 1 = new, 2 = both.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null) { prop.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("[setup] activeInputHandler not found");
            }
        }

        static void ApplyRenderPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (urp == null)
            {
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, RendererPath);
                urp = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(urp, UrpPath);
            }
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 160f;
            urp.shadowCascadeCount = 1;
            // the toon look (from Airship Crew): one sharp shadow map, filtered, then thresholded crisp by the shader
            urp.mainLightShadowmapResolution = 4096;
            urp.shadowDepthBias = 0.8f;
            urp.shadowNormalBias = 0.5f;
            var so = new SerializedObject(urp);
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = (int)SoftShadowQuality.High;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        // Plain URP Lit; the view tints copies of it. The hand-written toon shader replaces it in M4.
        static Material EnsureBaseMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogError("[setup] URP Lit shader not found"); return null; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BaseMatPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, BaseMatPath); }
            mat.shader = shader;
            mat.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A material in Resources (loaded at run time by name), on the given shader with its default values.
        static void ResourceMaterial(string name, string shaderPath)
        {
            const string dir = "Assets/_Project/Resources";
            Directory.CreateDirectory(dir);
            string path = dir + "/" + name + ".mat";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null) { Debug.LogError("[setup] shader missing at " + shaderPath); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string n = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: m.SetColor(n, shader.GetPropertyDefaultVectorValue(i)); break;
                    case ShaderPropertyType.Vector: m.SetVector(n, shader.GetPropertyDefaultVectorValue(i)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: m.SetFloat(n, shader.GetPropertyDefaultFloatValue(i)); break;
                }
            }
            EditorUtility.SetDirty(m);
        }

        static void BuildBootScene(Material baseMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameBootstrap");
            var boot = go.AddComponent<GameBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("embeddedConfig").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigPath);
            so.FindProperty("baseMaterial").objectReferenceValue = baseMat;
            so.ApplyModifiedPropertiesWithoutUndo();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }
    }
}
