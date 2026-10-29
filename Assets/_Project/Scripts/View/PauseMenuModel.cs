using System.Collections.Generic;
using PocketFootball.Sim;

namespace PocketFootball.View
{
    // The pause menu's buttons (FEATURES.pauseMenu, Boot/PauseMenu.cs draws them): which ones show, the highlight, and
    // the press-twice guard on the two that end the game. No Unity here, so the EditMode tests can press it.
    public sealed class PauseMenuModel
    {
        public enum Item { Continue, Settings, MainMenu, Quit }

        public readonly List<Item> Items = new List<Item>();
        public int Pick { get; private set; }
        public bool Confirming { get; private set; }   // MAIN MENU or QUIT pressed once: the second press does it

        // MAIN MENU only with the title screen on (else there is no menu to go back to).
        public PauseMenuModel(GameConfig cfg)
        {
            Items.Add(Item.Continue);
            Items.Add(Item.Settings);
            if (cfg.Feature("titleScreen")) Items.Add(Item.MainMenu);
            Items.Add(Item.Quit);
        }

        public Item Picked => Items[Pick];

        // FEATURES.pauseQr (NO_PAUSE_QR=1: the join panel stays in the game's corner as before): the join QR code moves
        // off the game screen into the pause menu, so a late joiner presses Esc and scans. Only with the pause menu
        // there to hold it (not in the lab, tool runs or NO_PAUSE_MENU=1), else nobody could join mid-game.
        public static bool QrInMenu(GameConfig cfg, bool menuThere) => menuThere && cfg.Feature("pauseQr");

        public void Reset() { Pick = 0; Confirming = false; }

        public void Move(int dir)
        {
            int n = Items.Count;
            Pick = ((Pick + dir) % n + n) % n;
            Confirming = false;
        }

        public void PickAt(int i)
        {
            if (i < 0 || i >= Items.Count || i == Pick) return;
            Pick = i;
            Confirming = false;
        }

        // A press on the highlighted button: the item to act on now, or null (the first press of a game-ending one).
        public Item? Press()
        {
            var it = Picked;
            bool ends = it == Item.MainMenu || it == Item.Quit;
            if (ends && !Confirming) { Confirming = true; return null; }
            Confirming = false;
            return it;
        }

        public string Label(int i)
        {
            var it = Items[i];
            if (Confirming && i == Pick) return "SURE? PRESS AGAIN";
            return it == Item.Continue ? "CONTINUE" : it == Item.Settings ? "SETTINGS" : it == Item.MainMenu ? "MAIN MENU" : "QUIT GAME";
        }
    }
}
