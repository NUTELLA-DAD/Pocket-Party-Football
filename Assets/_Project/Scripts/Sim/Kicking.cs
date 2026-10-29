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
    //
    // Field goals and extra points (the kick from a hold):
    //   FEATURES.fgHolder (NO_FG_HOLDER=1): the snap goes to a holder (kicking.holderSpot, the RB's spot) kneeling
    //     fieldGoalBackYd deep; the kicker stands kickerBackYd behind him and kickerSideYd to his left, and once he
    //     kicks he runs up to the ball (approachSpeedYdS) and the ball leaves the holder's spot.
    //   FEATURES.kickSweep (NO_KICK_SWEEP=1): two taps instead of a swipe. An arrow sweeps left and right (aimSweepS for
    //     a full sweep there and back): the first tap sets the aim and snaps the ball. Then the arrow sweeps up and down
    //     (powerSweepS): the second tap sets the power, and the arc climbs with it (fgMinAngleDeg..fgMaxAngleDeg).
    //     The very top of the sweep (shankFromPower and up) is too much: the kick hooks up to shankAimDeg either way.
    //     The sweep is a plain back-and-forth line (Sweep), the same sum on the phone and the TV.
    //   FEATURES.kickSync (NO_KICK_SYNC=1): the kicker's phone runs its arrow from the game's own sweep clock (sent
    //     again every phoneSyncS), and a tap takes the arrow where the phone showed it (TapAt), so the phone, the TV
    //     and the kick agree even when the game's clock runs behind the phone's.
    //   FEATURES.kickArc (NO_KICK_ARC=1): punts and field goals get the arc instead (it takes over from kickSweep).
    //     Lined up, a shaded arc on the field shows the kick; the kicker steers it left and right (Steer: the phone's
    //     arrows or a drag, the pad's stick) and snaps the ball himself (the meter's HIKE: SetAim with that aim).
    //     Then two taps: the POWER meter runs up and down (powerSweepS) and the first tap stops it (SetPower); then the
    //     PITCH ANGLE needle runs up and down (pitchSweepS, arcMinPitchDeg..arcMaxPitchDeg) and the second tap stops it
    //     and kicks at that power and that climb (KickPitched). The top of the power (shankFromPower) still hooks.
    //     With kickSync the phone's taps carry the second its meter showed (TapAt), like the sweep's.
    //   FEATURES.wind (NO_WIND=1): a wind of up to windMaxYdS from any side, new every windEveryHuddles huddles, drawn
    //     from the game's seed (no draw from the sim's random numbers); it pushes a field goal in the air by windPush
    //     per yard a second. The flags on top of the posts show it (View/Stadium.cs).
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

        // A person is kicking this one, not the bot standing in for an idle person (the same rule as LiveStep's: with
        // taps a person counts as idle only once the line-up ran out on him). The TV shows the meters only for a person.
        public static bool PersonKicks(World w, Player kicker) =>
            kicker != null && kicker.Human && !(kicker.BotHelp && (!TapsOn(w) || w.Kick.AutoSnap));

        // ---- the hold (FEATURES.fgHolder) ----

        public static bool HolderOn(World w) => w.Cfg.Feature("fgHolder") && Mode(w) == KickKind.FieldGoal;

        // The holder, from the field goal's line-up to the whistle (null: no hold this play).
        public static Player Holder(World w) =>
            HolderOn(w) && w.Kick.Placed && (w.Phase == Phase.Set || w.Phase == Phase.Live) ? LivePlay.SpotOf(w, w.Offense, w.Cfg.kicking.holderSpot) : null;

        // Who the snap goes to: the holder on a field goal, else the QB's spot (the punter on a punt).
        public static Player SnapTaker(World w) => Holder(w) ?? LivePlay.Qb(w);

        // The holder has it down on the spot, waiting for the kick (the TV draws the ball standing on its point there).
        public static bool Holding(World w)
        {
            var h = Holder(w);
            return h != null && !w.Kick.Kicked && w.Ball.State == BallState.Held && w.Ball.Carrier == h.Slot;
        }

        // ---- the two taps (FEATURES.kickSweep) ----

        public static bool SweepOn(World w) => w.Cfg.Feature("kickSweep") && !w.Cfg.Feature("kickArc") && Mode(w) == KickKind.FieldGoal;

        // FEATURES.kickArc: a punt or a field goal is steered, snapped and then two taps (see the top).
        public static bool ArcOn(World w)
        {
            if (!w.Cfg.Feature("kickArc")) return false;
            var kind = Mode(w);
            return kind == KickKind.Punt || kind == KickKind.FieldGoal;
        }

        // Taps instead of a swipe this play (the sweep or the arc).
        public static bool TapsOn(World w) => SweepOn(w) || ArcOn(w);

        public enum SweepStage { None, Aim, Power, Pitch }

        // Where the kicker is in his taps: Aim while he lines up (the sweep's first tap, or the arc's HIKE, snaps it),
        // Power until the next tap; with the arc, then Pitch until the kick.
        public static SweepStage Stage(World w)
        {
            var k = w.Kick;
            if (!TapsOn(w) || k.Kicked || k.Approach || !k.Placed) return SweepStage.None;
            if (w.Phase == Phase.Set && !k.AimSet) return SweepStage.Aim;
            if (w.Phase == Phase.Live && k.AimSet) return k.PowerSet && ArcOn(w) ? SweepStage.Pitch : SweepStage.Power;
            return SweepStage.None;
        }

        // A plain back-and-forth: 0 -> 1 -> 0 over one period (no sine: a straight line each way).
        public static float Triangle(float t, float period)
        {
            if (period <= 0f) return 0f;
            float x = t / period;
            x -= (float)Math.Floor(x);
            return x < 0.5f ? x + x : (1f - x) + (1f - x);
        }

        // The aim arrow, `seconds` into its sweep: -1 (the kicker's left) .. 1 (right), starting in the middle going right.
        public static float AimSweep(KickingConfig c, float seconds)
        {
            float t = Triangle(seconds + c.aimSweepS * 0.5f * 0.5f, c.aimSweepS);
            return t + t - 1f;
        }

        // The power arrow, `seconds` into its sweep: 0 .. 1 and back, starting at nothing.
        public static float PowerSweep(KickingConfig c, float seconds) => Triangle(seconds, c.powerSweepS);

        // FEATURES.kickArc: the pitch needle, `seconds` into its sweep: 0 (arcMinPitchDeg) .. 1 (arcMaxPitchDeg) and back.
        public static float PitchSweep(KickingConfig c, float seconds) => Triangle(seconds, c.pitchSweepS);

        // The needle's share (0..1) as the climb in degrees.
        public static float PitchDeg(KickingConfig c, float share) => c.arcMinPitchDeg + (c.arcMaxPitchDeg - c.arcMinPitchDeg) * Clamp(share, 0f, 1f);

        // Game seconds into the arrow that is sweeping now (0 when none). The phone runs its arrow from this, so it shows
        // the same arrow as the TV even when the game's clock runs behind the phone's (a slow frame, the pause menu).
        public static float SweepSeconds(World w)
        {
            var k = w.Kick;
            float hz = w.Cfg.sim.tickHz;
            switch (Stage(w))
            {
                case SweepStage.Aim: return ArcOn(w) ? 0f : (w.Tick - k.AimTick) / hz;   // (the arc's aim is steered, not swept)
                case SweepStage.Power: return (w.Tick - k.PowerTick) / hz;
                case SweepStage.Pitch: return (w.Tick - k.PitchTick) / hz;
            }
            return 0f;
        }

        // The arrow right now (what a tap would set): the aim in the line-up, the power after the snap.
        public static float SweepNow(World w)
        {
            var c = w.Cfg.kicking;
            switch (Stage(w))
            {
                case SweepStage.Aim: return ArcOn(w) ? w.Kick.Aim : AimSweep(c, SweepSeconds(w));
                case SweepStage.Power: return PowerSweep(c, SweepSeconds(w));
                case SweepStage.Pitch: return PitchSweep(c, SweepSeconds(w));
            }
            return 0f;
        }

        // FEATURES.kickArc: the kick the TV's arc shows now. Lined up: arcPreviewPower at the kind's own climb; then
        // the power running; then the power he stopped with and the pitch running.
        public static void ArcNow(World w, out float power, out float pitchDeg)
        {
            var c = w.Cfg.kicking;
            var k = w.Kick;
            var stage = Stage(w);
            power = stage == SweepStage.Power ? SweepNow(w) : stage == SweepStage.Pitch ? k.ChosenPower : c.arcPreviewPower;
            pitchDeg = stage == SweepStage.Pitch ? PitchDeg(c, SweepNow(w)) : Angle(c, Mode(w));
        }

        // FEATURES.kickArc: lined up, the kicker steers his arc left (-1) and right (1). False when he can't now.
        public static bool Steer(World w, int slot, float aim)
        {
            var kicker = Kicker(w);
            if (!ArcOn(w) || Stage(w) != SweepStage.Aim || kicker == null || kicker.Slot != slot) return false;
            w.Kick.Aim = Clamp(aim, -1f, 1f);
            kicker.IdleTicks = 0;   // (steering is a touch of the phone, like the stick)
            return true;
        }

        // FEATURES.kickArc: the first tap after the snap stops the power meter; the pitch needle starts.
        public static bool SetPower(World w, int slot, float power)
        {
            var k = w.Kick;
            var kicker = Kicker(w);
            if (!ArcOn(w) || Stage(w) != SweepStage.Power || kicker == null || kicker.Slot != slot) return false;
            k.PowerSet = true;
            k.ChosenPower = Clamp(power, 0f, 1f);
            k.PitchTick = w.Tick;
            kicker.IdleTicks = 0;
            return true;
        }

        // FEATURES.kickArc: the second tap stops the pitch needle (share 0..1) and kicks at the power and aim he set.
        public static bool KickPitched(World w, int slot, float share)
        {
            var k = w.Kick;
            if (!ArcOn(w) || Stage(w) != SweepStage.Pitch) return false;
            var kicker = Kicker(w);
            if (kicker == null || kicker.Slot != slot) return false;
            k.PitchSet = true;
            k.Pitch = PitchDeg(w.Cfg.kicking, share);
            if (Kick(w, slot, k.ChosenPower, k.Aim)) return true;
            k.PitchSet = false;
            return false;
        }

        // The first tap: the aim is set and the ball is snapped. False when this person isn't the kicker lining up.
        public static bool SetAim(World w, int slot, float aim)
        {
            var k = w.Kick;
            var kicker = Kicker(w);
            if (Stage(w) != SweepStage.Aim || kicker == null || kicker.Slot != slot) return false;
            k.AimSet = true;
            k.Aim = Clamp(aim, -1f, 1f);
            LivePlay.Hike(w);
            k.PowerTick = w.Tick;
            if (ArcOn(w)) kicker.IdleTicks = 0;   // (FEATURES.kickArc: HIKE is a touch, so the bot doesn't step in for him)
            return true;
        }

        // A tap of the pad's or phone's one kick button: whichever arrow is sweeping, at the sim's own sweep.
        public static bool Tap(World w, int slot)
        {
            var stage = Stage(w);
            if (stage == SweepStage.Aim) return SetAim(w, slot, SweepNow(w));
            if (stage == SweepStage.Power) return ArcOn(w) ? SetPower(w, slot, SweepNow(w)) : Kick(w, slot, SweepNow(w), w.Kick.Aim);
            if (stage == SweepStage.Pitch) return KickPitched(w, slot, SweepNow(w));
            return false;
        }

        // FEATURES.kickSync: a phone's tap, with the game seconds into the sweep its arrow showed (it runs from
        // SweepSeconds). The arrow is taken there, so the kick goes where both the phone and the TV pointed; the
        // phone's seconds count only up to tapSlackS behind the game's (the trip over the wifi), never ahead of it.
        public static bool TapAt(World w, int slot, float seconds)
        {
            var c = w.Cfg.kicking;
            var stage = Stage(w);
            float now = SweepSeconds(w);
            float s = Clamp(seconds, Math.Max(0f, now - c.tapSlackS), now);
            if (stage == SweepStage.Aim) return SetAim(w, slot, ArcOn(w) ? w.Kick.Aim : AimSweep(c, s));
            if (stage == SweepStage.Power) return ArcOn(w) ? SetPower(w, slot, PowerSweep(c, s)) : Kick(w, slot, PowerSweep(c, s), w.Kick.Aim);
            if (stage == SweepStage.Pitch) return KickPitched(w, slot, PitchSweep(c, s));
            return false;
        }

        // The power that just clears the bar from the kick's spot with no wind (the phone marks it on the meter). With
        // FEATURES.kickArc the climb is his to pick, so it is the power that clears it at the field goal's own climb.
        public static float NeedPower(World w) => FieldGoalPower(w, KickDistance(w), 0f, ArcOn(w) ? w.Cfg.kicking.fieldGoalAngleDeg : -1f);

        // FEATURES.kickArc: the climbs (degrees, from arcMinPitchDeg up by arcWindowStepDeg) that clear the bar at this
        // power with no wind, lo..hi. False: none does (too weak, or a punt). The phone and the TV shade it on the dial.
        public static bool ClearPitch(World w, float power, out float lo, out float hi)
        {
            var c = w.Cfg.kicking;
            lo = hi = 0f;
            if (Mode(w) != KickKind.FieldGoal || c.arcWindowStepDeg <= 0f) return false;
            float d = KickDistance(w), need = c.crossbarYd;
            bool any = false;
            for (float p = c.arcMinPitchDeg; p <= c.arcMaxPitchDeg; p += c.arcWindowStepDeg)
            {
                if (Height(w, d, power, p) < need) continue;
                if (!any) lo = p;
                hi = p;
                any = true;
            }
            return any;
        }

        // ---- the wind (FEATURES.wind) ----

        public static bool WindOn(World w) => w.Cfg.Feature("wind") && On(w);

        // The wind now, yards a second along x and z (where it blows to). The same for every tick of a spell of
        // windEveryHuddles huddles, drawn from the game's seed.
        public static void Wind(World w, out float x, out float z)
        {
            x = z = 0f;
            if (!WindOn(w)) return;
            var c = w.Cfg.kicking;
            int spell = c.windEveryHuddles > 0 ? w.HuddleNumber / c.windEveryHuddles : 0;
            uint h = Mix((uint)w.Seed, (uint)spell);
            float speed = c.windMaxYdS * Unit(h) * Weather.Wind(w);   // FEATURES.weather: a storm blows harder
            float turn = (float)(Math.PI + Math.PI) * Unit(Mix(h, (uint)spell));
            x = speed * (float)Math.Cos(turn);
            z = speed * (float)Math.Sin(turn);
        }

        // The push on a field goal in the air this tick (yards a second, a second); zero for every other ball.
        public static void Drift(World w, out float ax, out float az)
        {
            ax = az = 0f;
            if (!w.Kick.Kicked || Mode(w) != KickKind.FieldGoal) return;
            Wind(w, out float x, out float z);
            ax = x * w.Cfg.kicking.windPush;
            az = z * w.Cfg.kicking.windPush;
        }

        static uint Mix(uint a, uint b) { unchecked { uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u); h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16; return h; } }   // hygiene:allow (murmur3's mixer)
        static float Unit(uint h) => (h >> 8) / 16777216f;   // hygiene:allow (24 bits -> 0..1)

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
            w.Kick.Aim = 0f;   // (FEATURES.kickArc: the arc starts straight down the field)
            if (kind == KickKind.Punt)
            {
                if (kicker != null) Place(w, kicker, new[] { -c.puntBackYd, 0f });
                var returner = LivePlay.SpotOf(w, w.Defense, c.returnerSpot);
                if (returner != null && c.puntReturner.Length > 1) Place(w, returner, c.puntReturner);
            }
            else if (kind == KickKind.FieldGoal && kicker != null)
            {
                var k = w.Kick;
                k.AimTick = w.Tick;
                k.HoldX = w.LineOfScrimmageX - w.Dir * c.fieldGoalBackYd;
                k.HoldZ = 0f;
                var holder = Holder(w);
                if (holder == null) { Place(w, kicker, new[] { -c.fieldGoalBackYd, 0f }); return; }
                // FEATURES.fgHolder: the holder on one knee at the spot (just to its right, from the kicker), the
                // kicker a few steps back and to the left of it; the snap goes to the holder
                Place(w, holder, new[] { -c.fieldGoalBackYd, -w.Dir * c.holderSideYd });
                Place(w, kicker, new[] { -(c.fieldGoalBackYd + c.kickerBackYd), w.Dir * c.kickerSideYd });
                w.Ball.GiveTo(holder, w.Cfg.ball);
            }
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
        // the ball first. False when this person isn't the kicker right now. With FEATURES.kickSweep a set aim (the
        // first tap) stands and `aim` is ignored; a kick without one (an old swipe) sets both at once. With
        // FEATURES.fgHolder the kicker runs up to the holder first, so the ball is in the air a moment later.
        public static bool Kick(World w, int slot, float power, float aim)
        {
            var kind = Mode(w);
            var kicker = Kicker(w);
            var k = w.Kick;
            if (kind == KickKind.None || k.Kicked || k.Approach || kicker == null || kicker.Slot != slot) return false;
            bool sweep = TapsOn(w);
            if (sweep && !k.AimSet)
            {
                // no first tap yet: this kick's aim is the aim (in the line-up it snaps the ball too)
                if (w.Phase == Phase.Set) { if (!SetAim(w, slot, aim)) return false; }
                else { k.AimSet = true; k.Aim = Clamp(aim, -1f, 1f); k.PowerTick = w.Tick; }
            }
            if (sweep) aim = k.Aim;
            if (w.Phase == Phase.Set) LivePlay.Hike(w);
            if (w.Phase != Phase.Live || LivePlay.Carrier(w) != BallMan(w)) return false;
            if (Holder(w) != null) RunUp(w, kicker, power, aim, !sweep);
            else Launch(w, kicker, kind, power, aim, !sweep);
            return true;
        }

        // Who has the ball before the kick: the holder, else the kicker himself.
        static Player BallMan(World w) => Holder(w) ?? Kicker(w);

        // FEATURES.fgHolder: the kick is on, the kicker runs up to the ball (LiveStep moves him and kicks once there).
        static void RunUp(World w, Player kicker, float power, float aim, bool exact)
        {
            var k = w.Kick;
            k.Approach = true;
            k.ApproachTick = w.Tick;
            k.StartX = kicker.X; k.StartZ = kicker.Z;
            k.PendingPower = power; k.PendingAim = aim; k.PendingExact = exact;
        }

        // One tick of the run-up; true once he is at the ball and the holder has had it for holdReadyS.
        static bool RunUpStep(World w, Player kicker)
        {
            var c = w.Cfg.kicking;
            var k = w.Kick;
            float tx = k.HoldX - w.Dir * c.kickContactYd, tz = k.HoldZ;
            float dx = tx - k.StartX, dz = tz - k.StartZ, len = (float)Math.Sqrt(dx * dx + dz * dz);
            float run = (w.Tick - k.ApproachTick) * w.Dt * c.approachSpeedYdS;
            bool there = run >= len || len <= 0f;
            float f = there ? 1f : run / len;
            kicker.X = k.StartX + dx * f; kicker.Z = k.StartZ + dz * f;
            kicker.VX = there ? 0f : dx / len * c.approachSpeedYdS;
            kicker.VZ = there ? 0f : dz / len * c.approachSpeedYdS;
            return there && w.PlayTicks >= Huddle.Ticks(w, c.holdReadyS);
        }

        // exact: the power and aim are kicked as given (a bot, a swipe); else the top of the power sweep can hook it.
        static void Launch(World w, Player kicker, KickKind kind, float power, float aim, bool exact)
        {
            var c = w.Cfg.kicking;
            var b = w.Ball;
            var k = w.Kick;
            power = Clamp(power, 0f, 1f);
            aim = Clamp(aim, -1f, 1f);
            float speed = Speed(w, power);
            float aimDeg = aim * c.maxAimDeg;
            if (!exact && power >= c.shankFromPower) aimDeg += w.Rng.Range(-c.shankAimDeg, c.shankAimDeg);   // too much: it hooks
            float climb = (k.PitchSet ? k.Pitch : Climb(w, kind, power)) * Rad, yaw = aimDeg * Rad;   // (FEATURES.kickArc: his pitch)
            float flat = speed * (float)Math.Cos(climb);
            bool hold = Holder(w) != null;
            b.State = BallState.Flying;
            b.Carrier = -1;
            b.X = hold ? k.HoldX : kicker.X; b.Z = hold ? k.HoldZ : kicker.Z; b.Y = c.kickHeightYd;
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

        // (FEATURES.weather / sandField: a cold, wet or sandy ball flies a little shorter for the same power)
        public static float Speed(World w, float power)
        {
            var c = w.Cfg.kicking;
            return (c.minSpeedYdS + (c.maxSpeedYdS - c.minSpeedYdS) * power) * Weather.KickSpeed(w);
        }

        // The climb (degrees): with FEATURES.kickSweep a field goal's arc rises with its power, else the kind's own.
        public static float Climb(World w, KickKind kind, float power)
        {
            var c = w.Cfg.kicking;
            if (kind != KickKind.FieldGoal || !w.Cfg.Feature("kickSweep")) return Angle(c, kind);
            return c.fgMinAngleDeg + (c.fgMaxAngleDeg - c.fgMinAngleDeg) * Clamp(power, 0f, 1f);
        }

        // From where this field goal is kicked to the posts (yards along the field).
        static float KickDistance(World w)
        {
            var k = w.Kick;
            var kicker = Kicker(w);
            float x = Holder(w) != null || kicker == null ? k.HoldX : kicker.X;
            return Math.Abs(w.Dir * w.Cfg.field.HalfLength - x);
        }

        // The bot kicker: kickoffs and punts at a set power; a field goal just hard enough to clear the bar by
        // botFieldGoalClearYd, aimed at the middle of the posts. A little noise on both. With FEATURES.wind he reads
        // botWindRead of the wind's push (aims into a crosswind, kicks harder into a headwind).
        static void BotKick(World w, Player kicker, KickKind kind)
        {
            var c = w.Cfg.kicking;
            var k = w.Kick;
            float power = kind == KickKind.Kickoff ? c.botKickoffPower : c.botPuntPower;
            float aimDeg = 0f;
            bool hold = Holder(w) != null;
            if (kind == KickKind.FieldGoal)
            {
                float fromZ = hold ? k.HoldZ : kicker.Z;
                float d = KickDistance(w);
                // the wind's push over the flight (from the power he'd kick it with in a calm)
                Wind(w, out float wx, out float wz);
                float p0 = FieldGoalPower(w, d, c.botFieldGoalClearYd);
                float climb = Climb(w, kind, p0) * Rad;
                float t = d / Math.Max(c.minSpeedYdS, Speed(w, p0) * (float)Math.Cos(climb));
                float push = 0.5f * c.windPush * t * t * c.botWindRead;
                power = FieldGoalPower(w, Math.Max(0f, d - wx * w.Dir * push), c.botFieldGoalClearYd) + c.botFieldGoalExtraPower;
                aimDeg = (float)Math.Atan2((fromZ + wz * push) * w.Dir, d) / Rad;
            }
            power += w.Rng.Range(-c.botPowerNoise, c.botPowerNoise);
            aimDeg += w.Rng.Range(-c.botAimNoiseDeg, c.botAimNoiseDeg);
            if (hold) RunUp(w, kicker, power, aimDeg / c.maxAimDeg, true);
            else Launch(w, kicker, kind, power, aimDeg / c.maxAimDeg, true);
        }

        // The power that clears the crossbar by botFieldGoalClearYd at d yards (1 = as hard as he can).
        public static float FieldGoalPower(World w, float d) => FieldGoalPower(w, d, w.Cfg.kicking.botFieldGoalClearYd);

        // The power that clears the crossbar by clearYd at d yards, with no wind (1: even as hard as he can won't).
        // The arc can climb with the power (Climb), so it is found by halving: meterSearchSteps halvings.
        // pitchDeg: the climb (FEATURES.kickArc), or below zero for the climb that goes with the power (Climb).
        public static float FieldGoalPower(World w, float d, float clearYd, float pitchDeg = -1f)
        {
            var c = w.Cfg.kicking;
            float need = c.crossbarYd + clearYd;
            if (Height(w, d, 1f, pitchDeg) < need) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < c.meterSearchSteps; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Height(w, d, mid, pitchDeg) >= need) hi = mid; else lo = mid;
            }
            return hi;
        }

        // How high a field goal kicked at this power is d yards out (no wind; below the grass once it has come down).
        static float Height(World w, float d, float power, float pitchDeg = -1f)
        {
            var c = w.Cfg.kicking;
            float climb = (pitchDeg >= 0f ? pitchDeg : Climb(w, KickKind.FieldGoal, power)) * Rad;
            float v = Speed(w, power), cos = (float)Math.Cos(climb);
            if (v <= 0f || cos <= 0f) return 0f;
            float t = d / (v * cos);
            return c.kickHeightYd + v * (float)Math.Sin(climb) * t - 0.5f * w.Cfg.ball.gravityYdS2 * t * t;
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
                if (kicker == null || LivePlay.Carrier(w) != BallMan(w)) { LivePlay.Whistle(w, "PLAY OVER", w.LineOfScrimmageX); return true; }
                if (k.Approach)
                {
                    if (RunUpStep(w, kicker)) Launch(w, kicker, kind, k.PendingPower, k.PendingAim, k.PendingExact);
                    return true;
                }
                // FEATURES.kickSweep: snapped before the first tap (he ran out of time): the aim stops where it was
                // (FEATURES.kickArc: where he steered it)
                if (TapsOn(w) && !k.AimSet)
                {
                    if (!ArcOn(w)) k.Aim = AimSweep(c, (w.Tick - k.AimTick) / (float)w.Cfg.sim.tickHz);
                    k.AimSet = true;
                    k.AutoSnap = true;
                    k.PowerTick = w.Tick;
                }
                // (a person tapping through the sweep never moves his stick, so being idle only hands it to the bot if
                // the line-up ran out on him: else he has humanKickMaxS for the second tap)
                bool idle = kicker.BotHelp && (!TapsOn(w) || k.AutoSnap);
                bool bot = !kicker.Human || idle || w.PlayTicks >= Huddle.Ticks(w, c.humanKickMaxS);
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
                if (p == kicker || p == Holder(w)) return default(PlayerInput);
                // kickoff: everyone holds his spot until the ball is in the air (no running before the kick)
                if (kind == KickKind.Kickoff) return To(p, p.HomeX, p.HomeZ, false, arrive);
                // a punt's returner stays back; everyone else plays the called jobs (block, rush, cover)
                if (kind == KickKind.Punt && p.Team == w.Defense && p.Spot == w.Cfg.kicking.returnerSpot) return To(p, p.HomeX, p.HomeZ, false, arrive);
                return null;
            }
            if (kind == KickKind.FieldGoal || p == kicker) return default(PlayerInput);
            var b = w.Ball;
            float tx = k.Landed ? b.X : b.TargetX, tz = k.Landed ? b.Z : b.TargetZ;
            if (BotsV3.On(w)) return BotsV3.KickThink(w, p, tx, tz, Returner(w, tx, tz));   // bots v3: lanes, the return wall, real blocks
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

        // The returner right now: the receiving man nearest where a kick in the air comes down (or lies); null when
        // there is no kick in the air, or it has been caught.
        public static Player ReturnerNow(World w)
        {
            if (Mode(w) == KickKind.None || !w.Kick.Kicked || w.Kick.Returned || Mode(w) == KickKind.FieldGoal) return null;
            var b = w.Ball;
            return Returner(w, w.Kick.Landed ? b.X : b.TargetX, w.Kick.Landed ? b.Z : b.TargetZ);
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
        public float Power, Aim;       // the last kick, for the TV meter (Aim: also the first tap's aim, FEATURES.kickSweep)
        // a field goal: the holder's spot (FEATURES.fgHolder); the first tap (FEATURES.kickSweep), when each arrow began
        // to sweep; the run-up to the ball, from where, and the kick it ends in
        public float HoldX, HoldZ;
        public bool AimSet, AutoSnap, Approach, PendingExact;
        public int AimTick, PowerTick, ApproachTick;
        public float StartX, StartZ, PendingPower, PendingAim;
        // FEATURES.kickArc: the first tap's power, the second's climb (degrees), and when the pitch needle began
        public bool PowerSet, PitchSet;
        public float ChosenPower, Pitch;
        public int PitchTick;

        public void NewPlay() { Placed = false; Kicked = false; Landed = false; Returned = false; AimSet = false; AutoSnap = false; Approach = false; PowerSet = false; PitchSet = false; }
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
        // FEATURES.fgHolder: the holder's spot and the kicker's run-up; FEATURES.kickSweep: the two arrows and the arc;
        // FEATURES.wind: the wind (see the top of Kicking)
        public int holderSpot = 1, meterSearchSteps, windEveryHuddles;
        public float holderSideYd, kickerBackYd, kickerSideYd, kickContactYd, approachSpeedYdS, holdReadyS;
        public float aimSweepS, powerSweepS, phoneSyncS, tapSlackS, fgMinAngleDeg, fgMaxAngleDeg, shankFromPower, shankAimDeg;
        public float windMaxYdS, windPush, botWindRead, windMphPerYdS;
        // FEATURES.kickArc: the pitch needle, the arc lined up, the pad's steering, the punt's good-hang shading
        public float pitchSweepS, arcMinPitchDeg, arcMaxPitchDeg, arcWindowStepDeg, arcPreviewPower, arcSteerPerS, puntGoodMinDeg, puntGoodMaxDeg;
        public KickCamConfig cam = new KickCamConfig();
        public float[] puntReturner = new float[0];
        public List<float[]> kickoffKicking = new List<float[]>();
        public List<float[]> kickoffReceiving = new List<float[]>();
    }

    // Config/game.json "kicking.cam" (FEATURES.kickCam, View): for a field goal the TV camera stands backYd behind the
    // holder's spot, sideYd across (plus: the kicker's side, so the holder doesn't hide the ball) and heightM up, and
    // tilts so the picture runs from the kicker's feet at the bottom to the flags on the uprights at the top (lookShare
    // of the way up from the feet: over a half leaves room for the scoreboard); fovDeg wide. It eases there and back at rate.
    public sealed class KickCamConfig
    {
        public float backYd, sideYd, heightM, fovDeg, lookShare, rate, holdAfterS;
    }
}
