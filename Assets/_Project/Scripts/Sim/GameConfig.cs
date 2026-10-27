using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Every tunable lives in Config/game.json; this is its typed shape. Units: yards unless the name says otherwise.
    public sealed class GameConfig
    {
        public int schema;
        public SimConfig sim = new SimConfig();
        public FieldConfig field = new FieldConfig();
        public CameraConfig camera = new CameraConfig();
        public PlayersConfig players = new PlayersConfig();
        public BotsConfig bots = new BotsConfig();
        public FormationConfig formation = new FormationConfig();
        public BallConfig ball = new BallConfig();
        public HuddleConfig huddle = new HuddleConfig();
        public LivePlayConfig play = new LivePlayConfig();
        public DownsConfig downs = new DownsConfig();
        public ReceivingConfig receiving = new ReceivingConfig();   // WS-05 (Receiving.cs)
        // Loaded from Config/plays/*.json (not game.json) by ConfigLoader.AddPlays, sorted by id.
        [Newtonsoft.Json.JsonIgnore] public List<PlayConfig> plays = new List<PlayConfig>();
        public BaselineConfig baseline = new BaselineConfig();
        public List<TeamConfig> teams = new List<TeamConfig>();
        public NetConfig net = new NetConfig();
        public ViewConfig view = new ViewConfig();
        public AnimConfig anim = new AnimConfig();
        public CoachConfig coach = new CoachConfig();   // WS-06 (BotCoach.cs)
        public EventsConfig events = new EventsConfig();   // WS-00 (Events.cs)
        public ReplayConfig replay = new ReplayConfig();   // WS-08 (ReplayConfig.cs, View/Replay.cs)
        public ClockConfig clock = new ClockConfig();   // WS-02 (Clock.cs)
        public LobbyConfig lobby = new LobbyConfig();   // WS-07
        public CarrierConfig carrier = new CarrierConfig();   // WS-04 (CarrierMoves.cs)
        public DefenseConfig defense = new DefenseConfig();   // WS-03 defender moves (DefenseMoves.cs)
        public KickingConfig kicking = new KickingConfig();   // WS-01 (Kicking.cs)
        public StadiumConfig stadium = new StadiumConfig();   // WS-10 the stadium and crowd (StadiumConfig.cs, View/Stadium.cs)
        public AudiblesConfig audibles = new AudiblesConfig();   // WS-13 (Audibles.cs)
        public StatsConfig stats = new StatsConfig();      // WS-12 (Stats.cs, View/PostGame.cs, View/Celebrations.cs)
        public GamepadConfig gamepad = new GamepadConfig();   // WS-14 Xbox pads (GamepadConfig.cs, Input/GamepadSource.cs)
        public AudioConfig audio = new AudioConfig();   // WS-09 sound on the TV (AudioConfig.cs, View/Audio.cs)
        // Every rule switch. ConfigLoader turns FEATURES.<name> off when NO_<NAME>=1 is set.
        public SortedDictionary<string, bool> features = new SortedDictionary<string, bool>();

        public bool Feature(string name) => features.TryGetValue(name, out bool on) && on;
    }

    public sealed class SimConfig
    {
        public int tickHz, hashEveryTicks, maxTicksPerFrame;
    }

    // The mover: jog / sprint speeds, how fast you speed up and slow down, sprint stamina (1 = full), the bump circle.
    public sealed class PlayersConfig
    {
        public float jogSpeedYdS, sprintSpeedYdS, accelYdS2, decelYdS2;
        public float staminaDrainPerS, staminaRegenPerS, staminaRestart;
        public float radiusYd, bumpPushFrac, boundsMarginYd;
        public int perTeam;
    }

    // Bot v0: stands on its formation spot (walks back when bumped off it); with wander on (the baseline, B key)
    // it picks a random nearby spot every thinkEveryTicks (+ up to thinkJitterTicks) and walks there.
    public sealed class BotsConfig
    {
        public int thinkEveryTicks, thinkJitterTicks;
        public float wanderYd, speedFrac, arriveYd;
        public bool wander;
        // bots v1 (M1-d): blocking (a blocker in reach slows a defender to blockedSpeedFrac; he sheds the block after
        // shedAfterS and runs free for shedFreeS), rushing round a blocker, the QB's reads (one receiver at each
        // readTimesS, open = no defender within openYd; at readDeadlineS he throws to the most open), and a bot that
        // helps a person who has been idle idleTakeoverS during a live play
        public float blockReachYd, blockedSpeedFrac, shedAfterS, shedFreeS, rushAroundYd, openYd, readDeadlineS, idleTakeoverS;
        public float coachCallS;   // FEATURES.botQuickCall: a team with no people locks its coach's call this far into the huddle
        public List<float> readTimesS = new List<float>();
        // bots v2 (WS-06, FEATURES.botsV2, Bots.cs): a rusher this close with no block on him is pressure (throw it
        // or scramble); at the deadline or under pressure he throws only to a man with deadlineOpenYd of space; throws
        // miss by up to missYd + missPerYd per yard (+ pressureMissYd when pressured); longer than lobOverYd: a lob;
        // the man it is for sees where it will land only adjustS before it comes down; he won't throw it past a defender
        // within laneYd of the back half of the throw; a carrier within stepOutYd of a sideline with a tackler within
        // stepOutHitYd on his infield side heads out of bounds (stepOutAheadFrac: how much he still runs upfield)
        // escapeAfterS: before this a QB under pressure can't escape it yet
        public float escapeAfterS;
        public float pressureYd, deadlineOpenYd, missYd, missPerYd, pressureMissYd, lobOverYd, adjustS, laneYd, stepOutYd, stepOutHitYd, stepOutAheadFrac;
    }

    // One spot in a formation: back = yards from the line of scrimmage onto the team's own side, z = across.
    public sealed class SpotConfig
    {
        public string pos;
        public float back, z;
        public int number;
    }

    // The order of each list is the order humans fill spots: offense QB first, defense middle LB first.
    public sealed class FormationConfig
    {
        public float lineOfScrimmageXYd, attackDir;
        public int offenseTeam;
        public List<SpotConfig> offense = new List<SpotConfig>();
        public List<SpotConfig> defense = new List<SpotConfig>();
    }

    // The ball flies in 3D: Y is height in yards. A pass lands at catchHeightYd.
    public sealed class BallConfig
    {
        public float gravityYdS2, carryHeightYd, catchHeightYd, bulletSpeedYdS, lobApexYd;
        public float bounce, settleVyYdS, rollDecelYdS2, restSpeedYdS;
    }

    // The safety net: these seeded bot games must replay with the same hashes (Tests/Fixtures/baseline).
    public sealed class BaselineConfig
    {
        public int seconds, players;
        public List<int> seeds = new List<int>();
    }

    // The secret huddle: seconds to call a play, then seconds lined up before the next huddle (until M1-c's hike).
    public sealed class HuddleConfig
    {
        public float seconds, setSeconds;
    }

    // A live play (M1-c): the hike, handoffs, passes and catches, tackles, jukes, the whistle. Odds are 0..1.
    // receivers: the offense spots the QB can throw to, in the order of the shapes (shapes[i] is the badge over them).
    public sealed class LivePlayConfig
    {
        public float botHikeS, humanHikeMaxS, whistleS, maxPlayS, handoffYd, handoffWindowS, botThrowS;
        public float catchRadiusYd, catchMaxHeightYd, catchChanceOffense, catchChanceDefense;
        public float tackleReachYd, tackleBase, tackleSpeedBonus, tackleFromBehindBonus, stumbleSpeedFrac;
        public int tackleCooldownTicks, stumbleTicks, jukeTicks, jukeCooldownTicks;
        public float jukeBurstYdS, pursuitLeadS;
        public float throwMaxYards;   // the throw stick pushed all the way: this far from the QB (ThrowStick.cs)
        public float throwDeadZone;   // ...and let go nearer the middle than this (0..1): no throw
        public List<int> receivers = new List<int>();
        public List<string> shapes = new List<string>();
    }

    // Downs (M1-e): 10 yards in 4 downs for a new set; a touchdown is 6 points, then the other team starts at its own 25.
    public sealed class DownsConfig
    {
        public float firstDownYd, startOwnYd;
        public int downsToGain, touchdownPoints;
    }

    // One play (docs/PLAYS_FORMAT.md): 11 jobs in formation spot order. runner: the offense spot the QB hands off to
    // (run plays), -1 = the QB keeps it.
    public sealed class PlayConfig
    {
        public string id, name, side, desc;
        public int runner = -1;
        public List<JobConfig> jobs = new List<JobConfig>();
    }

    // route | block | zone | man | rush. Points are [ahead, z] yards from the ball (ahead = the offense's direction).
    public sealed class JobConfig
    {
        public string kind, text;
        public List<float[]> pts = new List<float[]>();
        public float radius;
        public int target = -1;
    }

    public sealed class TeamConfig
    {
        public string name, @short, color, color2;
        // the rigged Humanoid model under Resources every player of the team wears, and its colour map
        public string model, modelTexture;
        // FEATURES.posModels: a position wearing its own model instead (e.g. the linebackers); first match wins
        public List<PosModelConfig> posModels = new List<PosModelConfig>();
        // FEATURES.cheerleaders: the squad on the far sideline wears this rigged model and colour map (Stadium.cs)
        public string cheerModel, cheerTexture;

        // The model and colour map a player at this position wears.
        public void ModelFor(string pos, bool byPos, out string m, out string tex)
        {
            m = model; tex = modelTexture;
            if (!byPos || posModels == null) return;
            foreach (var pm in posModels)
                if (pm != null && pm.pos == pos && !string.IsNullOrEmpty(pm.model)) { m = pm.model; tex = pm.modelTexture; return; }
        }
    }

    public sealed class PosModelConfig
    {
        public string pos, model, modelTexture;
    }

    public sealed class NetConfig
    {
        public int port, portTries, maxPlayers, codeLength;
        public string bindAddress, codeLetters;
        public float stickStaleS;
    }

    public sealed class ViewConfig
    {
        public float playerHeightM, playerRadiusM, findMeS, plateFontPx, joinPanelQrPx, ballLengthM, ballWidthM, meSendHz;
        // FIND ME: the soft team-colour glow on the grass under the player (Markings.Beam): its size, how solid its middle
        public float findMeRadiusM, findMeAlpha;
        // the players' own toon shade (gauntlet 3): a warmer, same-hue shade than the stadium's cool purple family,
        // so a green jersey shades dark green, not teal (View/FootballFigure.KitMaterial)
        public string playerShadeHue, playerBounce, playerShadeLift;
        public float playerShadeUnify, playerShadeSaturation;
        // and a bolder ink line than the stadium's (concept 01's outlines), a softer rim (less plastic)
        public float playerOutlinePx, playerOutlineTint, playerRim;
        // how far apart (hips, metres) a tackler and the carrier go down, so the tackle meets (FEATURES.tackleContact)
        public float tackleContactM;
    }

    // The players' moves on the TV (View/Moves.cs, FEATURES.moves): key frames per second (stepped, no wobble), how
    // long each one-off move lasts, when a receiver puts his hands up (within reachS he'll be within reachYd of the
    // ball coming down at catching height), when the men on the grass get up (the last getUpS of the whistle), and
    // how often a blocker shoves. The snap (View/Snap.cs, FEATURES.snap): the ball spotted snapAheadYd in front of the
    // center's feet, flying back at snapSpeedYdS (at least snapMinS) with a rise of snapArcYd, pulled in for snapSecureS.
    public sealed class AnimConfig
    {
        public float fps, throwS, handoffS, secureS, bobbleS, reachYd, reachS, getUpS, blockShoveS;
        public float snapAheadYd, snapSpeedYdS, snapMinS, snapArcYd, snapSecureS;
        // FEATURES.blockLock (View/Moves.cs): a blocker and his man lock arms lockGapYd apart (each slides at most
        // lockPullYd, at lockSlideYdS) and face each other, turned only lockTurnDeg toward the camera
        public float lockGapYd, lockPullYd, lockSlideYdS, lockTurnDeg;
        // FEATURES.watchBall (View/PlayersView.cs): with a pass in the air, the men within watchYd of where it's aimed
        // turn to follow it: shoulders up to lookTwistDeg, the head lookHeadDeg more, the rest the whole body; in lookStepDeg steps
        public float watchYd, lookTwistDeg, lookHeadDeg, lookStepDeg;
        // FEATURES.faceDownfield (View/PlayersView.cs): a figure turns only cameraTurnDeg toward the camera (0 = it faces
        // straight down the field, or along its run); a locked pair turns no more than that either
        public float cameraTurnDeg;
        // FEATURES.momentumLean (View/Momentum.cs): a figure turns at most turnFastDegS at sprint speed (turnSlowDegS
        // standing), and leans into its speeding up, slowing and turning (the sim's own velocity change, smoothed over
        // leanTauS): leanMaxDeg at leanRefYdS2, curved by leanGamma so small cuts stay small; leanFeetShare of it tips
        // the whole body over its feet, the rest bends the chest; in leanStepDeg steps. Braking past plantFrom of the
        // full lean plants the lead leg (a reverse); a sideways lean widens the outside leg by up to cutSpreadDeg.
        public float turnSlowDegS, turnFastDegS, leanTauS, leanRefYdS2, leanMaxDeg, leanGamma, leanFeetShare, leanStepDeg;
        public float plantFrom, cutSpreadDeg;
    }

    // WS-07 the lobby (FEATURES.lobby): each person picks an offense spot and a defense spot before kickoff. The game
    // waits on the lobby until a phone taps KICK OFF (holdUntilKickoff), a bot plays for a dropped phone until it comes
    // back (botCoversDropped), and a swap request waits swapAskS seconds for the other person's answer.
    public sealed class LobbyConfig
    {
        public bool holdUntilKickoff, botCoversDropped;
        public float swapAskS;
    }

    // The field runs along X (goal line to goal line), across Z (sideline to sideline); the centre is (0, 0).
    public sealed class FieldConfig
    {
        public float lengthYd, widthYd, endZoneYd, yardM;
        public float stripeYd, lineEveryYd, lineWidthYd, sidelineBorderYd;
        public float hashFromSidelineYd, hashLengthYd, surroundYd;
        public string grassColor, grassAltColor, lineColor, endZoneColor, surroundColor, skyColor;
        public FieldPaintConfig paint = new FieldPaintConfig();   // WS-11 (FieldPaintConfig.cs, View/FieldView.cs)

        public float HalfLength => lengthYd * 0.5f;
        public float HalfWidth => widthYd * 0.5f;
        public float GoalLineX => HalfLength - endZoneYd;
    }

    public sealed class CameraConfig
    {
        public float visibleYards, fovDeg, pitchDeg, lookAtXYd, lookAtZYd;
        public float sideline;   // -1: the camera stands on the -Z sideline
        public float leadYd, followRate;   // how far ahead of the ball it looks, how quickly it catches up (per second)
        public FreeCameraConfig free = new FreeCameraConfig();   // the practice lab's free camera (FEATURES.labFreeCam)
        public LiveCameraConfig live = new LiveCameraConfig();   // the framing once a play is running (FEATURES.snapZoom)
    }

    // After the snap the TV camera eases back and up to this framing (from visibleYards / pitchDeg above), so the whole
    // play fits; it comes back in for the next snap. delayS: seconds after the snap before it starts; rate: per second.
    public sealed class LiveCameraConfig
    {
        public float visibleYards, pitchDeg, delayS, rate;
    }

    // The lab's free camera: drag to turn it, right-drag to slide it, the wheel to come closer, F1-F5 for these presets.
    public sealed class FreeCameraConfig
    {
        public float orbitDegPerPx, zoomStep, minDistM, maxDistM, minPitchDeg, maxPitchDeg;
        public float boundsMarginYd;  // how far past the field the point it looks at can slide
        public List<float> lensesDeg = new List<float>();   // , and . step through these (keeping the same framing)
        public List<FreeCameraPreset> presets = new List<FreeCameraPreset>();
    }

    // yawDeg 0 looks across the field from the -Z sideline (like the TV camera), 90 looks down the field toward +X.
    // visibleYards: how many yards fit across the screen at the point it looks at.
    public sealed class FreeCameraPreset
    {
        public string name;
        public float yawDeg, pitchDeg, fovDeg, visibleYards;
    }
}
