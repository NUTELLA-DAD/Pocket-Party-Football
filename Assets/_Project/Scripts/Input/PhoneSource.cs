using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Input
{
    // One phone's latest state. Filled on the main thread from WebSocket messages; presses are latched until the next
    // sim tick polls them, so a quick tap between two ticks is never lost. A press whose aid isn't the one the phone
    // was last told to show is ignored (a stale button from a layout that has since changed).
    public sealed class PhoneSource : IInputSource
    {
        public readonly Seat Seat;
        public int Slot => Seat.Slot;
        public bool Connected => Seat.Connected;

        // What the phone is currently showing (sent in the "ui" message); presses must carry it.
        public string Aid = "";

        float stickX, stickY, stickAt;
        bool findPressed, sprintHeld;

        public PhoneSource(Seat seat) { Seat = seat; }

        // Phone stick: x right, y DOWN (screen). The camera stands on the near sideline, so screen right is +X along
        // the field and screen up is +Z, away from the camera.
        public void OnStick(float x, float y, float now) { stickX = x; stickY = y; stickAt = now; }

        // Returns false when the press was ignored by the aid guard.
        public bool OnButton(string b, bool down, string aid)
        {
            if (aid != Aid) return false;
            switch (b)
            {
                case "find": if (down) findPressed = true; break;
                case "sprint": sprintHeld = down; break;
                default: return false;
            }
            return true;
        }

        public bool TakeFind()
        {
            bool f = findPressed;
            findPressed = false;
            return f;
        }

        public void OnDisconnect() { stickX = stickY = 0; sprintHeld = false; }

        // A stick that has gone quiet (phone locked, Wi-Fi blip) stops.
        public void ExpireStick(float now, float staleSeconds)
        {
            if (now - stickAt > staleSeconds) { stickX = 0; stickY = 0; }
        }

        public PlayerInput Poll() => new PlayerInput { MoveX = stickX, MoveZ = -stickY, Sprint = sprintHeld };
    }
}
