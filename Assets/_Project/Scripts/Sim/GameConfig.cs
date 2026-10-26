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
        // Loaded from Config/plays/*.json (not game.json) by ConfigLoader.AddPlays, sorted by id.
        [Newtonsoft.Json.JsonIgnore] public List<PlayConfig> plays = new List<PlayConfig>();
        public BaselineConfig baseline = new BaselineConfig();
        public List<TeamConfig> teams = new List<TeamConfig>();
        public NetConfig net = new NetConfig();
        public ViewConfig view = new ViewConfig();
        public AnimConfig anim = new AnimConfig();
        public EventsConfig events = new EventsConfig();   // WS-00 (Events.cs)
        public ReplayConfig replay = new ReplayConfig();   // WS-08 (ReplayConfig.cs, View/Replay.cs)
        public StatsConfig stats = new StatsConfig();      // WS-12 (Stats.cs, View/PostGame.cs, View/Celebrations.cs)
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
        public List<float> readTimesS = new List<float>();
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
    }

    // The players' moves on the TV (View/Moves.cs, FEATURES.moves): key frames per second (stepped, no wobble), how
    // long each one-off move lasts, when a receiver puts his hands up (within reachS he'll be within reachYd of the
    // ball coming down at catching height), when the men on the grass get up (the last getUpS of the whistle), and
    // how often a blocker shoves.
    public sealed class AnimConfig
    {
        public float fps, throwS, handoffS, secureS, bobbleS, reachYd, reachS, getUpS, blockShoveS;
    }

    // The field runs along X (goal line to goal line), across Z (sideline to sideline); the centre is (0, 0).
    public sealed class FieldConfig
    {
        public float lengthYd, widthYd, endZoneYd, yardM;
        public float stripeYd, lineEveryYd, lineWidthYd, sidelineBorderYd;
        public float hashFromSidelineYd, hashLengthYd, surroundYd;
        public string grassColor, grassAltColor, lineColor, endZoneColor, surroundColor, skyColor;

        public float HalfLength => lengthYd * 0.5f;
        public float HalfWidth => widthYd * 0.5f;
        public float GoalLineX => HalfLength - endZoneYd;
    }

    public sealed class CameraConfig
    {
        public float visibleYards, fovDeg, pitchDeg, lookAtXYd, lookAtZYd;
        public float sideline;   // -1: the camera stands on the -Z sideline
        public float leadYd, followRate;   // how far ahead of the ball it looks, how quickly it catches up (per second)
    }
}
