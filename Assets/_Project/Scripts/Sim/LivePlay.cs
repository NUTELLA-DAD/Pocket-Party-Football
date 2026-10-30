using System;

namespace PocketFootball.Sim
{
    // A live play (M1-c), behind FEATURES.liveplay (NO_LIVEPLAY=1: no hike, the huddle just repeats).
    // The QB hikes; on a run play (play.runner) he hands off when the runner reaches him; else he throws (a person picks
    // the receiver by shape, tap = bullet, hold = lob; a bot QB throws to the most open receiver after botThrowS).
    // A pass coming down near a player may be caught (the offense more often than the defense: an interception);
    // a ball that hits the grass is incomplete. Defenders next to the ball carrier try a tackle: the odds rise with the
    // tackler's speed and from behind; a JUKE makes every try miss for a moment; a miss leaves the tackler stumbling.
    // The whistle blows on a tackle (a sack behind the line), an incompletion, an interception, out of bounds,
    // a touchdown or when the play runs too long; the next play starts where the ball ended up.
    public static class LivePlay
    {
        public static bool On(World w) => w.Cfg.Feature("liveplay");

        public static Player Qb(World w) => SpotOf(w, w.Offense, 0);

        public static Player SpotOf(World w, int team, int spot)
        {
            foreach (var p in w.Players) if (p.Team == team && p.Spot == spot) return p;
            return null;
        }

        public static Player Carrier(World w) => w.Ball.State == BallState.Held ? w.Find(w.Ball.Carrier) : null;

        public static PlayConfig Called(World w, int team) => team >= 0 && team < w.Calls.Length ? Huddle.Find(w, w.Calls[team]) : null;

        // The receiver for shape k (1-based), or null.
        public static Player Receiver(World w, int k)
        {
            var r = w.Cfg.play.receivers;
            return k >= 1 && k <= r.Count ? SpotOf(w, w.Offense, r[k - 1]) : null;
        }

        public static void Hike(World w)
        {
            w.Phase = Phase.Live;
            w.PlayTicks = 0;
            w.Thrown = false;
            w.StickThrow = false;
            w.PassTarget = -1;
            w.Intercepted = false;
            w.Result = "";
            w.CatchTried.Clear();
            foreach (var p in w.Players) { p.RouteIndex = 0; p.TackleCooldown = 0; p.StumbleTicks = 0; p.JukeTicks = 0; p.JukeCooldown = 0; p.BlockedTicks = 0; p.FreeTicks = 0; p.OffBalanceTicks = 0; p.OffBalanceBy = -1; }
            if (BotsV3.On(w)) foreach (var p in w.Players) BotsV3.AtTheSnap(w, p);   // bots v3: the defense has to see the snap
            w.Events.Push(w, EventKind.Hike, Qb(w));
        }

        public static void Whistle(World w, string result, float spotX)
        {
            // an interception return ends like any run, but it is still an interception (or a pick six)
            if (w.Intercepted) result = result == "TOUCHDOWN" ? "PICK SIX" : "INTERCEPTED";
            // at the spot (FEATURES.hitAngle: a big hit is spotted where he was hit, not where he landed)
            var held = Carrier(w);
            if (held != null) w.Events.Push(w, EventKind.Whistle, held.Slot, held.Team, spotX, held.Z, -1, result);
            else w.Events.Push(w, EventKind.Whistle, null, null, result);
            var f = w.Cfg.field;
            float lim = f.GoalLineX - f.lineEveryYd * 0.5f;
            w.Phase = Phase.Whistle;
            w.PhaseTicks = Huddle.Ticks(w, w.Cfg.play.whistleS);
            w.Result = result;
            w.NextLos = result == "TOUCHDOWN" || result == "PICK SIX" ? w.Cfg.formation.lineOfScrimmageXYd : (spotX < -lim ? -lim : spotX > lim ? lim : spotX);
            // the numbers: yards gained this play (a touchdown counts to the goal line)
            float end = result == "TOUCHDOWN" ? f.GoalLineX * w.Dir : w.NextLos;
            w.PlaysRun++;
            w.YardsGained += (int)Math.Round((end - w.LineOfScrimmageX) * w.Dir);
            w.Results[result] = (w.Results.TryGetValue(result, out int n) ? n : 0) + 1;
            Downs.Apply(w, result, spotX);
        }

        // One player's buttons this tick (after it moved).
        public static void Act(World w, Player p, PlayerInput input)
        {
            if (!On(w)) return;
            var c = w.Cfg.play;
            if (p.TackleCooldown > 0) p.TackleCooldown--;
            if (p.StumbleTicks > 0) p.StumbleTicks--;
            if (p.JukeTicks > 0) p.JukeTicks--;
            if (p.JukeCooldown > 0) p.JukeCooldown--;
            if (p.OffBalanceTicks > 0) p.OffBalanceTicks--;   // FEATURES.hitAngle (HitAngle.Step: does he stay up?)

            if (w.Phase == Phase.Set && input.Hike && p == Qb(w)) { Hike(w); return; }
            if (w.Phase != Phase.Live) return;
            var carrier = Carrier(w);
            // the QB's throw
            if (input.Throw > 0 && carrier == p && p == Qb(w) && !w.Thrown)
            {
                var target = Receiver(w, input.Throw);
                ThrowTo(w, target, input.Lob);
                // FEATURES.passTarget: the pass is for the man whose button was pressed, not whoever is nearest where it lands
                if (w.Thrown && target != null && p.Human && w.Cfg.Feature("passTarget")) w.PassTarget = target.Slot;
            }
            else if (input.ThrowAt && carrier == p && p == Qb(w) && !w.Thrown && ThrowStick.On(w)) ThrowStick.Throw(w, p, input.AimX, input.AimZ, input.Arc);
            // the ball carrier's juke: a burst to the side the stick points (or the far side)
            if (input.Juke && carrier == p && p.JukeCooldown == 0)
            {
                p.JukeTicks = c.jukeTicks;
                p.JukeCooldown = c.jukeCooldownTicks;
                float side = input.MoveZ < 0f ? -1f : 1f;
                p.VZ += side * c.jukeBurstYdS;
                w.Events.Push(w, EventKind.Juke, p);
            }
            // a person's tackle
            if (input.Tackle && carrier != null && carrier.Team != p.Team && p.TackleCooldown == 0 && Dist(p, carrier) <= TackleReach(w, p))
                TryTackle(w, p, carrier);
        }

        public static void ThrowTo(World w, Player target, bool lob)
        {
            if (target == null) return;
            var b = w.Cfg.ball;
            var qb = Carrier(w);
            if (qb == null) return;
            // lead the receiver by the flight time
            float dx = target.X - qb.X, dz = target.Z - qb.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            float vy = (float)Math.Sqrt((b.gravityYdS2 + b.gravityYdS2) * b.lobApexYd);
            float t = lob ? (vy + vy) / b.gravityYdS2 : d / b.bulletSpeedYdS;
            if (w.Throw(target.X + target.VX * t, target.Z + target.VZ * t, lob)) { w.Thrown = true; w.Events.Push(w, EventKind.Throw, qb, target); }
        }

        // How far a tackle reaches: a person's TACKLE button reaches half as far again as a bot's automatic try.
        public static float TackleReach(World w, Player p) => w.Cfg.play.tackleReachYd * (p.Human ? 1f + 0.5f : 1f);

        public static float Dist(Player a, Player b) { float dx = a.X - b.X, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }

        static void TryTackle(World w, Player tackler, Player carrier)
        {
            var c = w.Cfg.play;
            tackler.TackleCooldown = c.tackleCooldownTicks;
            if (carrier.JukeTicks > 0) { tackler.StumbleTicks = c.stumbleTicks; w.Events.Push(w, EventKind.MissedTackle, tackler, carrier); return; }   // juked!
            float speed = (float)Math.Sqrt(tackler.VX * tackler.VX + tackler.VZ * tackler.VZ);
            float odds = c.tackleBase + speed * c.tackleSpeedBonus;
            // from behind: the carrier runs away from the tackler
            if ((carrier.X - tackler.X) * carrier.VX + (carrier.Z - tackler.Z) * carrier.VZ > 0f) odds += c.tackleFromBehindBonus;
            odds += Ratings.TackleBonus(w, tackler, carrier);   // FEATURES.ratings: a big man brings a small one down
            odds = HitAngle.TackleOdds(w, carrier, odds);   // FEATURES.hitAngle: easier on a man off balance
            odds = CarrierMoves.TackleOdds(w, tackler, carrier, odds);   // WS-04
            if (w.Rng.Value() < odds)
            {
                if (!HitAngle.Land(w, tackler, carrier, out float fumbleMul, out string how, out float spotX)) return;   // FEATURES.hitAngle: a glancing blow, he stays up
                if (fumbleMul > 0f && Fumbles.OnHit(w, tackler, carrier, fumbleMul)) return;   // WS-04: knocked loose, the play goes on
                bool sack = carrier == Qb(w) && !w.Thrown && (spotX - w.LineOfScrimmageX) * w.Dir < 0f;
                w.Events.Push(w, sack ? EventKind.Sack : EventKind.Tackle, tackler, carrier, how);
                Whistle(w, sack ? "SACKED" : "TACKLED", spotX);
            }
            else { tackler.StumbleTicks = c.stumbleTicks; w.Events.Push(w, EventKind.MissedTackle, tackler, carrier); }
        }

        // After everyone moved and the ball flew: handoffs, bot throws, catches, incompletions, tackles, the lines.
        public static void Step(World w)
        {
            if (!On(w) || w.Phase != Phase.Live) return;
            if (Kicking.LiveStep(w)) return;   // WS-01: a kick in the air (the return runs below)
            var c = w.Cfg.play;
            var f = w.Cfg.field;
            float dir = w.Dir;
            w.PlayTicks++;
            var qb = Qb(w);
            var carrier = Carrier(w);
            var play = Called(w, w.Offense);

            // the handoff on a run play
            if (carrier != null && carrier == qb && play != null && play.runner >= 0 && w.PlayTicks <= Huddle.Ticks(w, c.handoffWindowS))
            {
                var runner = SpotOf(w, w.Offense, play.runner);
                if (runner != null && Dist(runner, qb) <= c.handoffYd) { w.Ball.GiveTo(runner, w.Cfg.ball); carrier = runner; w.Events.Push(w, EventKind.Handoff, runner, qb); }
            }
            // a bot QB (or one helping an idle person) reads his receivers
            if (carrier != null && carrier == qb && (!qb.Human || qb.BotHelp) && !w.Thrown && (play == null || play.runner < 0))
            {
                var target = Bots.QbRead(w, qb, play);
                if (target != null) ThrowTo(w, target, Dist(target, qb) > w.Cfg.bots.wanderYd);
            }

            // catches: the ball coming down within reach
            if (w.Ball.State == BallState.Flying && w.Ball.VY < 0f && w.Ball.Y <= c.catchMaxHeightYd)
            {
                Player near = null; float nd = float.MaxValue;
                // FEATURES.passTarget: the man the QB picked goes up for it ahead of a teammate who drifted closer
                var meant = w.PassTarget >= 0 ? Bots.Intended(w) : null;
                bool hisBall = meant != null && !w.CatchTried.Contains(meant.Slot)
                    && (float)Math.Sqrt((meant.X - w.Ball.X) * (meant.X - w.Ball.X) + (meant.Z - w.Ball.Z) * (meant.Z - w.Ball.Z)) <= c.catchRadiusYd;
                foreach (var p in w.Players)
                {
                    if (w.CatchTried.Contains(p.Slot)) continue;
                    if (hisBall && p.Team == w.Offense && p != meant) continue;
                    float dx = p.X - w.Ball.X, dz = p.Z - w.Ball.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d <= c.catchRadiusYd && d < nd) { near = p; nd = d; }
                }
                // FEATURES.catchPlay: nobody in the circle, a man running at a low ball can lay out for it
                bool dive = false;
                if (near == null && CatchPlay.On(w)) { near = CatchPlay.Diver(w, hisBall, meant); dive = near != null; }
                if (near != null)
                {
                    w.CatchTried.Add(near.Slot);
                    bool offense = near.Team == w.Offense;
                    var rival = CatchPlay.On(w) ? CatchPlay.Rival(w, near) : null;   // FEATURES.catchPlay: a jump ball
                    // FEATURES.tightCoverage: the man on him may pick it off or knock it down first
                    var tight = TightCoverage.Resolve(w, near, rival);
                    if (tight == TightCoverage.Result.Picked && w.Phase != Phase.Live) return;
                    if (tight == TightCoverage.Result.None)
                    {
                        float chance = Receiving.Chance(w, near, Weather.Catch(w, Ratings.Catch(w, near, offense ? c.catchChanceOffense : c.catchChanceDefense)));   // WS-05: CATCH timing; FEATURES.weather: a wet ball
                        if (CatchPlay.On(w)) chance = CatchPlay.Chance(w, chance, dive, rival != null);
                        bool caught = w.Rng.Value() < chance;
                        if (CatchPlay.On(w)) CatchPlay.After(w, near, rival, dive, caught);
                        if (caught)
                        {
                            // FEATURES.oobIncomplete: a catch with his feet out of bounds is no catch (no gain, no first
                            // down, no interception): incomplete, the ball falls on past him and goes back to the line
                            if (near.OutOfBounds && w.Cfg.Feature("oobIncomplete"))
                            {
                                w.Events.Push(w, EventKind.Incomplete, qb != null ? qb.Slot : -1, w.Offense, w.Ball.X, w.Ball.Z);
                                Whistle(w, "INCOMPLETE", w.LineOfScrimmageX);
                                return;
                            }
                            w.Ball.GiveTo(near, w.Cfg.ball);
                            w.Events.Push(w, offense ? EventKind.Catch : EventKind.Interception, near, qb);
                            if (!offense)
                            {
                                // FEATURES.intreturn: he runs it back until the whistle; off: the play ends at the catch
                                if (!w.Cfg.Feature("intreturn")) { Whistle(w, "INTERCEPTED", near.X); return; }
                                w.Intercepted = true;
                            }
                        }
                    }
                }
            }
            if (Fumbles.Step(w)) return;   // WS-04: a fumble on the grass is not an incompletion
            if (w.Ball.State == BallState.Loose)
            {
                w.Events.Push(w, EventKind.Incomplete, qb != null ? qb.Slot : -1, w.Offense, w.Ball.X, w.Ball.Z);
                Whistle(w, "INCOMPLETE", w.LineOfScrimmageX);
                return;
            }

            carrier = Carrier(w);
            if (carrier != null)
            {
                bool td = carrier.Team == w.Offense ? carrier.X * dir > f.GoalLineX : carrier.X * -dir > f.GoalLineX;   // (the defense: a pick six)
                if (td) { w.Events.Push(w, EventKind.Touchdown, carrier); Whistle(w, "TOUCHDOWN", carrier.X); return; }
                if (carrier.OutOfBounds) { w.Events.Push(w, EventKind.OutOfBounds, carrier); Whistle(w, "OUT OF BOUNDS", carrier.X); return; }
                if (HitAngle.Step(w, carrier)) return;   // FEATURES.hitAngle: off balance, and down he goes
                // bot defenders next to the carrier try a tackle
                foreach (var p in w.Players)
                {
                    if (p.Human || p.Team == carrier.Team || p.TackleCooldown > 0) continue;
                    if (Dist(p, carrier) <= c.tackleReachYd) { TryTackle(w, p, carrier); if (w.Phase != Phase.Live) return; }
                }
            }
            if (w.PlayTicks >= Huddle.Ticks(w, c.maxPlayS)) Whistle(w, "PLAY OVER", carrier != null ? carrier.X : w.LineOfScrimmageX);
        }
    }
}
