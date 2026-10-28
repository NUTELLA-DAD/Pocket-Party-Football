using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // WS-05 CALL FOR IT, the TV and phone side (the rule is Sim/Receiving.cs, behind FEATURES.receiving).
    // A receiver calling for it: his shape badge pulses big and small over his head on the TV, stepped at anim.fps
    // (no wobble), and his own team's QB phone gets one "call" message so its receiver button pulses too.
    // The message goes to the QB's phone ONLY: never to the other team, never into the public status message.
    public sealed class ReceivingHud : MonoBehaviour
    {
        public PartyHost Host;
        readonly Dictionary<int, int> sent = new Dictionary<int, int>();   // receiver slot -> the call tick already sent

        void Update()
        {
            var w = Host != null ? Host.World : null;
            if (w == null || Host.Server == null || !Receiving.On(w)) return;
            var qb = LivePlay.Qb(w);
            var seat = qb != null && qb.Human ? Host.Roster[qb.Slot] : null;
            foreach (var p in w.Players)
            {
                int at = Receiving.CallStart(w, p);
                if (at < 0 || (sent.TryGetValue(p.Slot, out int was) && was == at)) continue;
                sent[p.Slot] = at;
                // the QB's phone, and only if it really is on the receiver's team
                if (seat != null && seat.Connected && seat.Conn >= 0 && seat.Team == p.Team && qb.Team == p.Team)
                    Host.Server.Send(seat.Conn, Messages.Call(w, p));
            }
        }

        void OnGUI()
        {
            var w = Host != null ? Host.World : null;
            if (w == null || Host.Photo != null || !Receiving.On(w) || Replay.OnScreen) return;
            var cam = Camera.main;
            if (cam == null) return;
            GUI.depth = -1;   // over the plain badges
            float s = Screen.height / 1080f;
            foreach (var p in w.Players)
            {
                int at = Receiving.CallStart(w, p);
                if (at < 0) continue;
                Vector3 sp = cam.WorldToScreenPoint(Host.Players.HeadPosition(p.Slot) + Vector3.up * 0.55f);
                if (sp.z <= 0) continue;
                // stepped: big on even frames, small on odd ones, anim.fps frames a second of sim time
                int frame = (w.Tick - at) * Mathf.RoundToInt(w.Cfg.anim.fps) / w.Cfg.sim.tickHz;
                float grow = frame % 2 == 0 ? 1.7f : 1.3f;
                Hud.ShapeBadge(new Vector2(sp.x, Screen.height - sp.y - 4 * s), Receiving.ShapeOf(w, p) - 1, s * grow);
            }
        }
    }
}
