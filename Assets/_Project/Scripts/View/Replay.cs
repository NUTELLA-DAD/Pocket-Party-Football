using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketFootball.View
{
    // The instant replay (WS-08, FEATURES.replay; NO_REPLAY=1 turns it off). The tape copies the World after every
    // tick it sees; at the whistle (read from the shared event list, Sim/Events.cs) it waits replay.afterS, then plays
    // the last replay.seconds of the play (plus the afterS) back at replay.speed. The TV draws the replay with its own
    // figures from copies of the World, so the sim never knows: it keeps running into the huddle while the TV replays.
    // The result card (ResultCard.cs) shows over the replay and for replay.cardS after it.
    // Pure C#: the EditMode tests drive it with a World and no frames.
    public sealed class ReplayTape
    {
        readonly GameConfig cfg;
        readonly List<World> tape = new List<World>();   // oldest first, one copy per tick seen
        readonly Stack<World> spare = new Stack<World>();
        readonly List<GameEvent> fresh = new List<GameEvent>();
        readonly List<GameEvent> play = new List<GameEvent>();   // this play's events, from the hike
        int nextSeq, lastTick = -1;
        int whistleTick = -1;
        float losAtHike;
        float cardLeft;

        public bool Playing { get; private set; }
        public float Clock { get; private set; }     // TV seconds since the replay began
        public int FromTick { get; private set; }
        public int ToTick { get; private set; }
        public int Replays { get; private set; }     // how many have played (tests)
        public ResultCard.Info Card { get; private set; }
        public bool ShowCard => Card != null && (Playing || cardLeft > 0f);

        public ReplayTape(GameConfig cfg) { this.cfg = cfg; }

        public static bool On(GameConfig cfg) => cfg.Feature("replay");

        int Ticks(float s) => (int)Math.Round(s * cfg.sim.tickHz);

        // How long a replay lasts on the TV, seconds.
        public float LengthS => (ToTick - FromTick) / (float)cfg.sim.tickHz / Math.Max(0.05f, cfg.replay.speed);

        // The sim tick the replay shows now (with a fraction between ticks).
        public float PlayTick => FromTick + Clock * cfg.replay.speed * cfg.sim.tickHz;

        // Call after the World stepped, once a frame (or once a tick). Only reads the World.
        public void Observe(World w)
        {
            if (!On(cfg)) return;
            // what happened since last time
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            foreach (var e in fresh)
            {
                if (e.Kind == EventKind.Hike) { play.Clear(); losAtHike = w.LineOfScrimmageX; whistleTick = -1; }
                play.Add(e);
                if (e.Kind == EventKind.Whistle)
                {
                    whistleTick = e.Tick;
                    Card = ResultCard.Build(w, play, losAtHike);
                    cardLeft = 0f;
                }
            }
            // a person has hiked the next play (or the huddle was skipped): the replay gives way to the live game
            if (Playing && w.Phase == Phase.Live) Stop();
            if (Playing) return;   // (the tape holds still while it plays)
            if (w.Tick != lastTick) { Record(w); lastTick = w.Tick; }
            if (whistleTick >= 0 && w.Tick >= whistleTick + Ticks(cfg.replay.afterS)) Begin();
        }

        void Record(World w)
        {
            int keep = Ticks(cfg.replay.seconds + cfg.replay.afterS) + 2;
            while (tape.Count >= keep) { spare.Push(tape[0]); tape.RemoveAt(0); }
            var copy = spare.Count > 0 ? spare.Pop() : new World(cfg, 0);
            Copy(w, copy);
            tape.Add(copy);
        }

        void Begin()
        {
            int end = whistleTick + Ticks(cfg.replay.afterS);
            whistleTick = -1;
            if (tape.Count < 2) return;
            FromTick = Math.Max(tape[0].Tick, end - Ticks(cfg.replay.seconds + cfg.replay.afterS));
            ToTick = Math.Min(tape[tape.Count - 1].Tick, end);
            if (ToTick <= FromTick) return;
            Playing = true;
            Clock = 0f;
            Replays++;
        }

        void Stop()
        {
            if (!Playing) return;
            Playing = false;
            cardLeft = cfg.replay.cardS;
        }

        // TV time goes by. False once the replay is over.
        public bool Advance(float dt)
        {
            if (!Playing) { cardLeft = Math.Max(0f, cardLeft - dt); return false; }
            Clock += dt;
            if (Clock >= LengthS) { Stop(); return false; }
            return true;
        }

        // The two copies either side of a tick and how far between them it is (0: a, 1: b). False: nothing taped.
        public bool Segment(float tick, out World a, out World b, out float alpha)
        {
            a = b = null; alpha = 0f;
            if (tape.Count == 0) return false;
            int i = 0;
            while (i + 1 < tape.Count && tape[i + 1].Tick <= tick) i++;
            a = tape[i];
            b = i + 1 < tape.Count ? tape[i + 1] : a;
            int span = b.Tick - a.Tick;
            alpha = span > 0 ? Math.Max(0f, Math.Min(1f, (tick - a.Tick) / span)) : 1f;
            return true;
        }

        // Everything the TV draws, from one World into another (a copy on the tape, or the replay's own World). Never
        // the live World as `to`.
        public static void Copy(World from, World to)
        {
            to.Tick = from.Tick;
            to.Offense = from.Offense;
            to.LineOfScrimmageX = from.LineOfScrimmageX;
            to.Dir = from.Dir;
            to.Down = from.Down;
            to.FirstDownX = from.FirstDownX;
            to.Note = from.Note;
            to.PendingTurnover = from.PendingTurnover;
            to.Phase = from.Phase;
            to.PhaseTicks = from.PhaseTicks;
            to.HuddleNumber = from.HuddleNumber;
            for (int t = 0; t < from.Calls.Length && t < to.Calls.Length; t++) to.Calls[t] = from.Calls[t];
            to.PlayTicks = from.PlayTicks;
            to.Result = from.Result;
            to.NextLos = from.NextLos;
            to.Thrown = from.Thrown;
            to.Intercepted = from.Intercepted;
            to.CatchTried.Clear();
            foreach (int s in from.CatchTried) to.CatchTried.Add(s);
            while (to.Players.Count > from.Players.Count) to.Players.RemoveAt(to.Players.Count - 1);
            while (to.Players.Count < from.Players.Count) to.Players.Add(new Player());
            for (int i = 0; i < from.Players.Count; i++) CopyPlayer(from.Players[i], to.Players[i]);
            var fb = from.Ball; var tb = to.Ball;
            tb.State = fb.State; tb.Carrier = fb.Carrier;
            tb.X = fb.X; tb.Y = fb.Y; tb.Z = fb.Z; tb.VX = fb.VX; tb.VY = fb.VY; tb.VZ = fb.VZ;
            tb.TargetX = fb.TargetX; tb.TargetZ = fb.TargetZ;
            var fh = from.Hit; var th = to.Hit;   // FEATURES.hitAngle: how the last hit landed (Moves reads it)
            th.Last = fh.Last; th.Tick = fh.Tick; th.Tackler = fh.Tackler; th.Carrier = fh.Carrier;
            th.FromX = fh.FromX; th.FromZ = fh.FromZ; th.ToX = fh.ToX; th.ToZ = fh.ToZ;
        }

        static void CopyPlayer(Player f, Player t)
        {
            t.Slot = f.Slot; t.Team = f.Team; t.Spot = f.Spot; t.Number = f.Number; t.Pos = f.Pos; t.Human = f.Human;
            t.X = f.X; t.Z = f.Z; t.VX = f.VX; t.VZ = f.VZ; t.HomeX = f.HomeX; t.HomeZ = f.HomeZ;
            t.Stamina = f.Stamina; t.Tired = f.Tired; t.Sprinting = f.Sprinting; t.OutOfBounds = f.OutOfBounds;
            t.TackleCooldown = f.TackleCooldown; t.StumbleTicks = f.StumbleTicks; t.JukeTicks = f.JukeTicks; t.JukeCooldown = f.JukeCooldown; t.RouteIndex = f.RouteIndex;
            t.BlockedTicks = f.BlockedTicks; t.FreeTicks = f.FreeTicks; t.IdleTicks = f.IdleTicks;
            t.OffBalanceTicks = f.OffBalanceTicks; t.OffBalanceBy = f.OffBalanceBy;
            t.BotHelp = f.BotHelp; t.Blocking = f.Blocking;
            t.GoalX = f.GoalX; t.GoalZ = f.GoalZ; t.NextThinkTick = f.NextThinkTick;
        }
    }

    // The replay on the TV: its own figures and ball (a second PlayersView, so the live moves are never disturbed),
    // the live ones hidden while it plays, the camera following the replay's ball, a REPLAY tag and the result card.
    // PartyHost drives it with one line at the end of its Update (Drive).
    public sealed class Replay : MonoBehaviour
    {
        GameConfig cfg;
        World live;
        PlayersView liveView;
        CameraRig rig;
        Func<int, string> nameOf;
        public ReplayTape Tape { get; private set; }
        PlayersView view;                                   // the replay's figures, with their own moves
        readonly List<GameObject> mine = new List<GameObject>();
        readonly List<GameObject> liveRoots = new List<GameObject>();
        World ghost;                                        // what the replay's figures are drawn from
        World shownA;                                       // the tape copy last loaded as the "before" tick
        float camX;
        bool hidden;
        GUIStyle tagStyle;

        public bool Showing => Tape != null && Tape.Playing;

        // True while a replay is on the TV. The live game's marks over the field (the rings under people's players,
        // the triangles over them, shape badges, the throw aim, the kick meter...) stand where the live players are,
        // not where the replay's are, so each of them checks this and hides.
        public static bool OnScreen { get; private set; }

        // The World the replay is drawing this frame (its line of scrimmage and first-down line), or null.
        public World Shown => Showing ? ghost : null;

        // From PartyHost.Update, after the live view drew and the camera moved. names: a person's name by slot (or null).
        public static Replay Drive(GameObject on, GameConfig cfg, World live, PlayersView liveView, CameraRig rig, Func<int, string> names)
        {
            if (!ReplayTape.On(cfg)) return null;
            var r = on.GetComponent<Replay>();
            if (r == null)
            {
                r = on.AddComponent<Replay>();
                r.cfg = cfg; r.live = live; r.liveView = liveView; r.rig = rig;
                r.Tape = new ReplayTape(cfg);
                r.ghost = new World(cfg, 0);
                r.FindLiveRoots();
                r.Build();
            }
            r.nameOf = names;
            r.Frame();
            return r;
        }

        // The live view's figures, ball and its shadow (made by PlayersView before the replay's own).
        void FindLiveRoots()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == "Players" || go.name == "Ball" || go.name == "BallShadow") liveRoots.Add(go);
        }

        // The replay's view, built and hidden ahead of time so the first replay starts without a hitch.
        void Build()
        {
            var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            view = new PlayersView(cfg, LiveMaterial());
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (!before.Contains(go)) mine.Add(go);
            // build the 22 figures now: tick -1 is one the moves never look at
            ReplayTape.Copy(live, ghost);
            ghost.Tick = -1;
            view.Render(ghost, 1f);
            SetMine(false);
        }

        // Before a replay: the moves forget the last one. They look at its first tick twice, far back in time, so
        // whatever they make of the jump from the last replay is over (and a held tackle is cleared) by its first tick.
        void Prime()
        {
            const int Back = 100000;
            if (!Tape.Segment(Tape.FromTick, out var first, out _, out _)) return;
            ReplayTape.Copy(first, ghost);
            ghost.Phase = Phase.Set;   // (from a whistle to not: the moves let go of held poses)
            ghost.Tick = first.Tick - Back;
            view.Moves.Observe(ghost);
            ReplayTape.Copy(first, ghost);
            ghost.Tick = first.Tick - Back + 1;
            view.Moves.Observe(ghost);
            shownA = null;
        }

        Material LiveMaterial()
        {
            foreach (var p in live.Players)
            {
                var f = liveView.Figure(p.Slot);
                var r = f != null ? f.GetComponentInChildren<Renderer>() : null;
                if (r != null) return r.sharedMaterial;
            }
            return null;
        }

        void SetMine(bool on) { foreach (var go in mine) if (go != null) go.SetActive(on); }

        void SetLive(bool on) { foreach (var go in liveRoots) if (go != null) go.SetActive(on); }

        void Frame()
        {
            bool was = Tape.Playing;
            Tape.Observe(live);
            if (Tape.Playing && !was) { camX = float.NaN; Prime(); }
            Tape.Advance(Time.unscaledDeltaTime);   // (also the card's time after the replay)
            OnScreen = Tape.Playing;

            if (!Tape.Playing)
            {
                if (hidden) { SetLive(true); SetMine(false); hidden = false; }
                return;
            }
            if (!hidden) { SetMine(true); hidden = true; }
            SetLive(false);   // (every frame: the live view turns its ball shadow back on)
            if (!Tape.Segment(Tape.PlayTick, out var a, out var b, out float alpha)) return;
            if (a != shownA)
            {
                // a new tick: the "before" positions, then the "after" ones, as PartyHost does for the live game
                ReplayTape.Copy(a, ghost);
                view.BeforeStep(ghost);
                shownA = a;
            }
            ReplayTape.Copy(b, ghost);
            view.Render(ghost, alpha);
            FollowBall(Time.unscaledDeltaTime);
        }

        // Like the live camera: along the field with the ball, ahead of the carrier, to where a pass comes down.
        void FollowBall(float dt)
        {
            var bl = ghost.Ball;
            var carrier = LivePlay.Carrier(ghost);
            float target = bl.State == BallState.Flying ? bl.TargetX : carrier != null ? carrier.X + ghost.Dir * cfg.camera.leadYd : bl.X;
            float lim = cfg.field.HalfLength + cfg.field.sidelineBorderYd - cfg.camera.visibleYards * 0.5f;
            target = Mathf.Clamp(target, -lim, lim);
            if (float.IsNaN(camX)) camX = target;   // a cut to the replay, like on TV
            camX += (target - camX) * (1f - Mathf.Exp(-dt * cfg.camera.followRate * cfg.replay.speed));
            rig.SetLookAtX(camX);
        }

        void OnDestroy()
        {
            OnScreen = false;
            foreach (var go in mine) if (go != null) Destroy(go);
        }

        void OnGUI()
        {
            UiFont.Use();   // (FEATURES.toonHud: the game's own lettering)
            if (Tape == null) return;
            GUI.depth = -10;   // over the game's HUD
            float s = Screen.height / 1080f;
            if (Tape.Playing)
            {
                tagStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                tagStyle.fontSize = Mathf.RoundToInt(cfg.replay.tagFontPx * s);
                var r = new Rect(24 * s, Screen.height - 96 * s, 220 * s, 64 * s);
                GUI.color = new Color(0.75f, 0.1f, 0.1f, 0.92f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, "REPLAY", tagStyle);
            }
            if (Tape.ShowCard) ResultCard.Draw(Tape.Card, cfg, nameOf, s);
        }
    }
}
