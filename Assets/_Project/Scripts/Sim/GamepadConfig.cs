using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Xbox pads (WS-14, Input/GamepadSource.cs, Boot/GamepadHost.cs, FEATURES.gamepad). Only the input side reads
    // these; the sim never does. deadzone: stick travel (0..1) that counts as no push; lobHoldS: a throw button held
    // this long is a lob (the phone's LOB_HOLD_MS); kickFullS: A held this long on the kick meter is a full-power
    // kick (the stick aims); join: the button that joins a pad; name: "PAD" -> PAD 1, PAD 2;
    // hintFontPx: the TV's pad line on a 1080p TV.
    // layouts: per phone layout (Input/PhoneLayouts.cs, plus "pick"), pad button -> the phone button it presses
    // ("throw:k" throws to receiver k, "team:t" picks team t, "agree" goes with the team's favourite play);
    // any: buttons that do the same thing in every layout (a layout's own mapping wins). holds: actions that are
    // held down like the phone's hold buttons (the rest fire on the press).
    public sealed class GamepadConfig
    {
        public float deadzone, lobHoldS, kickFullS, hintFontPx;
        public string join, name;
        public List<string> holds = new List<string>();
        public SortedDictionary<string, string> any = new SortedDictionary<string, string>();
        public SortedDictionary<string, SortedDictionary<string, string>> layouts = new SortedDictionary<string, SortedDictionary<string, string>>();
    }
}
