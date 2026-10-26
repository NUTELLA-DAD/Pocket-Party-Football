using System.Collections.Generic;
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
        // The receiver shapes in throw order (play.shapes): a throw names its target by shape.
        public IList<string> Shapes = new string[0];

        float stickX, stickY, stickAt;
        bool findPressed, sprintHeld, blockHeld, hikePressed, jukePressed, tacklePressed, lob;
        int throwTo;
        bool callPressed, catchPressed;   // WS-05
        CarrierMove movePressed;          // WS-04: SPIN / STIFF ARM / DIVE
        DefMove defPressed;               // WS-03

        public PhoneSource(Seat seat) { Seat = seat; }

        // Phone stick: x right, y DOWN (screen). The camera stands on the near sideline, so screen right is +X along
        // the field and screen up is +Z, away from the camera.
        public void OnStick(float x, float y, float now) { stickX = x; stickY = y; stickAt = now; }

        // Returns false when the press was ignored by the aid guard (or isn't a button this game knows).
        // tgt / lobHeld: a throw's receiver shape and whether it was held long enough to be a lob (sent on release).
        public bool OnButton(string b, bool down, string aid, string tgt = "", bool lobHeld = false)
        {
            if (aid != Aid) return false;
            switch (b)
            {
                case "find": if (down) findPressed = true; break;
                case "sprint": sprintHeld = down; break;
                case "block": blockHeld = down; break;
                case "hike": if (down) hikePressed = true; break;
                case "juke": if (down) jukePressed = true; break;
                case "tackle": if (down) tacklePressed = true; break;
                case "throw":
                    if (down) return true;                     // (the throw goes on release: tap = bullet, hold = lob)
                    int k = -1;
                    for (int i = 0; i < Shapes.Count; i++) if (Shapes[i] == tgt) k = i;
                    if (k < 0) return false;
                    throwTo = k + 1; lob = lobHeld;
                    break;
                case "scramble": break;                        // (the QB just runs with the stick)
                case "call": if (down) callPressed = true; break;     // WS-05: CALL FOR IT
                case "catch": if (down) catchPressed = true; break;   // WS-05: CATCH
                case "spin": if (down) movePressed = CarrierMove.Spin; break;           // WS-04
                case "stiff": if (down) movePressed = CarrierMove.StiffArm; break;      // WS-04
                case "divein": if (down) movePressed = CarrierMove.Dive; break;         // WS-04 (the carrier's dive; "dive" is DIVE TACKLE)
                case "dive": if (down) { tacklePressed = true; defPressed = DefMove.Dive; } break;   // WS-03 (a plain tackle with NO_DEFMOVES=1)
                case "swim": if (down) defPressed = DefMove.Swim; break;      // WS-03
                case "bull": if (down) defPressed = DefMove.BullRush; break;  // WS-03
                case "swat": if (down) defPressed = DefMove.Swat; break;      // WS-03
                case "jump": if (down) defPressed = DefMove.Jump; break;      // WS-03
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

        public void OnDisconnect() { stickX = stickY = 0; sprintHeld = false; blockHeld = false; }

        // A stick that has gone quiet (phone locked, Wi-Fi blip) stops.
        public void ExpireStick(float now, float staleSeconds)
        {
            if (now - stickAt > staleSeconds) { stickX = 0; stickY = 0; }
        }

        public PlayerInput Poll()
        {
            var i = new PlayerInput
            {
                MoveX = stickX, MoveZ = -stickY, Sprint = sprintHeld, Block = blockHeld,
                Hike = hikePressed, Juke = jukePressed, Tackle = tacklePressed, Throw = throwTo, Lob = lob,
                Call = callPressed, Catch = catchPressed,   // WS-05
                Move = movePressed,                         // WS-04
                Def = defPressed,                           // WS-03
            };
            hikePressed = jukePressed = tacklePressed = false;
            callPressed = catchPressed = false;            // WS-05
            movePressed = CarrierMove.None;                // WS-04
            defPressed = DefMove.None;                     // WS-03
            throwTo = 0; lob = false;
            return i;
        }
    }
}
