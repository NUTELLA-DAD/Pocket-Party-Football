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
        public TitleScreen Title { get; private set; }   // the title screen while it is up, else null

        void Awake()
        {
            Instance = this;
            Args = CliArgs.FromEnvironment();
            Config = ConfigLoader.Parse(LoadConfigText(), Environment.GetEnvironmentVariable);
            ConfigLoader.AddPlays(Config, LoadPlays());
            PaceFrames(Config);
            TitleScreen.ApplySavedSettings(Config);   // the sound level, and the helpers switched off on the SETTINGS page
            bool title = TitleScreen.Requested(Config, Args);
            if (title) TitleScreen.PrepareAttract(Config, Args);   // the bots' show game behind the title
            CupHost.Apply(Config, title);   // FEATURES.cup: the Pocket Cup's next game wears its two teams

            BuildLight();
            Field = FieldView.Build(Config, baseMaterial);
            Rig = CameraRig.Create(Config);
            if (Args.LookX.HasValue) Rig.SetLookAtX(Args.LookX.Value);
            Host = PartyHost.Create(gameObject, Config, baseMaterial, Rig, Args);
            if (LabMode.Requested()) Lab = LabMode.Create(gameObject, Config, baseMaterial, Host, Rig, ConfigDir);
            if (title) Title = TitleScreen.Attach(gameObject, Config, Host, Rig, Args.TitlePage);
            FrameMeter.Attach(gameObject, Config, Args.Fps);   // F3: frames a second on the TV; a line in the log every perfLogEveryS

            if (!string.IsNullOrEmpty(Args.ShotsDir))
            {
                var h = gameObject.AddComponent<ScreenshotHarness>();
                h.Dir = Args.ShotsDir;
                h.Fixture = Args.Fixture;
                h.QuitAfter = Args.QuitAfterShots;
                if (MotionReel.Requested(out string motionCam)) MotionReel.Attach(gameObject, Config, Host, Lab, Rig, Args.ShotsDir, motionCam);
                if (BotReel.Requested(out int reelEvery)) BotReel.Attach(gameObject, Host, Lab, Args.ShotsDir, reelEvery);   // the bots' film (tools/botreel.ps1)
            }
            Debug.Log($"[boot] Pocket Party Football up: field {Config.field.lengthYd} x {Config.field.widthYd} yd, camera {Config.camera.visibleYards} yd wide");
        }

        // A steady view.frameRate. Vsync on alone ran a 240 Hz laptop at an uneven 130-155 fps (Unity ignores
        // targetFrameRate while vsync is on), so a frame stayed up for 1, 2 or 3 refreshes and running judders. When the
        // screen's rate is a whole multiple of it, vsync every N refreshes (240 Hz: every 4th); otherwise vsync off and
        // the frame rate capped. NO_SMOOTH_MOTION=1: vsync as the quality level has it, as before.
        static void PaceFrames(GameConfig cfg)
        {
            int fps = cfg.view.frameRate > 0f ? Mathf.RoundToInt(cfg.view.frameRate) : 60;
            Application.targetFrameRate = fps;
            if (!cfg.Feature("smoothMotion")) return;
            double hz = Screen.currentResolution.refreshRateRatio.value;
            int every = hz > 0 ? (int)Math.Round(hz / fps) : 0;
            bool whole = every >= 1 && every <= 4 && Math.Abs(hz / every - fps) < 1.5;
            QualitySettings.vSyncCount = whole ? every : 0;
            Debug.Log($"[boot] frame pacing: screen {hz:0.##} Hz, {fps} fps, " + (whole ? $"vsync every {every} refreshes" : "vsync off, capped"));
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
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !TitleScreen.HoldsEscape) Application.Quit(0);   // (Esc on a title page goes back)
        }
    }
}
