using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The TV side of a kick (WS-01, FEATURES.kicking). Over the kicker while he lines it up: what kind of kick it is
    // (KICKOFF, PUNT, FIELD GOAL 42 YDS, EXTRA POINT) and, for a person, SWIPE YOUR PHONE. For kicking.meterShowS after
    // the kick: his power as a bar filled in tenths (stepped, so it never wobbles) and his aim as a tick left or right
    // of the middle. Reads the sim, never writes it. Draws nothing with kicking off.
    //
    // A field goal with FEATURES.kickSweep (a person kicking): the same arrows his phone shows, drawn on the field so
    // the room sees them. While he lines up, a line of dots runs from the holder's spot the way he is aiming, sweeping
    // left and right; after the first tap, the dots rise into the arc the ball would fly at the power sweeping now
    // (with no wind: the flags on the posts are for reading that), and a power bar at the side shows where the sweep
    // is, the mark that just clears the bar and the red top that hooks. The panel names the wind too (FEATURES.wind).
    // With FEATURES.kickCam the panel sits at the top of the picture, clear of the kicker, who is close to the camera.
    public sealed class KickMeter : MonoBehaviour
    {
        World world;
        PlayersView players;
        GUIStyle text;
        string title = "";

        public static KickMeter Create(GameObject on, World w, PlayersView players)
        {
            var m = on.AddComponent<KickMeter>();
            m.world = w;
            m.players = players;
            return m;
        }

        void OnGUI()
        {
            if (world == null || players == null || !Kicking.On(world) || Replay.OnScreen) return;
            var cam = Camera.main;
            if (cam == null) return;
            var k = world.Kick;
            var c = world.Cfg.kicking;
            var kicker = Kicking.Kicker(world);
            bool before = kicker != null && Kicking.WaitingToKick(world, kicker);
            bool after = k.Kicked && world.Tick - k.KickTick < c.meterShowS * world.Cfg.sim.tickHz;
            bool goal = Kicking.Mode(world) == KickKind.FieldGoal;
            if (before)
            {
                // (remembered: once the whistle blows the play is no longer a kick)
                title = Kicking.Name(world);
                int yds = Kicking.KickYards(world);
                if (yds > 0 && !k.Try) title += " " + yds + " YDS";
                if (goal && Kicking.WindOn(world)) title += "   " + WindWords(world);
            }
            if ((!before && !after) || kicker == null || title.Length == 0) return;

            float s = Screen.height / 1080f;
            var stage = Kicking.Stage(world);
            bool person = kicker.Human && !kicker.BotHelp;
            bool sweep = before && person && stage != Kicking.SweepStage.None;
            if (sweep) Arrows(cam, stage, s);

            text ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            text.fontSize = Mathf.RoundToInt(26 * s);
            text.normal.textColor = Color.white;

            string ask = !before || !person ? "" : stage == Kicking.SweepStage.Aim ? "TAP YOUR PHONE TO AIM"
                : stage == Kicking.SweepStage.Power ? "TAP FOR POWER" : k.Approach ? "" : goal && Kicking.SweepOn(world) ? "" : "SWIPE YOUR PHONE";
            string line = ask.Length > 0 ? title + "   " + ask : title;
            float w = Mathf.Max(text.CalcSize(new GUIContent(line)).x + 32 * s, 260 * s);
            float h = 44 * s, barH = after ? 34 * s : 0f;
            float x, y;
            bool fromBehind = goal && world.Cfg.Feature("kickCam");
            if (fromBehind) { x = (Screen.width - w) * 0.5f; y = 24 * s; }
            else
            {
                Vector3 sp = cam.WorldToScreenPoint(players.HeadPosition(kicker.Slot) + Vector3.up * 1.6f);
                if (sp.z <= 0f) return;
                x = sp.x - w * 0.5f; y = Screen.height - sp.y - h - barH;
            }
            x = Hud.Clear(new Rect(x, y, w, h + barH), 8f * s).x;   // on screen, clear of the join panel
            Box(new Rect(x, y, w, h + barH), new Color(0.05f, 0.13f, 0.1f, 0.88f));
            GUI.Label(new Rect(x, y, w, h), line, text);
            if (!after) return;

            // power: ten steps from green to red; aim: a white tick, the middle is straight
            float pad = 12 * s, bw = w - pad * 2f, by = y + h, bh = barH - pad * 0.75f;
            Box(new Rect(x + pad, by, bw, bh), new Color(1f, 1f, 1f, 0.15f));
            float filled = Mathf.Floor(k.Power * 10f) / 10f;
            Box(new Rect(x + pad, by, bw * filled, bh), Color.Lerp(new Color(0.36f, 0.85f, 0.5f), new Color(0.95f, 0.3f, 0.25f), filled));
            float tick = x + pad + bw * (0.5f + k.Aim * 0.5f);
            Box(new Rect(tick - 3 * s, by - 4 * s, 6 * s, bh + 8 * s), Color.white);
            Box(new Rect(x + pad + bw * 0.5f - 1 * s, by, 2 * s, bh), new Color(0f, 0f, 0f, 0.5f));
        }

        // "WIND 9 MPH LEFT TO RIGHT", from the kicker's side: across his kick, or in his face, or behind him.
        static string WindWords(World w)
        {
            Kicking.Wind(w, out float x, out float z);
            float mph = Mathf.Sqrt(x * x + z * z) * w.Cfg.kicking.windMphPerYdS;
            if (mph < 1f) return "NO WIND";
            float along = x * w.Dir, right = -z * w.Dir;   // his right is -z going +x
            string way = Mathf.Abs(right) >= Mathf.Abs(along) ? (right > 0f ? "LEFT TO RIGHT" : "RIGHT TO LEFT") : along > 0f ? "AT HIS BACK" : "IN HIS FACE";
            return "WIND " + Mathf.RoundToInt(mph) + " MPH " + way;
        }

        // The phone's arrows on the field: dots from the hold out the way he aims, then up into the arc at this power.
        void Arrows(Camera cam, Kicking.SweepStage stage, float s)
        {
            var w = world;
            var c = w.Cfg.kicking;
            var k = w.Kick;
            float m = w.Cfg.field.yardM, dir = w.Dir;
            float aim = stage == Kicking.SweepStage.Aim ? Kicking.SweepNow(w) : k.Aim;
            float yaw = aim * c.maxAimDeg * Mathf.Deg2Rad;
            float dist = Mathf.Abs(dir * w.Cfg.field.HalfLength - k.HoldX);
            // along the field and across it, the kicker's right = -z going +x (as Kicking.Launch kicks it)
            float ux = dir * Mathf.Cos(yaw), uz = -dir * Mathf.Sin(yaw);
            var dot = new Color(1f, 0.93f, 0.35f, 0.95f);
            if (stage == Kicking.SweepStage.Aim)
            {
                for (float d = 1.5f; d <= dist; d += 1.5f) Dot(cam, new Vector3((k.HoldX + ux * d) * m, 0.05f, (k.HoldZ + uz * d) * m), 0.35f * m, dot);
                Dot(cam, new Vector3((k.HoldX + ux * dist) * m, 0.05f, (k.HoldZ + uz * dist) * m), 0.9f * m, Color.white);
                return;
            }
            // the arc at the power sweeping now (no wind): one dot every tenth of a second of its flight
            float power = Kicking.SweepNow(w);
            float speed = c.minSpeedYdS + (c.maxSpeedYdS - c.minSpeedYdS) * power;
            float climb = Kicking.Climb(w, KickKind.FieldGoal, power) * Mathf.Deg2Rad;
            float flat = speed * Mathf.Cos(climb), vy = speed * Mathf.Sin(climb), g = w.Cfg.ball.gravityYdS2;
            for (float t = 0.1f; t < 6f; t += 0.1f)
            {
                float y = c.kickHeightYd + vy * t - 0.5f * g * t * t, d = flat * t;
                if (y < 0f || d > dist + 5f) break;
                Dot(cam, new Vector3((k.HoldX + ux * d) * m, y * m, (k.HoldZ + uz * d) * m), 0.4f * m, dot);
            }
            PowerBar(power, Kicking.NeedPower(w), c.shankFromPower, s);
        }

        // A dot on the field (metres), as wide on screen as `size` metres would be there.
        static void Dot(Camera cam, Vector3 at, float size, Color color)
        {
            var sp = cam.WorldToScreenPoint(at);
            if (sp.z <= 0.5f) return;
            var edge = cam.WorldToScreenPoint(at + cam.transform.right * size);
            float r = Mathf.Clamp(Mathf.Abs(edge.x - sp.x), 3f, 40f);
            Box(new Rect(sp.x - r * 0.5f, Screen.height - sp.y - r * 0.5f, r, r), color);
        }

        // The power sweep at the side of the picture: the fill, the mark that just clears the bar, the red top.
        static void PowerBar(float power, float need, float shank, float s)
        {
            float bw = 34 * s, bh = 360 * s, x = Screen.width - bw - 60 * s, y = (Screen.height - bh) * 0.5f;
            Box(new Rect(x - 6 * s, y - 6 * s, bw + 12 * s, bh + 12 * s), new Color(0.05f, 0.13f, 0.1f, 0.88f));
            Box(new Rect(x, y, bw, bh), new Color(1f, 1f, 1f, 0.15f));
            Box(new Rect(x, y, bw, bh * (1f - shank)), new Color(0.95f, 0.3f, 0.25f, 0.6f));
            float fill = Mathf.Clamp01(power);
            Box(new Rect(x, y + bh * (1f - fill), bw, bh * fill), Color.Lerp(new Color(0.36f, 0.85f, 0.5f), new Color(1f, 0.8f, 0.25f), fill));
            float ny = y + bh * (1f - Mathf.Clamp01(need));
            Box(new Rect(x - 10 * s, ny - 2 * s, bw + 20 * s, 4 * s), Color.white);
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
