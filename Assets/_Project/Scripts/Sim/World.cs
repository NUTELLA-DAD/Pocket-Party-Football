using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // One player's controls for one tick, in field terms: MoveX along the field (+X = east end zone), MoveZ across it
    // (+Z = away from the camera).
    public struct PlayerInput
    {
        public float MoveX, MoveZ;
        public bool Sprint, Find;
        public bool Hike, Juke, Tackle, Block;
        public int Throw;        // 0 = no throw; k = throw to receiver k (1-based, the order of play.receivers / shapes)
        public bool Lob;         // with Throw: high and soft, else a bullet
        public bool Call, Catch; // WS-05: CALL FOR IT, CATCH (Receiving.cs)
        public CarrierMove Move; // WS-04: the ball carrier's SPIN / STIFF ARM / DIVE (CarrierMoves.cs)
        public DefMove Def;      // WS-03: a defender's SWIM / BULL RUSH / SWAT / JUMP / DIVE TACKLE (DefenseMoves.cs)
        // The throw stick (ThrowStick.cs): throw to a spot. AimX, AimZ: the stick in the MoveX / MoveZ frame (-1..1),
        // scaled by play.throwMaxYards from the QB. Arc: 0 = flat bullet .. 1 = high lob.
        public bool ThrowAt;
        public float AimX, AimZ, Arc;
    }

    public sealed class Player
    {
        public int Slot, Team, Spot, Number;
        public string Pos = "";
        public bool Human;
        public float X, Z, VX, VZ;           // yards, yards per second
        public float HomeX, HomeZ;           // the formation spot
        public float Stamina = 1f;           // 1 = full
        public bool Tired;                   // emptied the tank: no sprint until staminaRestart
        public bool Sprinting;
        public bool OutOfBounds;
        // a live play
        public int TackleCooldown, StumbleTicks, JukeTicks, JukeCooldown, RouteIndex;
        public int OffBalanceTicks, OffBalanceBy = -1;     // FEATURES.hitAngle: staggering from a glancing blow, and who gave it (HitAngle.cs)
        public int BlockedTicks, FreeTicks, IdleTicks;   // engaged by a blocker / shed the block / a person not touching the phone
        public bool BotHelp;                              // a bot is playing for an idle person this tick
        public bool Blocking;                             // this player is blocking this tick (a person holding BLOCK, or a bot)
        public int OffenseSpot = -1, DefenseSpot = -1;    // WS-07: a person's two-way picks (Formation.cs, FEATURES.lobby; -1 = any free spot)
        // bot v0
        public float GoalX, GoalZ;
        public int NextThinkTick;
        // bots v3 (BotsV3.cs, FEATURES.botsV3): what he last took in, when his next reaction lands, what he keeps doing
        // meanwhile, the man he is blocking, and how quick he is (0..1, drawn once)
        public int SeenSituation = -1, SeenCarrier = -1, ReactTick = -1, BlockSlot = -1;
        public float HeldX, HeldZ, Quick;
        public bool HeldSprint, HeldBlock;
    }

    // The authoritative game state. Step() advances one fixed tick (1 / tickHz s). Pure C#: no Unity, no clock,
    // no System.Random, players kept sorted by slot so nothing depends on dictionary order.
    public sealed class World
    {
        public readonly GameConfig Cfg;
        public readonly Rng Rng;
        public readonly int Seed;                      // the game's seed (FEATURES.wind draws the wind from it, not from Rng)
        public readonly List<Player> Players = new List<Player>();
        public readonly Ball Ball = new Ball();
        public int Tick;
        public int Offense;                  // the team with the ball
        public float LineOfScrimmageX;
        public bool BotsWander;
        public float Dir;                              // the way the offense attacks: +1 = toward +X (flips on a change of possession)
        // downs and the score (Downs.cs)
        public int Down = 1;
        public float FirstDownX;
        public readonly int[] Score;
        public string Note = "";                       // after the whistle: FIRST DOWN, TURNOVER ON DOWNS, ...
        public bool PendingTurnover;
        // the huddle (Huddle.cs): starts on the first tick
        public Phase Phase = Phase.Set;
        public int PhaseTicks;
        public int HuddleNumber;                                                             // counts huddles (a new one can follow a huddle)
        public readonly string[] Calls;                                                      // by team: the locked play id
        public readonly SortedDictionary<int, string> Suggestions = new SortedDictionary<int, string>();   // slot -> play id
        // the live play (LivePlay.cs)
        public int PlayTicks;
        public string Result = "";                     // why the whistle blew: TACKLED, SACKED, INCOMPLETE, TOUCHDOWN, ...
        public float NextLos;                          // where the next play starts (yards)
        public bool Thrown;                            // the ball has been thrown this play
        public bool StickThrow;                        // ...with the throw stick, to a spot (ThrowStick.cs)
        public int PassTarget = -1;                    // ...with a receiver button: his slot (FEATURES.passTarget, Bots.Intended)
        public bool Intercepted;                       // a defender caught it and is running it back (FEATURES.intreturn)
        public readonly HashSet<int> CatchTried = new HashSet<int>();
        public readonly ReceivingState Recv = new ReceivingState();   // WS-05: this play's calls and catch taps (Receiving.cs); not hashed
        // play-by-play numbers, for the bots' balance check (M1-d) and later the stats
        public int PlaysRun, YardsGained;
        public readonly SortedDictionary<string, int> Results = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly EventLog Events = new EventLog();   // WS-00: what happened (Events.cs); not hashed, the sim never reads it
        public readonly ClockState GameClock = new ClockState();   // WS-02: the game clock and quarters (Clock.cs)
        public readonly CarrierState Carry = new CarrierState();   // WS-04: carrier moves and fumbles (CarrierMoves.cs, Fumbles.cs)
        public readonly KickState Kick = new KickState();   // WS-01: kickoffs, punts, field goals, tries (Kicking.cs)
        public readonly HitState Hit = new HitState();   // FEATURES.hitAngle: the last hit, for the TV (HitAngle.cs); not hashed
        public readonly AudibleState Audible = new AudibleState();   // WS-13: audibles and hot routes this down (Audibles.cs)
        public readonly BotsV3.Plan BotPlan = new BotsV3.Plan();   // bots v3: this tick's blocking plan (BotsV3.cs); not hashed, rebuilt every tick
        public readonly Pursuit.Plan ChasePlan = new Pursuit.Plan();   // FEATURES.pursuitLanes: this tick's chase (Pursuit.cs); not hashed, rebuilt every tick

        public World(GameConfig cfg, int seed)
        {
            Cfg = cfg;
            Rng = new Rng(seed);
            Seed = seed;
            Offense = cfg.formation.offenseTeam;
            LineOfScrimmageX = cfg.formation.lineOfScrimmageXYd;
            BotsWander = cfg.bots.wander;
            Dir = cfg.formation.attackDir;
            FirstDownX = LineOfScrimmageX + Dir * cfg.downs.firstDownYd;
            Score = new int[cfg.teams.Count];
            Calls = new string[cfg.teams.Count];
        }

        public float Dt => 1f / Cfg.sim.tickHz;
        public int Defense => Offense == 0 ? 1 : 0;

        public Player Find(int slot)
        {
            foreach (var p in Players) if (p.Slot == slot) return p;
            return null;
        }

        public int TeamCount(int team)
        {
            int n = 0;
            foreach (var p in Players) if (p.Team == team) n++;
            return n;
        }

        public List<SpotConfig> Spots(int team) => team == Offense ? Cfg.formation.offense : Cfg.formation.defense;

        // Adds a player on the team's first free formation spot (spot >= 0: that one). Null when the team is full.
        // A player already in the world on another team moves over.
        public Player AddPlayer(int slot, int team, bool human, int spot = -1)
        {
            var p = Find(slot);
            if (p != null && p.Team == team) { p.Human = human; return p; }
            if (spot < 0) spot = FreeSpot(team);
            if (spot < 0) return null;
            if (p == null)
            {
                p = new Player { Slot = slot };
                int at = 0;
                while (at < Players.Count && Players[at].Slot < slot) at++;
                Players.Insert(at, p);
                if (Cfg.Feature("botsV3")) p.Quick = Rng.Value();   // bots v3: how quick he is to react
            }
            p.Human = human;
            p.Team = team;
            p.Spot = spot;
            Formation.Place(this, p);
            p.X = p.HomeX; p.Z = p.HomeZ;
            p.VX = p.VZ = 0;
            p.GoalX = p.X; p.GoalZ = p.Z;
            p.NextThinkTick = Tick;
            if (Ball.State == BallState.Held && Ball.Carrier < 0 && team == Offense && spot == 0) Ball.GiveTo(p, Cfg.ball);
            return p;
        }

        int FreeSpot(int team)
        {
            int n = Spots(team).Count;
            for (int s = 0; s < n; s++)
            {
                bool taken = false;
                foreach (var q in Players) if (q.Team == team && q.Spot == s) { taken = true; break; }
                if (!taken) return s;
            }
            return -1;
        }

        // A person takes a bot's place on a team: the bot with the lowest spot leaves, the person stands where it stood.
        // Null when the team has no bots left.
        public Player ReplaceBot(int team, int humanSlot)
        {
            Player bot = null;
            foreach (var q in Players) if (q.Team == team && !q.Human && (bot == null || q.Spot < bot.Spot)) bot = q;
            if (bot == null) return null;
            float x = bot.X, z = bot.Z;
            bool hadBall = Ball.Carrier == bot.Slot;
            RemovePlayer(bot.Slot);
            var old = Find(humanSlot);
            if (old != null) RemovePlayer(humanSlot);
            var p = AddPlayer(humanSlot, team, true, bot.Spot);
            p.X = x; p.Z = z;
            if (hadBall) Ball.GiveTo(p, Cfg.ball);
            return p;
        }

        public void RemovePlayer(int slot)
        {
            var p = Find(slot);
            if (p == null) return;
            Players.Remove(p);
            if (Ball.Carrier == slot) Ball.Drop();
        }

        // Everyone back to their formation spot, the ball to the QB (spot 0 on offense).
        public void LineUp()
        {
            foreach (var p in Players)
            {
                Formation.Place(this, p);
                p.X = p.HomeX; p.Z = p.HomeZ; p.VX = p.VZ = 0;
                p.GoalX = p.X; p.GoalZ = p.Z; p.NextThinkTick = Tick;
            }
            Ball.Reset();
            foreach (var p in Players) if (p.Team == Offense && p.Spot == 0) Ball.GiveTo(p, Cfg.ball);
        }

        // Throws from the carrier to a spot on the field: bullet (flat and fast) or lob (high, apex lobApexYd).
        public bool Throw(float targetX, float targetZ, bool lob)
        {
            if (Ball.State != BallState.Held || Ball.Carrier < 0) return false;
            Ball.Launch(targetX, targetZ, lob, Cfg.ball);
            return true;
        }

        // inputs: by slot, for human players. Bots think for themselves.
        public void Step(IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            float dt = Dt;
            Events.BeginTick();   // WS-00
            foreach (var p in Players)
            {
                PlayerInput input = default;
                p.BotHelp = false;
                if (p.Human)
                {
                    if (inputs != null) inputs.TryGetValue(p.Slot, out input);
                    bool idle = input.MoveX == 0f && input.MoveZ == 0f && !input.Sprint && !input.Hike && !input.Juke && !input.Tackle && !input.Block && input.Throw == 0 && !input.ThrowAt
                        && input.Def == DefMove.None && !input.Catch && !input.Call && input.Move == CarrierMove.None;   // (a move button is a touch too)
                    p.IdleTicks = idle ? p.IdleTicks + 1 : 0;
                    // a person who has put the phone down: a bot plays for them until they touch it again
                    if (Phase == Phase.Live && p.IdleTicks >= Cfg.bots.idleTakeoverS * Cfg.sim.tickHz) { input = Bots.Think(this, p); p.BotHelp = true; }
                }
                else input = Bots.Think(this, p);
                p.Blocking = input.Block;
                Mover.Move(this, p, input, dt);
                DefenseMoves.Act(this, p, ref input);   // WS-03: before LivePlay.Act, so a DIVE TACKLE isn't also a plain tackle
                LivePlay.Act(this, p, input);
                CarrierMoves.Act(this, p, input);   // WS-04
            }
            Mover.Bump(this);
            Blocking.Step(this);
            Mover.Bounds(this);
            Mover.Onside(this);   // FEATURES.stayOnside: no person past the line before the snap
            Ball.Step(this, dt);
            DefenseMoves.Step(this);   // WS-03 defender moves (FEATURES.defmoves): before LivePlay.Step, so a swat beats the catch
            LivePlay.Step(this);
            Huddle.Step(this);
            Receiving.Step(this, inputs);   // WS-05
            Clock.Step(this);   // WS-02
            Kicking.Step(this);   // WS-01
            Tick++;
        }

        // FNV-1a over everything that matters. Same inputs, same hash, every tick.
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;   // hygiene:allow (FNV offset basis)
            void Mix(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; }   // hygiene:allow (4 bytes; FNV prime)
                }
            }
            void MixF(float f) => Mix(BitConverter.SingleToInt32Bits(f));
            void MixS(string str) { if (str == null) { Mix(-1); return; } Mix(str.Length); foreach (char ch in str) Mix(ch); }
            Mix(Tick);
            Mix((int)Rng.State);
            Mix(Offense);
            Mix((int)Phase); Mix(PhaseTicks); Mix(HuddleNumber);
            MixF(Dir); Mix(Down); MixF(FirstDownX); foreach (int sc in Score) Mix(sc); MixS(Note); Mix(PendingTurnover ? 1 : 0);
            Mix(PlayTicks); MixS(Result); MixF(NextLos); MixF(LineOfScrimmageX); Mix(Thrown ? 1 : 0); Mix(Intercepted ? 1 : 0); Mix(PlaysRun); Mix(YardsGained);
            foreach (var c in Calls) MixS(c);
            foreach (var kv in Suggestions) { Mix(kv.Key); MixS(kv.Value); }
            foreach (var p in Players)
            {
                Mix(p.Slot); Mix(p.Team); Mix(p.Spot); Mix(p.Number); Mix(p.Human ? 1 : 0);
                MixF(p.X); MixF(p.Z); MixF(p.VX); MixF(p.VZ); MixF(p.Stamina);
                Mix(p.Tired ? 1 : 0); Mix(p.OutOfBounds ? 1 : 0);
                Mix(p.TackleCooldown); Mix(p.StumbleTicks); Mix(p.JukeTicks); Mix(p.JukeCooldown); Mix(p.RouteIndex);
                Mix(p.BlockedTicks); Mix(p.FreeTicks); Mix(p.IdleTicks);
                if (p.OffBalanceTicks > 0 || p.OffBalanceBy >= 0) { Mix(p.OffBalanceTicks); Mix(p.OffBalanceBy); }   // (only while it runs, so games without it hash as before)
                MixF(p.GoalX); MixF(p.GoalZ); Mix(p.NextThinkTick);
                if (Cfg.Feature("botsV3")) { Mix(p.SeenSituation); Mix(p.SeenCarrier); Mix(p.ReactTick); Mix(p.BlockSlot); MixF(p.HeldX); MixF(p.HeldZ); Mix(p.HeldSprint ? 1 : 0); Mix(p.HeldBlock ? 1 : 0); MixF(p.Quick); }
            }
            Mix((int)Ball.State); Mix(Ball.Carrier);
            MixF(Ball.X); MixF(Ball.Y); MixF(Ball.Z); MixF(Ball.VX); MixF(Ball.VY); MixF(Ball.VZ);
            return h;
        }
    }
}
