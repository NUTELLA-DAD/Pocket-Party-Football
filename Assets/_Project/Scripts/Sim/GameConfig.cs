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
        // Loaded from Config/plays/*.json (not game.json) by ConfigLoader.AddPlays, sorted by id.
        [Newtonsoft.Json.JsonIgnore] public List<PlayConfig> plays = new List<PlayConfig>();
        public BaselineConfig baseline = new BaselineConfig();
        public List<TeamConfig> teams = new List<TeamConfig>();
        public NetConfig net = new NetConfig();
        public ViewConfig view = new ViewConfig();
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

    // One play (docs/PLAYS_FORMAT.md): 11 jobs in formation spot order.
    public sealed class PlayConfig
    {
        public string id, name, side, desc;
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
    }
}
