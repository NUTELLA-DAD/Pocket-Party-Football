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
    //
    // FEATURES.kickArc (a person's punt or field goal): the arc itself is on the grass (Boot/KickArcView.cs); here,
    // at the side of the picture, Kenny's POWER meter and PITCH ANGLE dial (Resources/KickHud, the same art as the
    // phone): the colour bar fills the meter with the pointer at its top, the needle swings on the dial. A field goal
    // marks the power that clears the bar at its usual climb (the brackets) and, once the power is set, shades the
    // climbs that clear it; a punt shades a good high hang. They stay up, stopped, for meterShowS after the kick.
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
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
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
            bool person = Kicking.PersonKicks(world, kicker);
            bool arc = Kicking.ArcOn(world);
            bool sweep = before && person && stage != Kicking.SweepStage.None && !arc;
            if (sweep) Arrows(cam, stage, s);
            if (arc && (before && person && stage != Kicking.SweepStage.None || after && k.PitchSet)) ArcHud(stage, after, s);

            text ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            text.fontSize = Mathf.RoundToInt(26 * s);
            text.normal.textColor = Color.white;

            string ask = !before || !person ? "" : arc && stage == Kicking.SweepStage.Aim ? "STEER IT, THEN HIKE"
                : stage == Kicking.SweepStage.Pitch ? "TAP FOR PITCH" : stage == Kicking.SweepStage.Aim ? "TAP YOUR PHONE TO AIM"
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
            if (!after || arc && k.PitchSet) return;

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

        // ---- FEATURES.kickArc: the HUD art ----

        // Where things are in each piece of art (shares of its width and height): the meter's channel and its 0% and
        // 100% marks, the dial's pivot and the needle's reach (of the dial's width), the needle's hub (of its width).
        sealed class Art
        {
            public Texture2D power, dial, fill, pointer, needle, mark;
            public float chanL, chanR, zeroY, fullY, pivotX, pivotY, reach, hub;
        }
        static Art punt, goal;

        static Art Load(string set, float chanL, float chanR, float zeroY, float fullY, float pivotX, float pivotY, float reach, float hub)
        {
            Texture2D T(string n) => Resources.Load<Texture2D>("KickHud/" + set + "-" + n);
            var a = new Art { power = T("power"), dial = T("dial"), fill = T("fill"), pointer = T("pointer"), needle = T("needle"), mark = T("mark"),
                chanL = chanL, chanR = chanR, zeroY = zeroY, fullY = fullY, pivotX = pivotX, pivotY = pivotY, reach = reach, hub = hub };
            return a.power != null && a.dial != null && a.fill != null && a.needle != null ? a : null;
        }

        void ArcHud(Kicking.SweepStage stage, bool after, float s)
        {
            var w = world;
            var c = w.Cfg.kicking;
            var k = w.Kick;
            bool fg = Kicking.Mode(w) == KickKind.FieldGoal;
            var art = fg ? (goal ??= Load("fg", 0.345f, 0.663f, 0.736f, 0.145f, 0.120f, 0.774f, 0.48f, 0.1751f))
                         : (punt ??= Load("punt", 0.34f, 0.66f, 0.761f, 0.124f, 0.0775f, 0.801f, 0.52f, 0.1926f));
            if (art == null) return;
            float power = after ? k.Power : stage == Kicking.SweepStage.Power ? Kicking.SweepNow(w) : stage == Kicking.SweepStage.Pitch ? k.ChosenPower : 0f;
            float pitch = after ? k.Pitch : stage == Kicking.SweepStage.Pitch ? Kicking.PitchDeg(c, Kicking.SweepNow(w)) : c.arcMinPitchDeg;

            // the POWER meter, then the PITCH ANGLE dial to its right, both at the right of the picture
            float ph = 360 * s, pw = ph * art.power.width / art.power.height;
            float dh = 300 * s, dw = dh * art.dial.width / art.dial.height;
            float x = Screen.width - 40 * s - dw - 16 * s - pw, y = (Screen.height - ph) * 0.5f;
            var pr = new Rect(x, y, pw, ph);
            GUI.DrawTexture(pr, art.power);
            float cl = pr.x + pw * art.chanL, cw = pw * (art.chanR - art.chanL), y0 = pr.y + ph * art.zeroY, full = ph * (art.zeroY - art.fullY);
            float fill = Mathf.Clamp01(power);
            if (fill > 0f) GUI.DrawTextureWithTexCoords(new Rect(cl, y0 - full * fill, cw, full * fill), art.fill, new Rect(0f, 0f, 1f, fill));
            if (fg && art.mark != null)
            {
                // the brackets round the power that clears the bar
                float ny = y0 - full * Mathf.Clamp01(Kicking.NeedPower(w)), mw = cw * 1.7f, mh = mw * art.mark.height / art.mark.width;
                GUI.DrawTexture(new Rect(cl + cw * 0.5f - mw * 0.5f, ny - mh * 0.5f, mw, mh), art.mark);
            }
            if (art.pointer != null)
            {
                float tw = cw * 0.9f, th = tw * art.pointer.height / art.pointer.width;
                GUI.DrawTexture(new Rect(cl + cw + 2 * s, y0 - full * fill - th * 0.5f, tw, th), art.pointer);
            }

            var dr = new Rect(pr.xMax + 16 * s, pr.yMax - dh, dw, dh);
            GUI.DrawTexture(dr, art.dial);
            var pivot = new Vector2(dr.x + dw * art.pivotX, dr.y + dh * art.pivotY);
            float reach = dw * art.reach;
            // the climbs worth stopping on: a field goal's that clear the bar (once the power is set), a punt's good hang
            float lo = 0f, hi = -1f;
            if (!fg) { lo = c.puntGoodMinDeg; hi = c.puntGoodMaxDeg; }
            else if (stage == Kicking.SweepStage.Pitch && Kicking.ClearPitch(w, k.ChosenPower, out float a, out float b)) { lo = a; hi = b; }
            for (float deg = lo; deg <= hi; deg += 2f)
            {
                var at = pivot + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), -Mathf.Sin(deg * Mathf.Deg2Rad)) * dw * (fg ? 0.63f : 0.67f);   // (out on the dial's ticks)
                Box(new Rect(at.x - 5 * s, at.y - 5 * s, 10 * s, 10 * s), new Color(0.37f, 0.95f, 0.55f, 0.9f));
            }
            float nw = reach / (1f - art.hub), nh = nw * art.needle.height / art.needle.width;
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(-Mathf.Clamp(pitch, 0f, 90f), pivot);
            GUI.DrawTexture(new Rect(pivot.x - nw * art.hub, pivot.y - nh * 0.5f, nw, nh), art.needle);
            GUI.matrix = keep;
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
