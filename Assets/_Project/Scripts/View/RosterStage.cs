using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The title's ROSTER page (FEATURES.rosterPage): one player model standing on a gold spot, filmed by a camera of
    // its own into a picture the page draws in its panel. The stage stands far off under the world (the game's camera
    // never reaches it, its own camera sees nothing past a few metres), lit by the same sun as the field: the camera
    // stands on the sun's side, roster.turnDeg round from it, so the light falls on his front. He stands still in his
    // standing pose (baked, like the sideline players), in the team's own colours and a jersey number of his own.
    // The camera backs off until all of him fits, roster.frameMargin times his size (the models differ in height and
    // build, the linebacker's shoulder pads are wide).
    public sealed class RosterStage : MonoBehaviour
    {
        static readonly Vector3 Spot = new Vector3(5000f, -400f, 0f);

        GameConfig cfg;
        Camera cam;
        RenderTexture rt;
        Transform man;
        Vector3 view;     // from the spot toward the camera, flat
        Bounds body;      // all of him (world space)
        bool hasBody;
        string shown;   // team/model on the stage now

        public static RosterStage Build(GameConfig cfg)
        {
            var go = new GameObject("RosterStage");
            go.transform.position = Spot;
            var s = go.AddComponent<RosterStage>();
            s.cfg = cfg;
            var r = cfg.roster;

            var floor = Looks.SoftDisc(go.transform, "Spot", 0.9f, Colors.Hex(r.floor), 0.9f, Looks.Glow(null));
            floor.transform.localPosition = Vector3.up * 0.01f;

            // the camera on the sun's side, so the light is on his front
            Vector3 toSun = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : Vector3.right;
            toSun.y = 0f;
            if (toSun.sqrMagnitude < 1e-4f) toSun = Vector3.right;
            s.view = Quaternion.AngleAxis(r.turnDeg, Vector3.up) * toSun.normalized;
            var camGo = new GameObject("RosterCamera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = s.view * cfg.view.playerHeightM * 2f;
            s.cam = camGo.AddComponent<Camera>();
            s.cam.clearFlags = CameraClearFlags.SolidColor;
            s.cam.backgroundColor = Colors.Hex(r.backdrop);
            s.cam.fieldOfView = r.fovDeg;
            s.cam.nearClipPlane = 0.1f;
            s.cam.farClipPlane = cfg.view.playerHeightM * 10f;
            s.cam.enabled = false;   // (on while the page shows: Picture)
            return s;
        }

        // The picture of this team's card, w x h pixels (made again when the size changes).
        public Texture Picture(TeamConfig team, RosterCard card, int w, int h)
        {
            w = Mathf.Max(16, w); h = Mathf.Max(16, h);
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "RosterPicture", antiAliasing = 4 };
                cam.targetTexture = rt;
            }
            cam.enabled = true;
            Dress(team, card);
            Frame();
            return rt;
        }

        // The page closed: the camera stops (the stage stays, ready for the next visit).
        public void Hide()
        {
            if (cam != null) cam.enabled = false;
        }

        void Dress(TeamConfig team, RosterCard card)
        {
            string key = team.@short + "/" + card.Model;
            if (key == shown) return;
            shown = key;
            if (man != null) Destroy(man.gameObject);
            man = null;
            if (card.Spots.Count == 0) return;
            var spot = card.Spots[0];
            int number = spot.Numbers.Count == 0 || spot.Numbers.Contains(card.Number) ? card.Number : spot.Numbers[0];
            var fig = FootballFigure.Create(transform, "Roster " + card.Name, cfg.view.playerHeightM);
            // facing the camera (the figure faces its +x)
            Vector3 toCam = cam.transform.position - transform.position;
            toCam.y = 0f;
            fig.transform.localRotation = Quaternion.FromToRotation(Vector3.right, toCam.normalized);
            man = fig.transform;
            var kit = FootballFigure.KitMaterial(Looks.Toon(null), cfg.view);
            if (!ModelSkin.Wear(fig, team, spot.Pos, cfg, kit, number)) return;
            ModelSkin.Freeze(fig, FootballFigure.PoseKind.Stand);
            JerseyMarks.SetNumber(man.gameObject, cfg, number);
            JerseyMarks.SetSkin(man.gameObject, cfg, card.Model, JerseyMarks.SkinPick(number));
            hasBody = false;
            foreach (var rend in man.GetComponentsInChildren<Renderer>())
            {
                if (!hasBody) { body = rend.bounds; hasBody = true; }
                else body.Encapsulate(rend.bounds);
            }
        }

        // The camera along `view`, far enough back that his height (and his width, on a narrow picture) fits.
        void Frame()
        {
            float h = hasBody ? body.size.y : cfg.view.playerHeightM;
            float wide = hasBody ? Mathf.Max(body.size.x, body.size.z) : h * 0.5f;
            Vector3 aim = hasBody ? body.center : transform.position + Vector3.up * h * 0.5f;
            float m = Mathf.Max(1f, cfg.roster.frameMargin);
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * Mathf.Max(0.1f, cam.aspect);
            float dist = Mathf.Max(h * m * 0.5f / halfV, wide * m * 0.5f / halfH) + wide * 0.5f;
            cam.transform.position = aim + view * dist;
            cam.transform.LookAt(aim);
            cam.farClipPlane = dist * 3f;
        }

        void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
        }
    }
}
