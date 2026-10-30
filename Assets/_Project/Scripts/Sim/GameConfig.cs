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
        public List<TeamConfig> teams = new List<TeamConfig>();   // the two that play: away, home (TeamPick may swap them)
        public List<TeamConfig> moreTeams = new List<TeamConfig>();   // FEATURES.teamPick: the other teams of the league (TeamPick.cs)
        [Newtonsoft.Json.JsonIgnore] public List<TeamConfig> league;   // teams + moreTeams as loaded (TeamPick.League)
        public NetConfig net = new NetConfig();
        public ViewConfig view = new ViewConfig();
        public AnimConfig anim = new AnimConfig();
        public CoachConfig coach = new CoachConfig();   // WS-06 (BotCoach.cs)
        public EventsConfig events = new EventsConfig();   // WS-00 (Events.cs)
        public ReplayConfig replay = new ReplayConfig();   // WS-08 (ReplayConfig.cs, View/Replay.cs)
        public ClockConfig clock = new ClockConfig();   // WS-02 (Clock.cs)
        public OvertimeConfig overtime = new OvertimeConfig();   // FEATURES.overtime: a tied game goes to overtime (Clock.cs)
        public LobbyConfig lobby = new LobbyConfig();   // WS-07
        public CarrierConfig carrier = new CarrierConfig();   // WS-04 (CarrierMoves.cs)
        public DefenseConfig defense = new DefenseConfig();   // WS-03 defender moves (DefenseMoves.cs)
        public KickingConfig kicking = new KickingConfig();   // WS-01 (Kicking.cs)
        public StadiumConfig stadium = new StadiumConfig();   // WS-10 the stadium and crowd (StadiumConfig.cs, View/Stadium.cs)
        public SkyConfig sky = new SkyConfig();   // the sunset sky and its light (SkyConfig.cs, View/SkyView.cs)
        // FEATURES.goldenStadium: the golden-hour stadium (StadiumLook.cs): game.json blocks laid over the sky, the
        // stadium and the field paint before anything is built
        public Newtonsoft.Json.Linq.JObject stadiumLook;
        public AudiblesConfig audibles = new AudiblesConfig();   // WS-13 (Audibles.cs)
        public CustomPlaysConfig customPlays = new CustomPlaysConfig();   // FEATURES.customPlays: draw-your-own plays (CustomPlays.cs)
        public StatsConfig stats = new StatsConfig();      // WS-12 (Stats.cs, View/PostGame.cs, View/Celebrations.cs)
        public GamepadConfig gamepad = new GamepadConfig();   // WS-14 Xbox pads (GamepadConfig.cs, Input/GamepadSource.cs)
        public AudioConfig audio = new AudioConfig();   // WS-09 sound on the TV (AudioConfig.cs, View/Audio.cs)
        public MusicConfig music = new MusicConfig();   // FEATURES.music: the title, huddle and post-game music (MusicConfig.cs, View/Music.cs)
        public HitConfig hit = new HitConfig();   // FEATURES.hitAngle: straight-on hits and glancing blows (HitAngle.cs)
        public RatingsConfig ratings = new RatingsConfig();   // player ratings by position (Ratings.cs, FEATURES.ratings)
        public TitleConfig title = new TitleConfig();   // the title screen and its attract mode (TitleConfig.cs, Boot/TitleScreen.cs)
        public MarksConfig marks = new MarksConfig();   // FEATURES.jerseyNumbers / eyesLook: the players' numbers and eyes (View/JerseyMarks.cs)
        public ReactionsConfig reactions = new ReactionsConfig();   // FEATURES.faceReactions / grassStains / nameBars (View/Reactions.cs)
        public BotsV3Config botsV3 = new BotsV3Config();   // bots v3: reactions, blocking assignments, kick lanes (BotsV3.cs, FEATURES.botsV3)
        public CatchPlayConfig catchPlay = new CatchPlayConfig();   // FEATURES.catchPlay: diving and contested catches (CatchPlay.cs)
        public TightCoverageConfig tightCoverage = new TightCoverageConfig();   // FEATURES.tightCoverage: a man right on the receiver picks it off or knocks it down (TightCoverage.cs)
        public PursuitConfig pursuit = new PursuitConfig();   // FEATURES.pursuitLanes: chasing the ball carrier in lanes (Pursuit.cs)
        public CupConfig cup = new CupConfig();   // FEATURES.cup: the Couch Cup bracket (Cup.cs, Boot/CupHost.cs)
        public WeatherConfig weather = new WeatherConfig();   // FEATURES.weather / sandField: rain, snow, a sand field (Weather.cs)
        public WeatherLookConfig weatherLook = new WeatherLookConfig();   // ...and how they look on the TV (View/WeatherView.cs)
        public RosterConfig roster = new RosterConfig();   // FEATURES.rosterPage: the title's ROSTER page (RosterCards.cs, Boot/RosterPage.cs)
        public DriveBlockConfig driveBlock = new DriveBlockConfig();   // FEATURES.driveBlock: shoving your man with the drive stick (DriveBlock.cs)
        public DriveRushConfig driveRush = new DriveRushConfig();      // FEATURES.driveRush: the rusher shoving his blocker with the BULL RUSH stick (DriveRush.cs)
        public GameLengthConfig gameLength = new GameLengthConfig();   // FEATURES.gameLength: the SETTINGS page's game lengths (GameLength.cs)
        public CrashLogConfig crashLog = new CrashLogConfig();   // FEATURES.crashLog: errors written to a crash file (View/CrashReport.cs, Boot/CrashLog.cs)
        public SeasonConfig season = new SeasonConfig();   // FEATURES.season: run a club through a season of recruits, boosts and coaching (Season.cs, Boot/SeasonHost.cs)
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
        public float onsideGapYd;   // FEATURES.stayOnside: how far short of the line of scrimmage a player stops before the snap
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
        public SortedDictionary<string, FormationSetConfig> sets = new SortedDictionary<string, FormationSetConfig>();   // FEATURES.formations
    }

    // A named formation (FEATURES.formations): a play's "formation" picks one. spots: [back, z] per formation spot, in
    // the same order as formation.offense / defense (who is who stays the same; only where they stand changes).
    public sealed class FormationSetConfig
    {
        public string name, side;
        public List<float[]> spots = new List<float[]>();
        internal List<SpotConfig> Built;    // the spots with pos and number filled in from the base list (Formation.SpotsOf)
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
        public List<string> shapeColors = new List<string>();   // FEATURES.colorTargets: shapeColors[i] ("#rrggbb") paints shapes[i]
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
        public string formation;            // FEATURES.formations: a formation.sets id; none = the base line-up
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
        // FEATURES.jerseyNumbers: the players' numbers, white (numberFill) with a thin edge (numberEdge) in the team's trim
        public string numberFill, numberEdge;
        // FEATURES.sandField: "sand" when the team's home field is sand (Weather.cs, View/FieldCover.cs); else grass
        public string field;

        // The model and colour map a player at this position wears.
        public void ModelFor(string pos, bool byPos, out string m, out string tex) => ModelFor(pos, byPos, out m, out tex, out _);

        // bend: the position model's strideBend (1 for the team's own model, or none given). number: the player's jersey
        // number (-1 unknown), for an entry that lists its numbers (the outside linebackers, not the middle one)
        public void ModelFor(string pos, bool byPos, out string m, out string tex, out float bend, int number = -1)
        {
            m = model; tex = modelTexture; bend = 1f;
            if (!byPos || posModels == null) return;
            foreach (var pm in posModels)
                if (pm != null && pm.pos == pos && !string.IsNullOrEmpty(pm.model) && (pm.numbers == null || pm.numbers.Count == 0 || pm.numbers.Contains(number)))
                { m = pm.model; tex = pm.modelTexture; bend = pm.strideBend > 0f ? pm.strideBend : 1f; return; }
        }
    }

    // FEATURES.jerseyNumbers and FEATURES.eyesLook (View/JerseyMarks.cs, Toon shader): each model's numbers and irises
    // were painted out of its colour maps; its marks map (tools/rig/marks.py) says where the shader draws them.
    // The digits: the game's own glyphs drawn with a pen penHalf either side of the line and an edge round it, gap
    // between two digits (glyph units: a digit is 1 wide, 1.4 tall).
    // The eyes: the iris moves up to eyeMaxX across and eyeMaxY up or down (iris radii), all the way at eyeFullDeg off
    // where his head points, in eyeStep steps. Each glanceKeys key frames he may (glanceChance) look glanceDeg aside;
    // once every blinkEveryKeys key frames (at a time his own) he blinks for blinkKeys.
    public sealed class MarksConfig
    {
        public float penHalf, edge, gap;
        public float eyeMaxX, eyeMaxY, eyeFullDeg, eyeStep;
        public int glanceKeys, blinkEveryKeys, blinkKeys;
        public float glanceChance, glanceDeg;
        public List<ModelMarksConfig> models = new List<ModelMarksConfig>();
        public List<string> skinTones = new List<string>();   // FEATURES.skinTones: the skin colours players are given, light to dark
        public List<string> hairColors = new List<string>();  // FEATURES.hairColors: the hair colours players are given, dark to fair

        public ModelMarksConfig For(string model)
        {
            if (models == null || string.IsNullOrEmpty(model)) return null;
            foreach (var m in models) if (m != null && m.model == model) return m;
            return null;
        }
    }

    // FEATURES.faceReactions: how long a face stays happy or wincing after a play (seconds of sim time). FEATURES.grassStains:
    // how much grass a man picks up going down (0..stainMax) and how the stains look. FEATURES.nameBars: the slim name bar
    // over each person's player (font px at 1080p, how see-through it is during a live play, how far over his head).
    public sealed class ReactionsConfig
    {
        public float touchdownS, bigHitS, sackS, pickS;
        public float stainTackled, stainTackler, stainBigHit, stainMax;
        public string stainGrass, stainDirt;
        public float stainScale, stainOpacity;
        public float barFontPx, barLiveAlpha, barAboveM;
    }

    // One model's marks map (under Resources), its iris and eyelid colours, and the number painted on it at first (worn
    // by everyone with FEATURES.jerseyNumbers off).
    public sealed class ModelMarksConfig
    {
        public string model, map, iris, lid;
        public string skin;   // the model's own skin colour (marks.py prints it): each man's tone is drawn as a ratio of it
        public string hair;   // a model with hair showing (the cornerback's braid): its own hair colour (marks.py prints it)
        public int number;
    }

    public sealed class PosModelConfig
    {
        public string pos, model, modelTexture;
        // the running knee's bend on this model, a share of the stride's (strideKneeLandDeg, strideKneeDeg,
        // strideKneePushDeg): a big, short-legged build (the linebacker's) looks squatting running on a bent knee
        public float strideBend;
        // only the players wearing these numbers at this position (none: every one)
        public List<int> numbers = new List<int>();
    }

    public sealed class NetConfig
    {
        public int port, portTries, maxPlayers, codeLength;
        public string bindAddress, codeLetters;
        public float stickStaleS;
        // FEATURES.hotspotQr: the address range of this PC's own Wi-Fi hotspot (Windows Mobile Hotspot: 192.168.137.x),
        // and how often the TV looks again for a hotspot turned on or off while the game runs
        public string hotspotPrefix;
        public float addressCheckS;
    }

    public sealed class ViewConfig
    {
        public float playerHeightM, playerRadiusM, findMeS, plateFontPx, joinPanelQrPx, pauseQrPx, ballLengthM, ballWidthM, meSendHz;
        // the frame meter (Boot/FrameMeter.cs, F3 or -fps): how often it writes a line to the log, and what counts as a slow frame
        public float perfLogEveryS, perfSlowFrameMs;
        // FEATURES.smoothMotion: the steady frame rate the TV draws at (vsync every N refreshes when the screen's rate allows)
        public float frameRate;
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
        public float kickS;   // FEATURES.kickMotion: the kicker's leg, from the foot meeting the ball to landing
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
        // FEATURES.hitAngle (View/Moves.cs): a big hit's slide back to where he goes down, in knockKeys key frames
        public int knockKeys;
        // FEATURES.momentumLean (View/Momentum.cs): a figure turns at most turnFastDegS at sprint speed (turnSlowDegS
        // standing), and leans into its speeding up, slowing and turning (the sim's own velocity change, smoothed over
        // leanTauS going in, leanFallS coming back): leanMaxDeg at leanRefYdS2, curved by leanGamma so small cuts stay
        // small; sideways at most sideCapDeg (the rest goes front or back); plus speedLeanDeg forward at sprint speed.
        // leanFeetShare of it tips the whole body over its feet, the rest bends the chest; in leanStepDeg steps. Braking
        // past plantFrom of the full lean plants the lead leg (a reverse); a sideways lean widens the outside leg by up
        // to cutSpreadDeg and turns the shoulders cutTwistShare of the lean ahead into the cut.
        public float turnSlowDegS, turnFastDegS, leanTauS, leanFallS, leanRefYdS2, leanMaxDeg, leanGamma, leanFeetShare, leanStepDeg;
        public float sideCapDeg, speedLeanDeg, plantFrom, cutSpreadDeg, cutTwistShare;
        // the lean shown moves at most leanRateDegS; a forward lean while still turning round stops at turnLeanCapDeg;
        // stood still, a figure turns to face its team's way at turnSettleDegS
        public float leanRateDegS, turnLeanCapDeg, turnSettleDegS;
        // FEATURES.idleMotion (View/Alive.cs): a man standing still breathes, his chest up idleBreathDeg and back over
        // idleBreathKeys key frames, and shifts his weight foot to foot over idleShiftKeys: hips idleSwayCm over, the
        // other hip idleRollDeg lower, that knee eased idleKneeDeg
        public int idleBreathKeys, idleShiftKeys;
        public float idleBreathDeg, idleSwayCm, idleRollDeg, idleKneeDeg;
        // FEATURES.headTrack (View/Alive.cs): a man's head turns up to trackHeadDeg toward the ball, tips up to trackUpDeg
        // to a ball in the air and down to trackDownDeg (in lookStepDeg steps)
        public float trackHeadDeg, trackUpDeg, trackDownDeg;
        // FEATURES.ballSpin (View/Alive.cs): a pass turns spiralDegPerKey about its long axis each key frame, a kick
        // tumbleDegPerKey end over end
        public float spiralDegPerKey, tumbleDegPerKey;
        // FEATURES.strideCycle (View/Stride.cs): a whole stride with every in-between, its phase moved by the distance
        // run. Each list is walk, jog, sprint (at strideWalkYdS and the players' jog and sprint speeds), blended by
        // speed: strideCadence steps a second; strideStance the share of a stride a foot is on the grass, its sweep under
        // the hip at most strideReach of the leg's length behind or ahead (strideAhead of the sweep ahead at touch-down);
        // the planted knee bent strideKneeLandDeg at touch-down, strideKneeDeg under load and strideKneePushDeg at
        // toe-off; in the swing the knee folds strideKneeFoldDeg and the thigh drives strideThighDriveDeg forward;
        // strideBobCm the hips' rise in the air; strideHeelCm and strideToeDeg the heel's rise and the foot's point at
        // push-off, strideHeelStrikeDeg the toes up landing; the arms swing strideArmFwdDeg to strideArmBackDeg, the
        // elbow bent strideElbowFrontDeg in front and strideElbowBackDeg behind; the shoulders turn strideTwistDeg.
        // Coming to a stop, the last stride pose eases into the standing one over strideSettleTicks.
        public float strideWalkYdS;
        public float[] strideCadence, strideStance, strideReach, strideAhead, strideKneeLandDeg, strideKneeDeg, strideKneePushDeg;
        public float[] strideKneeFoldDeg, strideThighDriveDeg, strideBobCm, strideHeelCm, strideToeDeg, strideHeelStrikeDeg;
        public float[] strideArmFwdDeg, strideArmBackDeg, strideElbowFrontDeg, strideElbowBackDeg, strideTwistDeg;
        public int strideSettleTicks;
        // FEATURES.footFlex: pushing off, the toes bend up strideToeBendShare of the foot's point (they stay flat on the
        // grass); in the swing the foot lifts its toes strideSwingToeUpDeg (walk, jog, sprint) to clear the grass.
        public float strideToeBendShare;
        public float[] strideSwingToeUpDeg;
        // FEATURES.handCurl: each finger bone's curl toward the palm (degrees: fingers, fingertips, thumb, thumb tip) with
        // the hand spread open (handCurlOpenDeg, - bends back), relaxed (handCurlRelaxedDeg) and shut on the ball
        // (handCurlShutDeg); a grip between them blends them.
        public float[] handCurlOpenDeg, handCurlRelaxedDeg, handCurlShutDeg;
        // FEATURES.spineChain (View/ModelSkin.cs): the model's spine bends as a curve, its bend over the hips shared out
        // along Spine, Chest and UpperChest by spineBendShare (forward and to the side) and spineTwistShare (turning about
        // the spine); the neck takes neckShare of the head's turn
        public float[] spineBendShare, spineTwistShare;
        public float neckShare;
        // FEATURES.armHang (View/ModelSkin.cs): a model's arms hanging at rest come in until each wrist is at most
        // armHangGap (figure heights) outside its hips (long arms on wide shoulders, the cornerback, stuck out far)
        public float armHangGap;
        // FEATURES.strideHips (View/Stride.cs): in a stride the pelvis turns strideHipYawDeg each way with the leg reaching
        // forward and dips strideHipRollDeg on the swinging side (walk, jog, sprint)
        public float[] strideHipYawDeg, strideHipRollDeg;
        // FEATURES.breakdown (View/PlayersView.cs): a defender closing on the ball carrier breaks down for the tackle,
        // from gatherFromYd away (none) to gatherToYd (all of it), not when he comes from more than gatherBehindCos behind
        // the carrier's way: planted knees gatherKneeDeg more bent, the chest gatherLeanDeg further over, the feet
        // gatherWideDeg wider, the arms up gatherArmDeg in front (gatherArmOutDeg out, elbows gatherForeDeg), short steps
        // (a stride for gatherGaitYdS at most)
        public float gatherFromYd, gatherToYd, gatherBehindCos, gatherKneeDeg, gatherLeanDeg, gatherWideDeg, gatherArmDeg, gatherArmOutDeg, gatherForeDeg, gatherGaitYdS;
        // FEATURES.catchPlay (View/Moves.cs): a man reaching for a ball more than oneHandYd out to his side of his run goes
        // for it one-handed; two men of both teams within catchPlay.contestYd + leapCloseYd of each other go up for it
        // together, a hop of leapS seconds and leapYd at the top
        public float leapS, leapYd, leapCloseYd, oneHandYd;
    }

    // WS-07 the lobby (FEATURES.lobby): each person picks an offense spot and a defense spot before kickoff. The game
    // waits on the lobby until a phone taps KICK OFF (holdUntilKickoff), a bot plays for a dropped phone until it comes
    // back (botCoversDropped), and a swap request waits swapAskS seconds for the other person's answer.
    // FEATURES.lobbyScreen: the TV's join screen (Boot/Lobby.cs). The game behind darkens by tint; the QR code is qrFrac
    // of the screen height and each team card's emblem emblemFrac; a card lists at most listRows people. A new team picked
    // there waits teamApplyS with no further press before the stadium is built again with it.
    public sealed class LobbyConfig
    {
        public bool holdUntilKickoff, botCoversDropped;
        public float swapAskS;
        public float tint, qrFrac, emblemFrac, teamApplyS;
        public float hotspotCardFrac;   // FEATURES.hotspotQr: the Wi-Fi card's width, against the join card's
        public int listRows;
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
        public float nearSidelinePadYd;   // FEATURES.nearSideline: grass kept on screen past the near sideline border (CameraRig)
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

    // FEATURES.crashLog: how many different errors one run writes to its crash file (a repeat of one already written
    // only counts).
    public sealed class CrashLogConfig
    {
        public int maxEntries;
    }
}
