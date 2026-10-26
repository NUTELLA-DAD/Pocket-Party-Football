using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // WS-05 receiving, behind FEATURES.receiving (NO_RECEIVING=1 turns it off). Numbers in game.json "receiving".
    // CALL FOR IT: a receiver (an offense spot in play.receivers) taps it while the QB still has the ball, and his
    // shape pulses for callPulseS: on the TV, and on his own team's QB phone only (the other team's phones are never
    // told). He can call again callCooldownS after the last call.
    // CATCH: an offense player taps it as the ball comes in. A tap no more than catchWindowS before his catch try adds
    // catchWindowBonus to the catch chance (never above catchChanceMax); a tap earlier than that, but within
    // catchForgetS, is a jump too soon and takes catchEarlyPenalty off. Taps while one still counts are ignored, so
    // tapping over and over is a jump too soon. No tap (or an older one): the plain play.catchChanceOffense.
    // Bots never tap, so bot games play exactly as before, switch on or off.
    // Presses are read at the end of World.Step, so a tap counts from the next tick on (one tick, 1/30 s).
    public sealed class ReceivingConfig
    {
        public float callPulseS, callCooldownS, catchWindowS, catchForgetS, catchWindowBonus, catchEarlyPenalty, catchChanceMax;
    }

    // This play's calls and taps (cleared when no play is live). Not in World.Hash: it only acts through the catch
    // roll, whose outcome is hashed.
    public sealed class ReceivingState
    {
        public readonly SortedDictionary<int, int> CallTick = new SortedDictionary<int, int>();    // slot -> tick of the last CALL FOR IT
        public readonly SortedDictionary<int, int> CatchTick = new SortedDictionary<int, int>();   // slot -> tick of the last CATCH tap
        public int Calls;                                                                           // calls this game (the host sends the new ones)
    }

    public static class Receiving
    {
        public static bool On(World w) => w.Cfg.Feature("receiving");

        // The receiver's shape number (1-based, the order of play.receivers / shapes), or 0 when he isn't one.
        public static int ShapeOf(World w, Player p)
        {
            if (p == null || p.Team != w.Offense) return 0;
            return w.Cfg.play.receivers.IndexOf(p.Spot) + 1;
        }

        // The end of World.Step: this tick's CALL FOR IT and CATCH taps from people's phones.
        public static void Step(World w, IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            var st = w.Recv;
            if (!On(w) || w.Phase != Phase.Live)
            {
                st.CallTick.Clear();
                st.CatchTick.Clear();
                return;
            }
            if (inputs == null) return;
            var c = w.Cfg.receiving;
            int cooldown = Huddle.Ticks(w, c.callCooldownS), forget = Huddle.Ticks(w, c.catchForgetS);
            var qb = LivePlay.Qb(w);
            foreach (var p in w.Players)
            {
                if (!p.Human || !inputs.TryGetValue(p.Slot, out var input)) continue;
                if (input.Call && ShapeOf(w, p) > 0 && !w.Thrown && qb != null && LivePlay.Carrier(w) == qb
                    && (!st.CallTick.TryGetValue(p.Slot, out int last) || w.Tick - last >= cooldown))
                {
                    st.CallTick[p.Slot] = w.Tick;
                    st.Calls++;
                }
                // one jump at a time: a tap while the last one still counts changes nothing (tapping all the way
                // down the field is a jump too soon, not a free catch)
                if (input.Catch && p.Team == w.Offense && w.Ball.Carrier != p.Slot
                    && (!st.CatchTick.TryGetValue(p.Slot, out int jumped) || w.Tick - jumped > forget))
                    st.CatchTick[p.Slot] = w.Tick;
            }
        }

        // Is this receiver's shape pulsing now? (Until the throw, the end of the play or callPulseS.)
        public static bool Calling(World w, Player p)
        {
            if (!On(w) || p == null || w.Phase != Phase.Live || w.Thrown) return false;
            return w.Recv.CallTick.TryGetValue(p.Slot, out int at) && w.Tick - at < Huddle.Ticks(w, w.Cfg.receiving.callPulseS);
        }

        // The tick a receiver's current call began (for the stepped pulse on the TV), or -1.
        public static int CallStart(World w, Player p) => Calling(w, p) ? w.Recv.CallTick[p.Slot] : -1;

        // LivePlay's catch roll asks this: the chance for `p` to catch the ball now, given the plain chance.
        public static float Chance(World w, Player p, float plain)
        {
            if (!On(w) || p == null || p.Team != w.Offense || !w.Recv.CatchTick.TryGetValue(p.Slot, out int at)) return plain;
            var c = w.Cfg.receiving;
            int ago = w.Tick - at;
            if (ago <= Huddle.Ticks(w, c.catchWindowS)) return plain >= c.catchChanceMax ? plain : System.Math.Min(c.catchChanceMax, plain + c.catchWindowBonus);
            if (ago <= Huddle.Ticks(w, c.catchForgetS)) return System.Math.Max(0f, plain - c.catchEarlyPenalty);
            return plain;
        }
    }
}
