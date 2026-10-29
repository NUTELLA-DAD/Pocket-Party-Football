using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace PocketFootball.Sim
{
    // Config/lab.json: the practice lab's drills (a test bench, not a rule of the game, so no FEATURES switch).
    public sealed class LabConfig
    {
        public int keyboardSlot, botSlotBase, offenseTeam, logLines;
        public float attackDir, lineOfScrimmageXYd, idleTakeoverS, endAfterCatchS;
        public List<float> speeds = new List<float>();
        public List<LabDrill> drills = new List<LabDrill>();
    }

    // One drill: which formation spots are on the field (bots, except yours), the plays both sides "called", where
    // the ball starts, whether the play is already live (else you hike), and when it ends.
    // throwAtS >= 0: the lab's QB throws to receiver throwTo at that moment, cycling throwKinds (bullet | lob).
    public sealed class LabDrill
    {
        public string id = "", name = "", help = "", note = "", offensePlay = "", defensePlay = "";
        public string you = "offense";           // offense | defense | none (bots only)
        public int youSpot, carrier;
        public List<int> offense = new List<int>();
        public List<int> defense = new List<int>();
        public bool live, endOnCatch;
        public float maxS;                       // 0 = the game's own maxPlayS
        public float throwAtS = -1;
        public int throwTo = 1;
        public List<string> throwKinds = new List<string>();
        // the pursuit drills: the carrier starts out in the open at [yards past the line, z], already running carrierRunYdS
        // down the field (empty: at his formation spot, standing)
        public List<float> carrierAt = new List<float>();
        public float carrierRunYdS;
    }

    // The practice lab: sets up one drill on a World and runs it again after every whistle. Uses only the game's own
    // rules (LivePlay, Bots): the lab places players, gives the ball, and for some drills throws for the QB.
    // It also keeps a plain-words log of what happened (throws, catches, drops, tackle tries with their odds, jukes).
    public sealed class Lab
    {
        public readonly World W;
        public readonly LabConfig C;
        public int DrillIndex { get; private set; }
        public LabDrill Drill => C.drills[DrillIndex];
        public int YouSlot;                      // who "you" is: the keyboard, or the phone playing the lab
        public int You { get; private set; } = -1;   // your slot in this drill (-1: watching)
        public int Rep { get; private set; }     // how many times this drill has run since it was picked
        public int Starts { get; private set; }  // counts every setup (the host re-syncs a phone after each)
        public readonly List<string> Log = new List<string>();

        // the pass in the air, for its log line when it comes down
        int passer = -1, passTick;
        float passFromX, passFromZ;
        bool passLob, passOpen;
        int caughtTick = -1;
        readonly HashSet<int> triesSeen = new HashSet<int>();
        BallState lastBall;
        int lastCarrier = -1;
        float lastBallX, lastBallZ;
        Phase lastPhase;

        public static LabConfig Parse(string json) => JsonConvert.DeserializeObject<LabConfig>(json) ?? new LabConfig();

        // The lab's own tuning on top of game.json: a person standing still is never taken over by a bot.
        public Lab(World w, LabConfig c)
        {
            W = w;
            C = c;
            YouSlot = c.keyboardSlot;
            w.Cfg.bots.idleTakeoverS = c.idleTakeoverS;
        }

        public void Pick(int index)
        {
            int n = C.drills.Count;
            DrillIndex = ((index % n) + n) % n;
            Rep = 0;
            Log.Clear();
            Setup();
        }

        public void Next(int step) => Pick(DrillIndex + step);

        public void Again() { Rep = 0; Setup(); }

        public int FindDrill(string id)
        {
            for (int i = 0; i < C.drills.Count; i++) if (C.drills[i].id == id) return i;
            return -1;
        }

        // Clears the field and lines the drill up, the ball given out; live drills start at the snap.
        void Setup()
        {
            var w = W;
            var d = Drill;
            var gone = new List<int>();
            foreach (var p in w.Players) gone.Add(p.Slot);
            foreach (int s in gone) w.RemovePlayer(s);
            w.Ball.Reset();
            w.Offense = C.offenseTeam;
            w.Dir = C.attackDir;
            w.LineOfScrimmageX = C.lineOfScrimmageXYd;
            w.NextLos = w.LineOfScrimmageX;
            w.Down = 1;
            w.FirstDownX = w.LineOfScrimmageX + w.Dir * w.Cfg.downs.firstDownYd;
            w.PendingTurnover = false;
            w.Note = "";
            w.Result = "";
            w.Thrown = false;
            w.StickThrow = false;
            w.PassTarget = -1;
            w.Intercepted = false;
            w.Suggestions.Clear();
            w.CatchTried.Clear();
            for (int t = 0; t < w.Calls.Length; t++) w.Calls[t] = t == w.Offense ? d.offensePlay : d.defensePlay;
            w.HuddleNumber++;
            // no kickoff in the lab: the game's opening kickoff (Kicking.Step, at the first huddle) would take over the
            // first rep of the drill, move everyone and end it
            w.Kick.Opened = true; w.Kick.PendingKickoff = false; w.Kick.Kickoff = false; w.Kick.Try = false;
            w.Kick.NewPlay();

            You = d.you == "none" ? -1 : YouSlot;
            int bot = C.botSlotBase;
            Place(w.Offense, d.offense, d.you == "offense", d.youSpot, ref bot);
            Place(w.Defense, d.defense, d.you == "defense", d.youSpot, ref bot);
            w.LineUp();
            var holder = LivePlay.SpotOf(w, w.Offense, d.carrier);
            if (holder != null && d.carrierAt.Count > 1)   // [x, z]
            {
                holder.X = holder.HomeX = w.LineOfScrimmageX + w.Dir * d.carrierAt[0];
                holder.Z = holder.HomeZ = d.carrierAt[1];
                holder.VX = w.Dir * d.carrierRunYdS; holder.VZ = 0f;
            }
            if (holder != null) w.Ball.GiveTo(holder, w.Cfg.ball);

            if (d.live) LivePlay.Hike(w);
            else
            {
                w.Phase = Phase.Set;
                var qb = LivePlay.Qb(w);
                w.PhaseTicks = Huddle.Ticks(w, qb != null && qb.Human ? w.Cfg.play.humanHikeMaxS : w.Cfg.play.botHikeS);
            }
            passer = -1;
            caughtTick = -1;
            triesSeen.Clear();
            Remember();
            Starts++;
        }

        void Place(int team, List<int> spots, bool yours, int youSpot, ref int bot)
        {
            foreach (int spot in spots)
            {
                bool me = yours && spot == youSpot;
                W.AddPlayer(me ? YouSlot : bot++, team, me, spot);
            }
        }

        // After every World.Step: the lab's QB throw, a catch ending the drill, the drill's clock, the log, and after
        // the whistle (the game moves on to its next huddle) the same drill again.
        public void AfterTick()
        {
            var w = W;
            var d = Drill;
            // (with the huddle switched off, NO_HUDDLE=1, nothing ends the whistle: the lab counts it down itself)
            bool over = w.Phase == Phase.Huddle || (w.Phase == Phase.Whistle && !Huddle.On(w) && --w.PhaseTicks <= 0);
            if (over) { Rep++; Setup(); return; }
            Watch();
            Remember();
            if (w.Phase != Phase.Live) return;
            if (d.throwAtS >= 0f && !w.Thrown && w.PlayTicks == Huddle.Ticks(w, d.throwAtS))
                LivePlay.ThrowTo(w, LivePlay.Receiver(w, d.throwTo), ThrowKind() == "lob");
            if (d.endOnCatch && caughtTick >= 0 && w.Tick - caughtTick >= Huddle.Ticks(w, C.endAfterCatchS))
                LivePlay.Whistle(w, "CAUGHT", BallX());
            else if (d.maxS > 0f && w.PlayTicks >= Huddle.Ticks(w, d.maxS))
                LivePlay.Whistle(w, "DRILL OVER", BallX());
        }

        // This rep's throw (catching drill): throwKinds in turn.
        public string ThrowKind() => Drill.throwKinds.Count == 0 ? "bullet" : Drill.throwKinds[Rep % Drill.throwKinds.Count];

        float BallX() { var k = LivePlay.Carrier(W); return k != null ? k.X : W.Ball.X; }

        void Remember()
        {
            lastBall = W.Ball.State;
            lastCarrier = W.Ball.Carrier;
            lastBallX = W.Ball.X;
            lastBallZ = W.Ball.Z;
            lastPhase = W.Phase;
        }

        // ---- the log ----
        void Watch()
        {
            var w = W;
            var b = w.Ball;
            var c = w.Cfg.play;

            // a throw: it left a hand this tick
            if (b.State == BallState.Flying && lastBall == BallState.Held)
            {
                passer = lastCarrier;
                passFromX = lastBallX;
                passFromZ = lastBallZ;
                passTick = w.Tick;
                passOpen = true;
                triesSeen.Clear();
                // a lob leaves upward at exactly the speed that reaches lobApexYd (Ball.Launch); it has flown one tick
                var bc = w.Cfg.ball;
                float vyLob = (float)Math.Sqrt((bc.gravityYdS2 + bc.gravityYdS2) * bc.lobApexYd) - bc.gravityYdS2 * w.Dt;
                passLob = Math.Abs(b.VY - vyLob) < 0.5f;
                float yd = Len(b.TargetX - passFromX, b.TargetZ - passFromZ);
                Add((passLob ? "Lob" : "Bullet") + " by " + Who(w.Find(passer)) + ", aimed " + F1(yd) + " yd");
            }
            // catch tries on this pass
            if (passOpen)
                foreach (int slot in w.CatchTried)
                {
                    if (triesSeen.Contains(slot)) continue;
                    triesSeen.Add(slot);
                    var p = w.Find(slot);
                    bool offense = p != null && p.Team == w.Offense;
                    float chance = Ratings.Catch(w, p, offense ? c.catchChanceOffense : c.catchChanceDefense);
                    string air = F1((w.Tick - passTick) / (float)w.Cfg.sim.tickHz) + " s in the air";
                    if (b.State == BallState.Held && b.Carrier == slot)
                    {
                        Add((offense ? "  CAUGHT by " : "  INTERCEPTED by ") + Who(p) + ", " + air + " (" + Pct(chance) + " chance)");
                        if (offense) caughtTick = w.Tick;
                        passOpen = false;
                    }
                    else Add("  " + Who(p) + " got a hand on it and dropped it (" + Pct(chance) + " chance)");
                }
            // it hit the grass
            if (passOpen && b.State == BallState.Loose && lastBall == BallState.Flying)
            {
                Player near = null; float nd = float.MaxValue;
                foreach (var p in w.Players)
                {
                    if (p.Slot == passer) continue;
                    float dd = Len(p.X - b.X, p.Z - b.Z);
                    if (dd < nd) { nd = dd; near = p; }
                }
                string miss = near != null ? ": " + Who(near) + " was " + F1(nd) + " yd away (catch circle " + F1(c.catchRadiusYd) + " yd)" : "";
                Add("  Hit the grass" + miss);
                passOpen = false;
            }

            // tackle tries (a try sets the tackler's cooldown to the full count this tick) and jukes
            var held = LivePlay.Carrier(w);
            if (held != null)
            {
                foreach (var p in w.Players)
                {
                    if (p.TackleCooldown != c.tackleCooldownTicks || p.Team == held.Team) continue;
                    float odds = TackleOdds(w, p, held);
                    string at = F1(LivePlay.Dist(p, held)) + " yd";
                    if (w.Phase == Phase.Whistle && (w.Result == "TACKLED" || w.Result == "SACKED"))
                        Add(w.Result + " by " + Who(p) + " from " + at + " (" + Pct(odds) + " odds)");
                    else if (held.JukeTicks > 0) Add(Who(p) + " dove in from " + at + " and got juked");
                    else Add(Who(p) + " missed a tackle from " + at + " (" + Pct(odds) + " odds)");
                }
                if (held.JukeTicks == c.jukeTicks) Add("Juke by " + Who(held));
            }
            if (w.Phase == Phase.Whistle && lastPhase == Phase.Live) Add("Whistle: " + w.Result);
        }

        // The tackle odds LivePlay.TryTackle uses (kept the same: the base, the tackler's speed, from behind, the
        // carrier's move).
        public static float TackleOdds(World w, Player tackler, Player carrier)
        {
            var c = w.Cfg.play;
            float speed = Len(tackler.VX, tackler.VZ);
            float odds = c.tackleBase + speed * c.tackleSpeedBonus;
            if ((carrier.X - tackler.X) * carrier.VX + (carrier.Z - tackler.Z) * carrier.VZ > 0f) odds += c.tackleFromBehindBonus;
            odds = HitAngle.TackleOdds(w, carrier, odds);   // FEATURES.hitAngle: off balance
            odds = CarrierMoves.TackleOdds(w, tackler, carrier, odds);   // WS-04: spin, stiff arm, dive
            return odds < 0f ? 0f : odds > 1f ? 1f : odds;
        }

        // How far a tackle reaches (the game's own rule, LivePlay.TackleReach).
        public static float TackleReach(World w, Player p) => LivePlay.TackleReach(w, p);

        // The nearest defender to the ball carrier (null: nobody has the ball).
        public Player NearestTackler()
        {
            var k = LivePlay.Carrier(W);
            if (k == null) return null;
            Player best = null; float bd = float.MaxValue;
            foreach (var p in W.Players)
            {
                if (p.Team == k.Team) continue;
                float d = LivePlay.Dist(p, k);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        public string Who(Player p) => p == null ? "?" : p.Pos + " #" + p.Number + (p.Slot == You ? " (you)" : "");

        void Add(string line)
        {
            Log.Insert(0, line);
            while (Log.Count > C.logLines && Log.Count > 0) Log.RemoveAt(Log.Count - 1);
        }

        static float Len(float x, float z) => (float)Math.Sqrt(x * x + z * z);
        public static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        public static string Pct(float v) => v.ToString("0%", CultureInfo.InvariantCulture);
    }
}
