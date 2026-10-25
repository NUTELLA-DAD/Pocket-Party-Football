namespace PocketFootball.Input
{
    // What one player's controller says this tick. Phones, gamepads, bots and replays all fill the same struct (M0-c onwards).
    public struct PlayerInput
    {
        public float MoveX, MoveY;
        public bool Sprint, Find;
        public int Aid;
    }

    public interface IInputSource
    {
        int Slot { get; }
        bool Connected { get; }
        PlayerInput Poll();
    }
}
