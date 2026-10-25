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
    }

    public sealed class Player
    {
        public int Slot, Team, Number;
        public bool Human;
        public float X, Z, VX, VZ;           // yards, yards per second
        // bot v0
        public float GoalX, GoalZ;
        public int NextThinkTick;
    }

    // The authoritative game state. Step() advances one fixed tick (1 / tickHz s). Pure C#: no Unity, no clock,
    // no System.Random, players kept sorted by slot so nothing depends on dictionary order.
    public sealed class World
    {
        public readonly GameConfig Cfg;
        public readonly Rng Rng;
        public readonly List<Player> Players = new List<Player>();
        public int Tick;

        public World(GameConfig cfg, int seed)
        {
            Cfg = cfg;
            Rng = new Rng(seed);
        }

        public float Dt => 1f / Cfg.sim.tickHz;

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

        // Adds a player (or moves an existing one to a new team) at the team's next spawn spot.
        public Player AddPlayer(int slot, int team, bool human)
        {
            var p = Find(slot);
            if (p == null)
            {
                p = new Player { Slot = slot };
                int at = 0;
                while (at < Players.Count && Players[at].Slot < slot) at++;
                Players.Insert(at, p);
            }
            else if (p.Team == team) { p.Human = human; return p; }
            p.Human = human;
            p.Team = team;
            p.Number = NextNumber(team, p);
            Spawn(p);
            return p;
        }

        public void RemovePlayer(int slot)
        {
            var p = Find(slot);
            if (p != null) Players.Remove(p);
        }

        int NextNumber(int team, Player self)
        {
            int n = 1;
            bool taken = true;
            while (taken)
            {
                taken = false;
                foreach (var q in Players) if (q != self && q.Team == team && q.Number == n) { taken = true; n++; break; }
            }
            return n;
        }

        // Spawn spots: each team in a column on its own half, spread across the field.
        void Spawn(Player p)
        {
            var c = Cfg.players;
            int index = p.Number - 1;
            int row = index / c.perTeam, col = index % c.perTeam;
            float side = p.Team == 0 ? -1f : 1f;
            p.X = side * (c.spawnXYd + row * c.spawnRowYd);
            p.Z = (col - (c.perTeam - 1) * 0.5f) * c.spawnSpacingYd;
            p.VX = p.VZ = 0;
            p.GoalX = p.X; p.GoalZ = p.Z;
            p.NextThinkTick = Tick;
        }

        // inputs: by slot, for human players. Bots think for themselves.
        public void Step(IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            var c = Cfg.players;
            float dt = Dt;
            float maxX = Cfg.field.HalfLength + c.boundsMarginYd, maxZ = Cfg.field.HalfWidth + c.boundsMarginYd;
            foreach (var p in Players)
            {
                PlayerInput input = default;
                if (p.Human) { if (inputs != null) inputs.TryGetValue(p.Slot, out input); }
                else input = Bots.Think(this, p);
                float mx = input.MoveX, mz = input.MoveZ;
                float m = (float)Math.Sqrt(mx * mx + mz * mz);
                if (m > 1f) { mx /= m; mz /= m; }
                p.VX = mx * c.walkSpeedYdS;
                p.VZ = mz * c.walkSpeedYdS;
                p.X = Clamp(p.X + p.VX * dt, -maxX, maxX);
                p.Z = Clamp(p.Z + p.VZ * dt, -maxZ, maxZ);
            }
            Tick++;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

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
            Mix(Tick);
            Mix((int)Rng.State);
            foreach (var p in Players)
            {
                Mix(p.Slot); Mix(p.Team); Mix(p.Number); Mix(p.Human ? 1 : 0);
                Mix(Bits(p.X)); Mix(Bits(p.Z)); Mix(Bits(p.VX)); Mix(Bits(p.VZ));
                Mix(Bits(p.GoalX)); Mix(Bits(p.GoalZ)); Mix(p.NextThinkTick);
            }
            return h;
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    }
}
