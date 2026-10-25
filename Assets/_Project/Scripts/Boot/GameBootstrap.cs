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

        void Awake()
        {
            Instance = this;
            Args = CliArgs.FromEnvironment();
            Config = ConfigLoader.Parse(LoadConfigText(), Environment.GetEnvironmentVariable);
            Application.targetFrameRate = 60;

            BuildLight();
            Field = FieldView.Build(Config, baseMaterial);
            Rig = CameraRig.Create(Config);
            if (Args.LookX.HasValue) Rig.SetLookAtX(Args.LookX.Value);

            if (!string.IsNullOrEmpty(Args.ShotsDir))
            {
                var h = gameObject.AddComponent<ScreenshotHarness>();
                h.Dir = Args.ShotsDir;
                h.Fixture = Args.Fixture;
                h.QuitAfter = Args.QuitAfterShots;
            }
            Debug.Log($"[boot] Pocket Party Football up: field {Config.field.lengthYd} x {Config.field.widthYd} yd, camera {Config.camera.visibleYards} yd wide");
        }

        // A build reads StreamingAssets/config/game.json first (editable without rebuilding), else the embedded copy.
        string LoadConfigText()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "config", "game.json");
            if (File.Exists(path)) return File.ReadAllText(path);
            return embeddedConfig != null ? embeddedConfig.text : "{}";
        }

        void BuildLight()
        {
            var go = new GameObject("Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Hard;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
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
            GUI.Label(new Rect(12, 8, 600, 24), "Pocket Party Football - M0 field   (Esc quits)");
        }
    }
}
