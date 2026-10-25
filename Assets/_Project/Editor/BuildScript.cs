using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PocketFootball.Editor
{
    // Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod PocketFootball.Editor.BuildScript.BuildWindows
    public static class BuildScript
    {
        const string ConfigDir = "Assets/_Project/Config";
        const string StreamingConfigDir = "Assets/StreamingAssets/config";
        public const string ExePath = "Builds/Windows/PocketFootball.exe";

        [MenuItem("Pocket Football/Build Windows")]
        public static void BuildWindows()
        {
            CopyConfigToStreamingAssets();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.BootScenePath },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[build] result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize} time={s.totalTime}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        // The build reads its config from StreamingAssets so tuning can be edited without rebuilding.
        static void CopyConfigToStreamingAssets()
        {
            foreach (var src in Directory.GetFiles(ConfigDir, "*.json", SearchOption.AllDirectories))
            {
                string rel = src.Substring(ConfigDir.Length + 1);
                string dst = Path.Combine(StreamingConfigDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
            }
            AssetDatabase.Refresh();
        }
    }
}
