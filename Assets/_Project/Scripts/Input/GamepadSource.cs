using System.Collections.Generic;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine.InputSystem;
using PlayerInput = PocketFootball.Sim.PlayerInput;

namespace PocketFootball.Input
{
    // The pad's buttons, by the names game.json's gamepad block uses.
    public enum PadButton { A, B, X, Y, LB, RB, LT, RT, Start, Back, LS, RS, Up, Down, Left, Right }

    // One pad at one moment: the left stick (x right, y UP) and the buttons held.
    public struct PadState
    {
        public float X, Y;
        public uint Held;

        public static readonly string[] Names = { "a", "b", "x", "y", "lb", "rb", "lt", "rt", "start", "back", "ls", "rs", "up", "down", "left", "right" };

        public bool Has(PadButton b) => (Held & (1u << (int)b)) != 0;

        public PadState With(params PadButton[] bs)
        {
            var s = this;
            foreach (var b in bs) s.Held |= 1u << (int)b;
            return s;
        }

        public static PadState Read(Gamepad g)
        {
            var v = g.leftStick.ReadValue();
            var s = new PadState { X = v.x, Y = v.y };
            Set(ref s, PadButton.A, g.buttonSouth.isPressed); Set(ref s, PadButton.B, g.buttonEast.isPressed);
            Set(ref s, PadButton.X, g.buttonWest.isPressed); Set(ref s, PadButton.Y, g.buttonNorth.isPressed);
            Set(ref s, PadButton.LB, g.leftShoulder.isPressed); Set(ref s, PadButton.RB, g.rightShoulder.isPressed);
            Set(ref s, PadButton.LT, g.leftTrigger.isPressed); Set(ref s, PadButton.RT, g.rightTrigger.isPressed);
            Set(ref s, PadButton.Start, g.startButton.isPressed); Set(ref s, PadButton.Back, g.selectButton.isPressed);
            Set(ref s, PadButton.LS, g.leftStickButton.isPressed); Set(ref s, PadButton.RS, g.rightStickButton.isPressed);
            Set(ref s, PadButton.Up, g.dpad.up.isPressed); Set(ref s, PadButton.Down, g.dpad.down.isPressed);
            Set(ref s, PadButton.Left, g.dpad.left.isPressed); Set(ref s, PadButton.Right, g.dpad.right.isPressed);
            return s;
        }

        static void Set(ref PadState s, PadButton b, bool on) { if (on) s.Held |= 1u << (int)b; }
    }

    // WS-14: an Xbox pad playing one person's spot, through the same IInputSource as a phone. A pad seat is a
    // person's seat in the roster like a phone's (Kind Phone, no connection: nothing is ever sent to it, so it never
    // sees a secret), so the lobby, the head counts and the triangle over the player treat it like a phone.
    // Each pad button presses the phone button the gamepad block maps it to in the layout the phone would show right
    // now (PhoneLayouts.For), and the press goes through a PhoneSource of its own: every button a phone has, a pad has.
    // A press is read in the layout of the moment it happens, which is the pad's aid guard.
    public sealed class GamepadSource : IInputSource
    {
        public readonly Seat Seat;
        public int DeviceId = -1;
        public int Slot => Seat.Slot;
        public bool Connected => Seat.Connected;
        public string Layout { get; private set; } = "";

        readonly GamepadConfig cfg;
        readonly PhoneSource phone;
        readonly Queue<string> commands = new Queue<string>();
        readonly HashSet<string> holding = new HashSet<string>();
        uint was;
        int throwButton = -1, throwTo, kickButton = -1;
        float throwAt, kickAt, stickX;

        // The last kick swipe (the "kick" command): power 0..1 from how long A was held, aim -1..1 from the stick.
        public float KickPower { get; private set; }
        public float KickAim { get; private set; }
        int aidCounter;

        public GamepadSource(Seat seat, GamepadConfig cfg, IList<string> shapes)
        {
            Seat = seat;
            this.cfg = cfg;
            phone = new PhoneSource(seat) { Shapes = shapes ?? new string[0] };
        }

        // The phone button (or pad command) this pad button does in this layout, or null.
        public string ActionFor(string layout, PadButton b)
        {
            string name = PadState.Names[(int)b];
            if (cfg.layouts.TryGetValue(layout ?? "", out var map) && map.TryGetValue(name, out var a)) return a;
            return cfg.any.TryGetValue(name, out var g) ? g : null;
        }

        // Every frame: the pad's state and the layout its person's phone would show now.
        public void Feed(PadState s, string layout, float now)
        {
            if (layout != Layout)
            {
                Layout = layout;
                phone.Aid = "pad-" + layout + (++aidCounter);   // (holds carry over: let go below if unmapped now)
                throwButton = -1;
                kickButton = -1;
            }

            float x = s.X, y = s.Y, len = (float)System.Math.Sqrt(x * x + y * y);
            if (len <= cfg.deadzone) { x = 0f; y = 0f; }
            else
            {
                float k = System.Math.Min(1f, (len - cfg.deadzone) / (1f - cfg.deadzone)) / len;
                x *= k; y *= k;
            }
            stickX = x;
            phone.OnStick(x, -y, now);                 // (the phone's y points down the screen)

            // holds: down while any held button maps to them in this layout
            var want = new HashSet<string>();
            for (int b = 0; b < PadState.Names.Length; b++)
            {
                if ((s.Held & (1u << b)) == 0) continue;
                var a = ActionFor(layout, (PadButton)b);
                if (a != null && cfg.holds.Contains(a)) want.Add(a);
            }
            foreach (var a in want) if (holding.Add(a)) phone.OnButton(a, true, phone.Aid);
            var drop = new List<string>();
            foreach (var a in holding) if (!want.Contains(a)) drop.Add(a);
            foreach (var a in drop) { holding.Remove(a); phone.OnButton(a, false, phone.Aid); }

            // presses: on the way down; a throw goes on the way up (tap = bullet, held lobHoldS = lob)
            for (int b = 0; b < PadState.Names.Length; b++)
            {
                uint bit = 1u << b;
                bool down = (s.Held & bit) != 0, before = (was & bit) != 0;
                if (down && !before) Press(layout, (PadButton)b, now);
                else if (!down && before && b == throwButton)
                {
                    throwButton = -1;
                    int i = throwTo - 1;
                    if (i >= 0 && i < phone.Shapes.Count) phone.OnButton("throw", false, phone.Aid, phone.Shapes[i], now - throwAt >= cfg.lobHoldS);
                }
                else if (!down && before && b == kickButton)
                {
                    // the phone's swipe: held longer = harder (full at kickFullS), the stick aims
                    kickButton = -1;
                    KickPower = cfg.kickFullS > 0f ? System.Math.Min(1f, (now - kickAt) / cfg.kickFullS) : 1f;
                    KickAim = stickX;
                    commands.Enqueue("kick");
                }
            }
            was = s.Held;
        }

        // The buttons held right now count as already down (a pad that just joined: its START does nothing more).
        public void Prime(PadState s) { was = s.Held; }

        void Press(string layout, PadButton b, float now)
        {
            var a = ActionFor(layout, b);
            if (a == null || cfg.holds.Contains(a)) return;
            if (a.StartsWith("throw:", System.StringComparison.Ordinal))
            {
                if (int.TryParse(a.Substring(6), out int k) && k >= 1) { throwButton = (int)b; throwTo = k; throwAt = now; }
            }
            else if (a == "kick") { kickButton = (int)b; kickAt = now; }
            else if (a.StartsWith("team:", System.StringComparison.Ordinal) || a == "agree" || a == "kickoff") commands.Enqueue(a);
            else phone.OnButton(a, true, phone.Aid);
        }

        // The pad's commands that are not plain buttons (team:t, agree, kickoff, kick), for the host to carry out.
        public bool TakeCommand(out string cmd)
        {
            if (commands.Count > 0) { cmd = commands.Dequeue(); return true; }
            cmd = null;
            return false;
        }

        public bool TakeFind() => phone.TakeFind();

        public PlayerInput Poll() => phone.Poll();

        public void OnDisconnect()
        {
            phone.OnDisconnect();
            holding.Clear();
            commands.Clear();
            was = 0;
            throwButton = -1;
            kickButton = -1;
            Layout = "";
        }

        // ---- the game side (pure: the host and the tests share it) ----

        // A new pad's seat: PAD 1, PAD 2... (the number counts pads, not slots). Null when the game is full.
        public static Seat Join(Roster r, GamepadConfig cfg, int number)
        {
            return r.JoinPhone(-1, cfg.name + " " + number, null, "pad", out _);
        }

        // What this pad's person would see on a phone now: "pick" until they have a team and a player, and before
        // the game's first tick (the lobby), so a pad can still change sides until kickoff.
        public static string LayoutFor(World w, Seat seat)
        {
            if (seat.Team < 0 || w.Tick == 0) return "pick";
            var p = w.Find(seat.Slot);
            if (p == null || p.Team != seat.Team) return "wait";
            return PhoneLayouts.For(w, p);
        }

        // Picks a team the way a phone's pick does: take a bot's spot there; the spot left behind goes back to a bot
        // (in the bots' slots, from botSlotBase up). Null when the team is full of people.
        public static Player Pick(World w, Seat seat, int team, int teams, int botSlotBase)
        {
            if (team < 0 || team >= teams) return null;
            var p = w.Find(seat.Slot);
            if (p == null || p.Team != team)
            {
                bool had = p != null;
                int oldTeam = had ? p.Team : -1, oldSpot = had ? p.Spot : -1;
                float ox = had ? p.X : 0f, oz = had ? p.Z : 0f;
                var taken = w.ReplaceBot(team, seat.Slot);
                if (taken == null) return null;
                p = taken;
                if (had)
                {
                    int slot = botSlotBase;
                    while (w.Find(slot) != null) slot++;
                    var bot = w.AddPlayer(slot, oldTeam, false, oldSpot);
                    if (bot != null) { bot.X = ox; bot.Z = oz; }
                }
            }
            seat.Team = team;
            return p;
        }

        // A pad can't see the playbook (the TV may not show it), so A in the huddle goes with the team: the caller
        // locks the play with the most thumbs-up from the team's phones (ties: the first by id), or with none, the
        // next play in the book each huddle; anyone else gives that play a thumbs-up.
        public static string Favourite(World w, int team)
        {
            string best = null;
            int most = 0;
            foreach (var kv in Huddle.Thumbs(w, team)) if (kv.Value > most) { best = kv.Key; most = kv.Value; }
            if (best != null) return best;
            var book = Huddle.Playbook(w, team);
            return book.Count > 0 ? book[w.HuddleNumber % book.Count].id : null;
        }

        // The kicker's pad kicks, like the phone's swipe (Kicking.Kick checks it is the kicker and the moment).
        public static bool Kick(World w, int slot, float power, float aim) => Kicking.Kick(w, slot, power, aim);

        public static bool Agree(World w, int slot)
        {
            var p = w.Find(slot);
            if (p == null || w.Phase != Phase.Huddle || !Huddle.On(w)) return false;
            string id = Favourite(w, p.Team);
            if (id == null) return false;
            return Huddle.Caller(w, p.Team) == p ? Huddle.Lock(w, slot, id) : Huddle.Suggest(w, slot, id);
        }
    }
}
