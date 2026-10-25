using PocketFootball.Sim;

namespace PocketFootball.Input
{
    // Anything that drives a player: a phone now, gamepads and replays later. Bots live in the sim itself.
    public interface IInputSource
    {
        int Slot { get; }
        bool Connected { get; }
        PlayerInput Poll();
    }
}
