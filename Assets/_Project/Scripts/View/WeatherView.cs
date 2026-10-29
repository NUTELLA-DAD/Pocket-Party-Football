using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.weather: the rain or snow falling on the TV (game.json "weatherLook"; the sky, haze and grass colours
    // change before the stadium is built, Weather.ApplyLook). One mesh of see-through quads (Resources/Glow.mat) filling
    // a box of air in front of the TV camera: thin streaks for rain, small diamonds for snow, and in the rain splashes
    // on the grass. Each drop starts at a seeded spot in the box and falls at its own speed, leaning with the field
    // goal wind (FEATURES.wind); the box wraps round the camera, so the drops stay put in the world while the camera
    // moves. They move in stepped keys (keyFps) like every other effect, and never touch the sim.
    public sealed class WeatherView : MonoBehaviour
    {
        GameConfig cfg;
        WeatherSkyLook look;
        CameraRig rig;
        System.Func<World> world;
        bool snow;
        int drops, splashes;
        Vector3[] start, speed;   // per drop: its seeded spot in the box (0..1) and its own fall (m/s, before the wind)
        Vector3[] verts;
        Mesh mesh;

        public static WeatherView Attach(GameObject on, GameConfig cfg, CameraRig rig, Material fallback, System.Func<World> world)
        {
            if (Weather.Any(cfg)) Debug.Log("[weather] " + Weather.Name(Weather.Now(cfg)) + (Weather.Sand(cfg) ? " on sand" : ""));
            var look = Weather.Look(cfg);
            var box = cfg.weatherLook.boxM;
            if (look == null || look.count <= 0 || box == null || box.Length < 3) return null;
            var v = on.AddComponent<WeatherView>();
            v.cfg = cfg; v.look = look; v.rig = rig; v.world = world;
            v.snow = Weather.Now(cfg) == Sky.Snow;
            v.Build(Looks.Glow(fallback));
            return v;
        }

        void Build(Material mat)
        {
            drops = look.count;
            splashes = snow ? 0 : Mathf.Max(0, look.splashCount);
            int quads = drops + splashes;
            start = new Vector3[drops];
            speed = new Vector3[drops];
            for (int i = 0; i < drops; i++)
            {
                start[i] = new Vector3(Hash(i, 11), Hash(i, 23), Hash(i, 37));
                // (each falls a little faster or slower; a flake also drifts its own way)
                float fall = look.fallMS * (0.8f + 0.4f * Hash(i, 41));
                float dx = (Hash(i, 53) - 0.5f) * 2f * look.driftMS, dz = (Hash(i, 67) - 0.5f) * 2f * look.driftMS;
                speed[i] = new Vector3(dx, -fall, dz);
            }
            verts = new Vector3[quads * 4];
            var col = new Color[quads * 4];
            var tris = new int[quads * 6];
            var c = Colors.Hex(string.IsNullOrEmpty(look.color) ? "#ffffff" : look.color);
            for (int q = 0; q < quads; q++)
            {
                bool splash = q >= drops;
                float a = splash ? look.splashAlpha : look.alpha;
                // a rain streak fades out toward its top (the tail); flakes and splashes are even
                float tail = !snow && !splash ? 0.15f : 1f;
                col[q * 4 + 0] = Looks.VertexColor(c, a * tail);
                col[q * 4 + 1] = Looks.VertexColor(c, a * tail);
                col[q * 4 + 2] = Looks.VertexColor(c, a);
                col[q * 4 + 3] = Looks.VertexColor(c, a);
                int[] t = { 0, 1, 2, 0, 2, 3 };
                for (int k = 0; k < 6; k++) tris[q * 6 + k] = q * 4 + t[k];
            }
            mesh = new Mesh { name = "Weather", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.colors = col;
            mesh.triangles = tris;
            var go = new GameObject("Weather");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void LateUpdate()
        {
            var cam = rig != null ? rig.Cam : Camera.main;
            if (cam == null || mesh == null) return;
            var lk = cfg.weatherLook;
            // the stepped clock: whole keys only
            double time = Time.unscaledTimeAsDouble;
            long key = lk.keyFps > 0f ? (long)System.Math.Floor(time * lk.keyFps) : 0;
            double t = lk.keyFps > 0f ? key / (double)lk.keyFps : time;

            var ct = cam.transform;
            var box = new Vector3(lk.boxM[0], lk.boxM[1], lk.boxM[2]);
            var mid = ct.position + ct.forward * lk.aheadM;
            Vector3 lo = new Vector3(mid.x - box.x * 0.5f, 0f, mid.z - box.z * 0.5f);   // (the box stands on the grass)

            // the wind (yards a second) as metres a second, leaning the drops
            Vector3 wind = Vector3.zero;
            var w = world != null ? world() : null;
            if (w != null)
            {
                Kicking.Wind(w, out float wx, out float wz);
                wind = new Vector3(wx, 0f, wz) * (cfg.field.yardM * look.windLean);
            }

            Vector3 right = ct.right, up = ct.up;
            for (int i = 0; i < drops; i++)
            {
                var v = speed[i] + wind;
                var s = start[i];
                var p = new Vector3(
                    lo.x + Wrap(s.x * box.x + v.x * t - lo.x, box.x),
                    Wrap(s.y * box.y + v.y * t, box.y),
                    lo.z + Wrap(s.z * box.z + v.z * t - lo.z, box.z));
                int j = i * 4;
                if (snow)
                {
                    // a little diamond facing the camera
                    float h = look.widthM * 0.5f;
                    verts[j] = p + up * h; verts[j + 1] = p + right * h; verts[j + 2] = p - up * h; verts[j + 3] = p - right * h;
                }
                else
                {
                    // a thin streak along the way it falls, its tail (0, 1) up behind it
                    var dir = v.normalized;
                    var side = Vector3.Cross(dir, ct.position - p).normalized * (look.widthM * 0.5f);
                    var tail = p - dir * look.lengthM;
                    verts[j] = tail - side; verts[j + 1] = tail + side; verts[j + 2] = p + side; verts[j + 3] = p - side;
                }
            }
            // rain: splashes on the grass, somewhere new every key
            for (int i = 0; i < splashes; i++)
            {
                int k = (int)(key % 100000);
                var p = new Vector3(lo.x + Hash(i + k * 7919, 71) * box.x, 0.03f, lo.z + Hash(i + k * 7919, 83) * box.z);
                float h = look.splashM;
                int j = (drops + i) * 4;
                verts[j] = p + new Vector3(-h, 0f, 0f); verts[j + 1] = p + new Vector3(0f, 0f, h * 0.5f);
                verts[j + 2] = p + new Vector3(h, 0f, 0f); verts[j + 3] = p + new Vector3(0f, 0f, -h * 0.5f);
            }
            mesh.vertices = verts;
            mesh.bounds = new Bounds(new Vector3(mid.x, box.y * 0.5f, mid.z), box + Vector3.one * (look.lengthM * 2f + 1f));
        }

        // x into [0, size)
        static float Wrap(double x, float size)
        {
            if (size <= 0f) return 0f;
            double m = x % size;
            if (m < 0) m += size;
            return (float)m;
        }

        // seeded 0..1: the same rain every run
        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
