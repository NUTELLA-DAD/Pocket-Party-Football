using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    public enum KickKind { None, Kickoff, Punt, FieldGoal }

    // Kicking (WS-01), behind FEATURES.kicking (NO_KICKING=1 turns it off; it also needs the huddle, live plays and downs).
    // A kickoff opens the game and follows every score: no huddle, both teams line up in the kickoff spots
    // (kicking.kickoffKicking / kickoffReceiving), the kicker swipes his phone (or the bot kicks) and the deepest
    // receiver runs under it. On 4th down the playbook also offers PUNT and FIELD GOAL (Config/plays/punt.json, fg.json):
    // the punter stands puntBackYd deep, the holder's kicker fieldGoalBackYd. A touchdown is followed by a try from the
    // tryFromYd line: kick it (tryKickPoints) or run or pass it in (tryTouchdownPoints). Tackled in your own end zone
    // is a safety (safetyPoints), then a free kick from your own safetyKickFromYd. With FEATURES.clock the second half
    // opens with a kickoff too, by the team that received the first one.
    // A kick is a ball launched with a speed from the power (minSpeedYdS..maxSpeedYdS), a climb by kind and a sideways
    // aim (maxAimDeg). Field goals are good when the ball passes the end line between the posts above the crossbar.
    // Kickoffs and punts: a receiver catches it (catchChance) or picks it up once it has landed; a kicking-team player
    // touching it on the ground downs it there; out the back or caught in the end zone is a touchback. Once a
    // receiver has it, the run back uses the interception-return rules in LivePlay (tackles, out of bounds, a touchdown).
    public static class Kicking
    {
        const float Rad = (float)Math.PI / 180f;   // hygiene:allow (degrees to radians)

        public static bool On(World w) => w.Cfg.Feature("kicking") && Huddle.On(w) && LivePlay.On(w) && Downs.On(w);

        // ---- what is happening ----

        // The kick this play is (None: an ordinary play, or kicking is off).
        public static KickKind Mode(World w)
        {
            if (!On(w)) return KickKind.None;
            if (w.Kick.Kickoff) return KickKind.Kickoff;
            var play = LivePlay.Called(w, w.Offense);
            if (play == null) return KickKind.None;
            if (play.id == w.Cfg.kicking.puntPlay) return KickKind.Punt;
            if (play.id == w.Cfg.kicking.fgPlay) return KickKind.FieldGoal;
            return KickKind.None;
        }

        // KICKOFF, PUNT, FIELD GOAL, EXTRA POINT (for the phone meter and the TV).
        public static string Name(World w)
        {
            switch (Mode(w))
            {
                case KickKind.Kickoff: return "KICKOFF";
                case KickKind.Punt: return "PUNT";
                case KickKind.FieldGoal: return w.Kick.Try ? "EXTRA POINT" : "FIELD GOAL";
            }
            return "";
        }

        // The kicker: the offense's spot 0 (the QB's spot).
        public static Player Kicker(World w) => LivePlay.Qb(w);

        // FEATURES.kickerModel (NO_KICKER_MODEL=1): the kicker wears the kicker's model (posModels "K") from the line-up
        // of a kick to its whistle; in the huddle he is still the QB, so the TV never shows a kick before it is lined up.
        public static bool ShowsKicker(World w, Player p) =>
            p != null && w.Cfg.Feature("kickerModel") && w.Phase != Phase.Huddle && Mode(w) != KickKind.None && p == Kicker(w);

        // The kicker before the kick: his phone shows the kick meter (and nobody else's does).
        public static bool WaitingToKick(World w, Player p)
        {
            if (Mode(w) == KickKind.None || w.Kick.Kicked) return false;
            if (w.Phase != Phase.Set && w.Phase != Phase.Live) return false;
            return p != null && p == Kicker(w);
        }

        // A field goal's length as the TV says it: from the spot of the kick to the posts on the end line.
        public static int KickYards(World w)
        {
            if (Mode(w) != KickKind.FieldGoal) return 0;
            var f = w.Cfg.field;
            return (int)Math.Round(f.HalfLength - w.LineOfScrimmageX * w.Dir + w.Cfg.kicking.fieldGoalBackYd);
        }

        // ---- the playbook and the bot coach ----

        public static bool IsKickPlay(World w, PlayConfig play) => play != null && (play.id == w.Cfg.kicking.puntPlay || play.id == w.Cfg.kicking.fgPlay);

        // Whether a play belongs in the playbook right now: PUNT on 4th down, FIELD GOAL on 4th down or a try.
        // Every other play always. With kicking off the two kicks never show (so the bot coach's picks never change).
        public static bool InBook(World w, PlayConfig play)
        {
            if (!IsKickPlay(w, play)) return true;
            if (!On(w)) return false;
            bool fourth = w.Down >= w.Cfg.downs.downsToGain;
            if (play.id == w.Cfg.kicking.fgPlay) return w.Kick.Try || fourth;
            return fourth && !w.Kick.Try;
        }

        // The bot coach's kick: the extra point on a try; on 4th down a field goal in range, else a punt unless it is
        // short (botGoForItYd or less to go). Inside botGoForItGoalYd of the goal line on 4th and short it goes for the
        // touchdown instead of the field goal. Null: no kick, pick a play as usual.
        public static string BotCall(World w, int team)
        {
            if (!On(w) || team != w.Offense) return null;
            var c = w.Cfg.kicking;
            if (w.Kick.Try) return Has(w, c.fgPlay);
            if (w.Down < w.Cfg.downs.downsToGain) return null;
            float togo = (w.FirstDownX - w.LineOfScrimmageX) * w.Dir;
            float toGoal = w.Cfg.field.GoalLineX - w.LineOfScrimmageX * w.Dir;
            if (togo <= c.botGoForItYd && toGoal <= c.botGoForItGoalYd) return null;
            if (KickYardsFrom(w, w.LineOfScrimmageX) <= c.botFieldGoalMaxYd) return Has(w, c.fgPlay);
            return togo > c.botGoForItYd ? Has(w, c.puntPlay) : null;
        }

        static float KickYardsFrom(World w, float los) => w.Cfg.field.HalfLength - los * w.Dir + w.Cfg.kicking.fieldGoalBackYd;

        static string Has(World w, string id) => Huddle.Find(w, id) != null ? id : null;

        // ---- every tick (after the huddle) ----

        public static void Step(World w)
        {
            if (!On(w)) return;
            var k = w.Kick;
            // the opening kickoff, as soon as the first huddle would start
            if (!k.Opened && w.HuddleNumber > 0)
            {
                k.Opened = true;
                int kicking = w.Cfg.kicking.openingKickTeam;
                if (kicking != w.Offense) { w.Offense = kicking; w.Dir = -w.Dir; }
                w.LineOfScrimmageX = -w.Dir * (w.Cfg.field.GoalLineX - w.Cfg.kicking.kickoffFromYd);
                GoKickoff(w);
                return;
            }
            // the second half (FEATURES.clock starts it with a huddle): the team that received the opening kickoff
            // kicks off instead, from its own 35, attacking the way the clock turned it
            if (Clock.On(w) && w.GameClock.SecondHalf && !k.SecondHalf && w.Phase == Phase.Huddle)
            {
                k.SecondHalf = true;
                k.Try = false;
                int kicking = w.Cfg.kicking.openingKickTeam == 0 ? 1 : 0;
                if (kicking != w.Offense) { w.Offense = kicking; w.Dir = -w.Dir; }
                w.LineOfScrimmageX = -w.Dir * (w.Cfg.field.GoalLineX - w.Cfg.kicking.kickoffFromYd);
                GoKickoff(w);
                return;
            }
            if (w.Phase == Phase.Huddle)
            {
                if (k.PendingKickoff) { GoKickoff(w); return; }
                k.NewPlay();
                return;
            }
            if (w.Phase == Phase.Set && !k.Placed)
            {
                k.Placed = true;
                Align(w);
            }
        }

        // Straight from the whistle to the kickoff line-up: no huddle, nothing to call.
        static void GoKickoff(World w)
        {
            var k = w.Kick;
            var c = w.Cfg.kicking;
            k.PendingKickoff = false;
            k.NewPlay();
            k.Kickoff = true;
            k.Placed = true;
            for (int t = 0; t < w.Calls.Length; t++) w.Calls[t] = null;
            w.Suggestions.Clear();
            w.Phase = Phase.Set;
            w.Down = 1;
            w.FirstDownX = w.LineOfScrimmageX;
            w.LineUp();
            foreach (var p in w.Players)
            {
                var spots = p.Team == w.Offense ? c.kickoffKicking : c.kickoffReceiving;
                if (p.Spot >= 0 && p.Spot < spots.Count) Place(w, p, spots[p.Spot]);
            }
            var kicker = Kicker(w);
            w.PhaseTicks = Huddle.Ticks(w, kicker != null && kicker.Human ? c.humanKickMaxS : c.kickoffSetS);
        }

        // Punt and field goal line-ups: the kicker deep, and on a punt one receiver back to field it.
        static void Align(World w)
        {
            var c = w.Cfg.kicking;
            var kind = Mode(w);
            var kicker = Kicker(w);
            if (kind == KickKind.Punt)
            {
                if (kicker != null) Place(w, kicker, new[] { -c.puntBackYd, 0f });
                var returner = LivePlay.SpotOf(w, w.Defense, c.returnerSpot);
                if (returner != null && c.puntReturner.Length > 1) Place(w, returner, c.puntReturner);
            }
            else if (kind == KickKind.FieldGoal && kicker != null) Place(w, kicker, new[] { -c.fieldGoalBackYd, 0f });
        }

        // [ahead, z] from the line of scrimmage, ahead = the offense's (the kicking team's) way.
        static void Place(World w, Player p, float[] at)
        {
            p.HomeX = w.LineOfScrimmageX + w.Dir * at[0];
            p.HomeZ = at[1];
            p.X = p.HomeX; p.Z = p.HomeZ; p.VX = p.VZ = 0f;
            p.GoalX = p.X; p.GoalZ = p.Z;
        }

        // ---- the kick ----

        // A person's kick from the phone meter: power 0..1, aim -1 (kicker's left) .. 1 (right). In the line-up it snaps
        // the ball first. False when this person isn't the kicker right now.
        public static bool Kick(World w, int slot, float power, float aim)
        {
            var kind = Mode(w);
            var kicker = Kicker(w);
            if (kind == KickKind.None || w.Kick.Kicked || kicker == null || kicker.Slot != slot) return false;
            if (w.Phase == Phase.Set) LivePlay.Hike(w);
            if (w.Phase != Phase.Live || LivePlay.Carrier(w) != kicker) return false;
            Launch(w, kicker, kind, power, aim);
            return true;
        }

        static void Launch(World w, Player kicker, KickKind kind, float power, float aim)
        {
            var c = w.Cfg.kicking;
            var b = w.Ball;
            var k = w.Kick;
            power = Clamp(power, 0f, 1f);
            aim = Clamp(aim, -1f, 1f);
            float speed = c.minSpeedYdS + (c.maxSpeedYdS - c.minSpeedYdS) * power;
            float climb = Angle(c, kind) * Rad, yaw = aim * c.maxAimDeg * Rad;
            float flat = speed * (float)Math.Cos(climb);
            b.State = BallState.Flying;
            b.Carrier = -1;
            b.X = kicker.X; b.Z = kicker.Z; b.Y = c.kickHeightYd;
            // aim right = the kicker's right hand: -Z going +X, +Z going -X
            b.VX = w.Dir * flat * (float)Math.Cos(yaw);
            b.VZ = -w.Dir * flat * (float)Math.Sin(yaw);
            b.VY = speed * (float)Math.Sin(climb);
            // where it comes down to catching height (the shadow marker, the camera and the returner run there)
            float g = w.Cfg.ball.gravityYdS2, drop = b.Y - w.Cfg.ball.catchHeightYd;
            float t = (b.VY + (float)Math.Sqrt(b.VY * b.VY + (g + g) * drop)) / g;
            b.TargetX = b.X + b.VX * t;
            b.TargetZ = b.Z + b.VZ * t;
            k.Kicked = true;
            k.KickTick = w.Tick;
            k.Power = power;
            k.Aim = aim;
            w.Thrown = true;
        }

        static float Angle(KickingConfig c, KickKind kind) =>
            kind == KickKind.Punt ? c.puntAngleDeg : kind == KickKind.FieldGoal ? c.fieldGoalAngleDeg : c.kickoffAngleDeg;

        // The bot kicker: kickoffs and punts at a set power; a field goal just hard enough to clear the bar by
        // botFieldGoalClearYd, aimed at the middle of the posts. A little noise on both.
        static void BotKick(World w, Player kicker, KickKind kind)
        {
            var c = w.Cfg.kicking;
            float power = kind == KickKind.Kickoff ? c.botKickoffPower : c.botPuntPower;
            float aimDeg = 0f;
            if (kind == KickKind.FieldGoal)
            {
                float d = Math.Abs(w.Dir * w.Cfg.field.HalfLength - kicker.X);
                power = FieldGoalPower(w, d) + c.botFieldGoalExtraPower;
                aimDeg = (float)Math.Atan2(kicker.Z * w.Dir, d) / Rad;
            }
            power += w.Rng.Range(-c.botPowerNoise, c.botPowerNoise);
            aimDeg += w.Rng.Range(-c.botAimNoiseDeg, c.botAimNoiseDeg);
            Launch(w, kicker, kind, power, aimDeg / c.maxAimDeg);
        }

        // The power that clears the crossbar by botFieldGoalClearYd at d yards (1 = as hard as he can).
        public static float FieldGoalPower(World w, float d)
        {
            var c = w.Cfg.kicking;
            float climb = c.fieldGoalAngleDeg * Rad, cos = (float)Math.Cos(climb);
            float rise = c.kickHeightYd + d * (float)Math.Tan(climb) - (c.crossbarYd + c.botFieldGoalClearYd);
            if (rise <= 0f) return 1f;
            float v = (float)Math.Sqrt(w.Cfg.ball.gravityYdS2 * d * d / ((cos * cos + cos * cos) * rise));
            return Clamp((v - c.minSpeedYdS) / (c.maxSpeedYdS - c.minSpeedYdS), 0f, 1f);
        }

        // ---- a live kick (called first thing in LivePlay.Step) ----

        // True: this tick of the play is the kick's (LivePlay leaves it alone). False: an ordinary play, or a receiver
        // has the ball and LivePlay runs the return like an interception return.
        public static bool LiveStep(World w)
        {
            var kind = Mode(w);
            var k = w.Kick;
            if (kind == KickKind.None || k.Returned) return false;
            var c = w.Cfg.kicking;
            var f = w.Cfg.field;
            var b = w.Ball;
            float dir = w.Dir;
            w.PlayTicks++;

            if (!k.Kicked)
            {
                var kicker = Kicker(w);
                if (kicker == null || LivePlay.Carrier(w) != kicker) { LivePlay.Whistle(w, "PLAY OVER", w.LineOfScrimmageX); return true; }
                bool bot = !kicker.Human || kicker.BotHelp || w.PlayTicks >= Huddle.Ticks(w, c.humanKickMaxS);
                if (bot && w.PlayTicks >= Huddle.Ticks(w, Delay(c, kind))) BotKick(w, kicker, kind);
                return true;
            }
            if (b.State == BallState.Held) return false;
            if (b.State == BallState.Loose || b.Y <= 0f) k.Landed = true;

            if (kind == KickKind.FieldGoal)
            {
                // through the posts on the end line, above the bar
                if (b.X * dir >= f.HalfLength)
                {
                    bool good = b.Y >= c.crossbarYd && Math.Abs(b.Z) <= c.postsHalfWidthYd;
                    LivePlay.Whistle(w, good ? GoodName(w) : "NO GOOD", w.LineOfScrimmageX);
                }
                else if (k.Landed || Math.Abs(b.Z) > f.HalfWidth) LivePlay.Whistle(w, "NO GOOD", w.LineOfScrimmageX);
                return true;
            }

            // kickoffs and punts
            if (Math.Abs(b.X) > f.HalfLength) { LivePlay.Whistle(w, "TOUCHBACK", b.X); return true; }
            if (Math.Abs(b.Z) > f.HalfWidth) { LivePlay.Whistle(w, "OUT OF BOUNDS", b.X); return true; }
            if (b.Y <= w.Cfg.play.catchMaxHeightYd && (b.VY < 0f || k.Landed))
            {
                // the nearest player in reach: in the air only a receiver can catch it, on the ground anyone gets there
                float reach = k.Landed ? c.pickupYd : w.Cfg.play.catchRadiusYd;
                Player near = null; float nd = float.MaxValue;
                foreach (var p in w.Players)
                {
                    if (!k.Landed && (p.Team == w.Offense || w.CatchTried.Contains(p.Slot))) continue;
                    float dx = p.X - b.X, dz = p.Z - b.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d <= reach && d < nd) { near = p; nd = d; }
                }
                if (near != null && near.Team == w.Offense) { LivePlay.Whistle(w, "DOWNED", b.X); return true; }
                if (near != null)
                {
                    bool caught = true;
                    if (!k.Landed) { w.CatchTried.Add(near.Slot); caught = w.Rng.Value() < c.catchChance; }
                    if (caught)
                    {
                        b.GiveTo(near, w.Cfg.ball);
                        if (near.X * dir > f.GoalLineX) { LivePlay.Whistle(w, "TOUCHBACK", near.X); return true; }
                        k.Returned = true;
                        w.Intercepted = true;   // the run back: his team blocks, the kicking team chases (Bots, LivePlay)
                        return true;
                    }
                }
            }
            // it stopped rolling in the end zone, or nobody picked it up in time
            bool still = b.State == BallState.Loose && b.VX == 0f && b.VZ == 0f;
            if (still && b.X * dir > f.GoalLineX) { LivePlay.Whistle(w, "TOUCHBACK", b.X); return true; }
            if (w.PlayTicks >= Huddle.Ticks(w, w.Cfg.play.maxPlayS)) LivePlay.Whistle(w, "DOWNED", b.X);
            return true;
        }

        static float Delay(KickingConfig c, KickKind kind) =>
            kind == KickKind.Punt ? c.botPuntDelayS : kind == KickKind.FieldGoal ? c.botFieldGoalDelayS : c.botKickoffDelayS;

        static string GoodName(World w) => w.Kick.Try ? "EXTRA POINT" : "FIELD GOAL";

        // ---- the bots during a kick (called first thing in Bots.Live); null: the usual brains ----

        public static PlayerInput? BotThink(World w, Player p)
        {
            var kind = Mode(w);
            var k = w.Kick;
            if (kind == KickKind.None || k.Returned) return null;
            var kicker = Kicker(w);
            var arrive = w.Cfg.bots.arriveYd;
            if (!k.Kicked)
            {
                if (p == kicker) return default(PlayerInput);
                // kickoff: everyone holds his spot until the ball is in the air (no running before the kick)
                if (kind == KickKind.Kickoff) return To(p, p.HomeX, p.HomeZ, false, arrive);
                // a punt's returner stays back; everyone else plays the called jobs (block, rush, cover)
                if (kind == KickKind.Punt && p.Team == w.Defense && p.Spot == w.Cfg.kicking.returnerSpot) return To(p, p.HomeX, p.HomeZ, false, arrive);
                return null;
            }
            if (kind == KickKind.FieldGoal || p == kicker) return default(PlayerInput);
            var b = w.Ball;
            float tx = k.Landed ? b.X : b.TargetX, tz = k.Landed ? b.Z : b.TargetZ;
            // the kicking team covers: everyone to where it comes down
            if (p.Team == w.Offense) return To(p, tx, tz, true, 0f);
            // the receiving team: the man nearest the spot goes for the ball, the rest block for him
            if (p == Returner(w, tx, tz)) return To(p, tx, tz, true, 0f);
            Player mark = null; float md = float.MaxValue;
            foreach (var o in w.Players)
            {
                if (o.Team != w.Offense || o == kicker) continue;
                float d = LivePlay.Dist(p, o);
                if (d < md) { md = d; mark = o; }
            }
            if (mark == null) return default(PlayerInput);
            // stand between him and the ball
            float dx = tx - mark.X, dz = tz - mark.Z, dl = (float)Math.Sqrt(dx * dx + dz * dz);
            float gx = mark.X, gz = mark.Z;
            if (dl > 0f) { gx += dx / dl * arrive; gz += dz / dl * arrive; }
            var i = To(p, gx, gz, false, 0f);
            i.Block = true;
            return i;
        }

        static Player Returner(World w, float x, float z)
        {
            Player best = null; float bd = float.MaxValue;
            foreach (var p in w.Players)
            {
                if (p.Team != w.Defense) continue;
                float dx = p.X - x, dz = p.Z - z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        static PlayerInput To(Player p, float x, float z, bool sprint, float arrive)
        {
            float dx = x - p.X, dz = z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= arrive || d <= 0f) return default;
            return new PlayerInput { MoveX = dx / d, MoveZ = dz / d, Sprint = sprint };
        }

        // ---- after the whistle (called first thing in Downs.Apply); true: handled, Downs leaves it alone ----

        public static bool Apply(World w, string result, float spotX)
        {
            if (!On(w)) return false;
            var k = w.Kick;
            var c = w.Cfg.kicking;
            var d = w.Cfg.downs;
            float goal = w.Cfg.field.GoalLineX, dir = w.Dir;
            var kind = Mode(w);
            w.Note = "";

            // the try: whatever happens, the team that scored kicks off next
            if (k.Try)
            {
                k.Try = false;
                if (kind == KickKind.FieldGoal && result == "EXTRA POINT") w.Score[w.Offense] += c.tryKickPoints;
                else if (result == "TOUCHDOWN") { w.Score[w.Offense] += c.tryTouchdownPoints; w.Note = "TWO POINTS"; }
                else if (result == "PICK SIX") { w.Score[w.Defense] += c.tryTouchdownPoints; w.Note = "TWO POINTS"; }
                KickoffBy(w, w.Offense, c.kickoffFromYd);
                return true;
            }

            if (kind == KickKind.Kickoff || kind == KickKind.Punt)
            {
                bool kickoff = kind == KickKind.Kickoff;
                k.Kickoff = false;
                if (k.Returned)
                {
                    // the run back ended: name it what it was, not an interception
                    var carrier = w.Find(w.Ball.Carrier);
                    Rename(w, result == "PICK SIX" ? "TOUCHDOWN" : carrier != null && carrier.OutOfBounds ? "OUT OF BOUNDS" : "TACKLED");
                    w.Note = kickoff ? "KICK RETURN" : "PUNT RETURN";
                    if (result == "PICK SIX") { w.Score[w.Defense] += d.touchdownPoints; TryFor(w, w.Defense); return true; }
                }
                float touchback = dir * (goal - (kickoff ? c.kickoffTouchbackYd : c.puntTouchbackYd));
                float x = spotX;
                if (result == "TOUCHBACK" || x * dir > goal) { x = touchback; Rename(w, "TOUCHBACK"); }
                else if (result == "OUT OF BOUNDS" && kickoff && !k.Returned) x = dir * (goal - c.kickoffOutOfBoundsYd);
                GiveBall(w, w.Defense, Clamp(x, -goal, goal));
                return true;
            }

            if (kind == KickKind.FieldGoal)
            {
                if (result == "FIELD GOAL") { w.Score[w.Offense] += c.fieldGoalPoints; KickoffBy(w, w.Offense, c.kickoffFromYd); }
                else GiveBall(w, w.Defense, w.LineOfScrimmageX);
                return true;
            }

            // ordinary plays: touchdowns bring a try, a tackle in your own end zone is a safety
            if (result == "TOUCHDOWN") { w.Score[w.Offense] += d.touchdownPoints; TryFor(w, w.Offense); return true; }
            if (result == "PICK SIX") { w.Score[w.Defense] += d.touchdownPoints; TryFor(w, w.Defense); return true; }
            if (result == "INTERCEPTED" && spotX * dir > goal)
            {
                // run back no further than his own end zone: a touchback
                GiveBall(w, w.Defense, dir * (goal - c.interceptionTouchbackYd));
                w.Note = "TOUCHBACK";
                return true;
            }
            if (result != "INCOMPLETE" && result != "INTERCEPTED" && spotX * dir < -goal)
            {
                Rename(w, "SAFETY");
                w.Score[w.Defense] += c.safetyPoints;
                KickoffBy(w, w.Offense, c.safetyKickFromYd);
                return true;
            }
            return false;
        }

        // A score: the next play is `team`'s try, tryFromYd from the goal it attacks.
        static void TryFor(World w, int team)
        {
            float tdir = team == w.Offense ? w.Dir : -w.Dir;
            float goal = w.Cfg.field.GoalLineX;
            w.Kick.Try = true;
            w.PendingTurnover = team != w.Offense;
            w.NextLos = tdir * (goal - w.Cfg.kicking.tryFromYd);
            w.Down = 1;
            w.FirstDownX = tdir * goal;
            w.Note = "TRY NEXT";
        }

        // `team` kicks off next, from fromYd out of its own goal line (the 35; the 20 after a safety).
        static void KickoffBy(World w, int team, float fromYd)
        {
            float tdir = team == w.Offense ? w.Dir : -w.Dir;
            w.Kick.PendingKickoff = true;
            w.PendingTurnover = team != w.Offense;
            w.NextLos = -tdir * (w.Cfg.field.GoalLineX - fromYd);
            w.Down = 1;
            w.FirstDownX = w.NextLos;
            if (w.Note.Length == 0) w.Note = "KICKOFF NEXT";
        }

        // `team` gets the ball at x: first and ten going its way.
        static void GiveBall(World w, int team, float x)
        {
            float tdir = team == w.Offense ? w.Dir : -w.Dir;
            float goal = w.Cfg.field.GoalLineX;
            w.PendingTurnover = team != w.Offense;
            w.NextLos = x;
            w.Down = 1;
            float to = x + tdir * w.Cfg.downs.firstDownYd;
            w.FirstDownX = to * tdir > goal ? tdir * goal : to;
        }

        // The whistle already counted the result under its first name: move the count over.
        static void Rename(World w, string result)
        {
            if (w.Result == result) return;
            if (w.Results.TryGetValue(w.Result, out int n))
            {
                if (n > 1) w.Results[w.Result] = n - 1;
                else w.Results.Remove(w.Result);
            }
            w.Result = result;
            w.Results[result] = (w.Results.TryGetValue(result, out int m) ? m : 0) + 1;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // The kicking state of one game (World.Kick).
    public sealed class KickState
    {
        public bool Opened;            // the opening kickoff has been set up
        public bool SecondHalf;        // the second-half kickoff has been set up (FEATURES.clock)
        public bool PendingKickoff;    // after the whistle: a kickoff instead of a huddle
        public bool Kickoff;           // this play is a kickoff
        public bool Try;               // this play is a try (after a touchdown)
        // this play
        public bool Placed, Kicked, Landed, Returned;
        public int KickTick;
        public float Power, Aim;       // the last kick, for the TV meter

        public void NewPlay() { Placed = false; Kicked = false; Landed = false; Returned = false; }
    }

    // Config/game.json "kicking". Yards, seconds and degrees; odds 0..1. Spots are [ahead, z] from the line, ahead =
    // the kicking team's way, in formation spot order.
    public sealed class KickingConfig
    {
        public string puntPlay = "punt", fgPlay = "fg";
        public int openingKickTeam, returnerSpot;
        public float minSpeedYdS, maxSpeedYdS, kickoffAngleDeg, puntAngleDeg, fieldGoalAngleDeg, maxAimDeg, kickHeightYd;
        public float kickoffFromYd, safetyKickFromYd, kickoffSetS, humanKickMaxS;
        public float botKickoffDelayS, botPuntDelayS, botFieldGoalDelayS;
        public float kickoffTouchbackYd, puntTouchbackYd, kickoffOutOfBoundsYd, interceptionTouchbackYd;
        public float tryFromYd;
        public int tryKickPoints, tryTouchdownPoints, fieldGoalPoints, safetyPoints;
        public float crossbarYd, postsHalfWidthYd, puntBackYd, fieldGoalBackYd;
        public float catchChance, pickupYd;
        public float botKickoffPower, botPuntPower, botPowerNoise, botAimNoiseDeg, botFieldGoalClearYd, botFieldGoalExtraPower;
        public float botFieldGoalMaxYd, botGoForItYd, botGoForItGoalYd, meterShowS;
        public float[] puntReturner = new float[0];
        public List<float[]> kickoffKicking = new List<float[]>();
        public List<float[]> kickoffReceiving = new List<float[]>();
    }
}
