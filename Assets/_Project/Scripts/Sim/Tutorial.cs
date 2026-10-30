using System.Collections.Generic;
using Newtonsoft.Json;

namespace PocketFootball.Sim
{
    // Config/tutorial.json: the first-drive tutorial (FEATURES.tutorial, Boot/TutorialMode.cs). The lab's own numbers
    // (keyboardSlot, botSlotBase, lineOfScrimmageXYd, ...) plus the steps, in order. Each step is one lab drill with a
    // goal and the words the TV shows for it:
    //   complete   a pass caught by your team          gain   you carry the ball goalYd past the line
    //   tackle     the ball carrier is tackled          juke   you juke with the ball
    //   play       any play run to the whistle
    // A step that is not done after giveUpReps tries moves on anyway (nobody gets stuck on their first evening).
    public sealed class TutorialConfig
    {
        public LabConfig lab = new LabConfig();
        public float doneS, finishS;
        public int giveUpReps;
        public string introTitle = "", introText = "", niceText = "", moveOnText = "", finishTitle = "", finishText = "";
        public List<TutorialStep> steps = new List<TutorialStep>();
    }

    public sealed class TutorialStep
    {
        public string id = "", title = "", text = "", goal = "play";
        public float goalYd;
        public LabDrill drill = new LabDrill();
    }

    // The tutorial's progress on a World: the drill of the current step (run by Lab, the game's own rules), whether its
    // goal was met, the short NICE pause, then the next step; after the last one, Finished. Nothing counts until Begin
    // (the TV waits for a phone first; the drill plays behind the join card meanwhile).
    public sealed class Tutorial
    {
        public readonly World W;
        public readonly TutorialConfig C;
        public readonly Lab Lab;
        public int Step { get; private set; }
        public bool Started { get; private set; }
        public bool Done { get; private set; }        // this step's goal met (or given up): the NICE card, then the next
        public bool GaveUp { get; private set; }      // ...because it took too many tries
        public bool Finished { get; private set; }    // every step done: the finish card
        public TutorialStep Current => C.steps[Step];
        public int StepCount => C.steps.Count;

        int doneTick = -1, starts = -1;
        bool flew;          // the ball was in the air in this rep (a throw)
        Phase lastPhase;

        public static TutorialConfig Parse(string json) => JsonConvert.DeserializeObject<TutorialConfig>(json) ?? new TutorialConfig();

        public Tutorial(World w, TutorialConfig c)
        {
            W = w;
            C = c;
            c.lab.drills.Clear();
            foreach (var s in c.steps) { s.drill.id = s.id; s.drill.name = s.title; s.drill.help = s.text; c.lab.drills.Add(s.drill); }
            Lab = new Lab(w, c.lab);
        }

        public bool Ready => C.steps.Count > 0;

        // The first step on the field (before Begin: it plays behind the join card).
        public void Setup() { if (Ready) Lab.Pick(0); }

        // A phone joined (or ENTER on the TV): the first step from the top, and from now on the goals count.
        public void Begin()
        {
            if (!Ready || Started) return;
            Started = true;
            Step = 0;
            Done = GaveUp = Finished = false;
            Lab.Pick(0);
        }

        // Skip the step (TAB on the TV).
        public void Skip() { if (Started && !Finished && !Done) Complete(true); }

        // After every World.Step, after Lab.AfterTick.
        public void AfterTick()
        {
            if (!Ready || !Started || Finished) return;
            var w = W;
            if (Done)
            {
                if (w.Tick - doneTick < Huddle.Ticks(w, C.doneS)) return;
                if (Step + 1 >= C.steps.Count) { Finished = true; doneTick = w.Tick; return; }
                Step++;
                Done = GaveUp = false;
                Lab.Pick(Step);
                return;
            }
            if (Lab.Starts != starts) { starts = Lab.Starts; flew = false; lastPhase = w.Phase; }
            if (w.Ball.State == BallState.Flying) flew = true;
            if (Met(w)) Complete(false);
            else if (C.giveUpReps > 0 && Lab.Rep >= C.giveUpReps) Complete(true);
            lastPhase = w.Phase;
        }

        // Seconds the finish card has been up (the host goes back to the title after finishS).
        public float FinishedForS => Finished ? (W.Tick - doneTick) / (float)W.Cfg.sim.tickHz : 0f;
        public bool ReadyToLeave => Finished && FinishedForS >= C.finishS;

        void Complete(bool gaveUp)
        {
            Done = true;
            GaveUp = gaveUp;
            doneTick = W.Tick;
        }

        bool Met(World w)
        {
            var s = Current;
            var k = LivePlay.Carrier(w);
            switch (s.goal)
            {
                case "complete":
                    return flew && k != null && k.Team == w.Offense && k != LivePlay.Qb(w) && !w.Intercepted;
                case "gain":
                    return k != null && k.Slot == Lab.You && (k.X - w.LineOfScrimmageX) * w.Dir >= s.goalYd;
                case "juke":
                    return k != null && k.Slot == Lab.You && k.JukeTicks > 0;
                case "tackle":
                    return w.Phase == Phase.Whistle && lastPhase == Phase.Live && (w.Result == "TACKLED" || w.Result == "SACKED");
                default:   // "play"
                    return w.Phase == Phase.Whistle && lastPhase == Phase.Live;
            }
        }

        // The words on the TV right now: the step's title and text, or the intro, NICE, or the finish card.
        public string Title => !Started ? C.introTitle : Finished ? C.finishTitle : Done ? (GaveUp ? C.moveOnText : C.niceText) : Current.title;
        public string Text => !Started ? C.introText : Finished ? C.finishText : Current.text;
    }
}
