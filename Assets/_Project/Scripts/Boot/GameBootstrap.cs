using System;
using System.IO;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PocketFootball.Boot
{
    // The only MonoBehaviour in the Boot scene. Builds everything else from code and game.json.
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] TextAsset embeddedConfig;
        [SerializeField] Material baseMaterial;

        public static GameBootstrap Instance { get; private set; }
        public GameConfig Config { get; private set; }
        public CameraRig Rig { get; private set; }
        public GameObject Field { get; private set; }
        public CliArgs Args { get; private set; }
        public PartyHost Host { get; private set; }
        public LabMode Lab { get; private set; }     // the practice lab (-lab, Lab.bat), else null

        void Awake()
        {
            Instance = this;
            Args = CliArgs.FromEnvironment();
            Config = ConfigLoader.Parse(LoadConfigText(), Environment.GetEnvironmentVariable);
            ConfigLoader.AddPlays(Config, LoadPlays());
            Application.targetFrameRate = 60;

            BuildLight();
            Field = FieldView.Build(Config, baseMaterial);
            Rig = CameraRig.Create(Config);
            if (Args.LookX.HasValue) Rig.SetLookAtX(Args.LookX.Value);
            Host = PartyHost.Create(gameObject, Config, baseMaterial, Rig, Args);
            if (LabMode.Requested()) Lab = LabMode.Create(gameObject, Config, baseMaterial, Host, Rig, ConfigDir);

            if (!string.IsNullOrEmpty(Args.ShotsDir))
            {
                var h = gameObject.AddComponent<ScreenshotHarness>();
                h.Dir = Args.ShotsDir;
                h.Fixture = Args.Fixture;
                h.QuitAfter = Args.QuitAfterShots;
            }
            Debug.Log($"[boot] Pocket Party Football up: field {Config.field.lengthYd} x {Config.field.widthYd} yd, camera {Config.camera.visibleYards} yd wide");
        }

        // The editor reads Assets/_Project/Config itself (never a stale copy). A build reads StreamingAssets/config
        // (copied there by BuildScript, editable without rebuilding), else the embedded game.json.
        static string ConfigDir =>
#if UNITY_EDITOR
            "Assets/_Project/Config";
#else
            Path.Combine(Application.streamingAssetsPath, "config");
#endif

        string LoadConfigText()
        {
            string path = Path.Combine(ConfigDir, "game.json");
            if (File.Exists(path)) return File.ReadAllText(path);
            return embeddedConfig != null ? embeddedConfig.text : "{}";
        }

        static System.Collections.Generic.List<string> LoadPlays()
        {
            var list = new System.Collections.Generic.List<string>();
            string dir = Path.Combine(ConfigDir, "plays");
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.json")) list.Add(File.ReadAllText(f));
            if (list.Count == 0) Debug.LogWarning("[boot] no plays found in " + dir);
            return list;
        }

        void BuildLight()
        {
            var go = new GameObject("Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.93f, 0.8f);   // late-afternoon warmth (concept 04)
            sun.shadows = LightShadows.Hard;
            go.transform.rotation = Quaternion.Euler(42f, 215f, 0f);   // from behind the far right: shadows fall toward the camera, to the left
            if (SkyView.Apply(Config, sun)) return;   // the sunset: a low warm sun, the sky box, haze (FEATURES.sunsetSky)
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Application.Quit(0);
        }

        // Shows the version in a corner until the real HUD exists (M1-e).
        void OnGUI()
        {
            if (Host != null && Host.Photo != null) return;
            if (Lab != null) return;     // the lab draws its own panel
            GUI.Label(new Rect(12, 8, 600, 24), "Pocket Party Football - M0   (B adds a bot, Esc quits)");
        }
    }
}
