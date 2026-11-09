using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// A match for bots to play, stepped as the server steps one (tasks 6, 7 and 9 of the stage 5C plan): every tick
/// <see cref="BotDirector.Update"/>, then a command for each tank in slot order (the bot's, or the order given to any
/// other tank: all zeros unless <see cref="Order"/> says otherwise), the step, the vision and the rules. Played on the
/// flat 400 m arena (<see cref="FlatMap"/>) or a map from the catalog. It keeps the events, each tank's last command, and
/// for each bot how long it has been getting nowhere (<see cref="LongestStuckSeconds"/>) by the bot's own stuck rule:
/// trying to move, or holding short of where its role ends, without moving or turning.
/// </summary>
internal sealed class BotArena
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>A defender holding farther than this from its cover point (or its base centre, having given every cover point up) is short of it, m.</summary>
    public const float CoverReach = 4f;

    public const float Border = 10f;
    public const float BaseRadius = 25f;
    public static readonly Vector2 Centre0 = new(90, 200), Centre1 = new(310, 200);
    public static readonly Vector2 Base0 = new(45, 200), Base1 = new(355, 200);

    public static readonly float East = Angles.YawOf(new Vector2(1, 0));
    public static readonly float West = Angles.YawOf(new Vector2(-1, 0));
    public static readonly float North = Angles.YawOf(new Vector2(0, -1));
    public static readonly float South = Angles.YawOf(new Vector2(0, 1));

    /// <summary>The flat arena's waypoints: a 3 × 3 grid, x and z in {100, 200, 300}.</summary>
    public static readonly IReadOnlyList<Vector2> Waypoints = new Vector2[]
    {
        new(100, 100), new(200, 100), new(300, 100),
        new(100, 200), new(200, 200), new(300, 200),
        new(100, 300), new(200, 300), new(300, 300),
    };

    public static readonly IReadOnlyList<TeamBase> Bases = new TeamBase[] { new(0, Base0, BaseRadius), new(1, Base1, BaseRadius) };

    /// <summary>Three lanes, one via point each (0 north, 1 middle, 2 south), and three cover points a team in front of its base.</summary>
    public static readonly BotMapData Bots = new(
        new BotLane[]
        {
            new("north", new Vector2[] { new(200, 120) }),
            new("middle", new Vector2[] { new(200, 200) }),
            new("south", new Vector2[] { new(200, 280) }),
        },
        new Vector2[] { new(85, 160), new(85, 240), new(115, 200) },
        new Vector2[] { new(315, 160), new(315, 240), new(285, 200) });

    /// <summary>Five spawn points a team round its centre, facing the other team; the ten of them for a free-for-all.</summary>
    public static readonly SpawnLayout Spawns = MakeSpawns();

    private readonly InputCmd[] _inputs = new InputCmd[Simulation.MaxTanks];
    private readonly InputCmd[] _orders = new InputCmd[Simulation.MaxTanks + 1];   // by tank id (ids start at 1)
    private readonly int[] _anchorTick = new int[Simulation.MaxTanks + 1];
    private readonly Vector2[] _anchor = new Vector2[Simulation.MaxTanks + 1];
    private readonly float[] _anchorYaw = new float[Simulation.MaxTanks + 1];
    private readonly int[] _longestStuck = new int[Simulation.MaxTanks + 1];
    private byte _nextSlot;

    /// <summary>
    /// A match of <paramref name="mode"/> on <paramref name="map"/> (the flat arena when null), with the match seed
    /// <paramref name="seed"/>, a royale's rings <paramref name="zone"/> and its crates <paramref name="crates"/> (all for
    /// <see cref="MatchRules"/>).
    /// </summary>
    public BotArena(BattleMode mode = BattleMode.TeamBattle, MapDefinition? map = null, int? durationTicks = null, uint seed = 0,
        IReadOnlyList<ZoneRing>? zone = null, IReadOnlyList<Crate>? crates = null)
        : this(map ?? FlatMap(), null, null, mode, durationTicks, seed, zone, crates)
    {
    }

    /// <summary>The match on <paramref name="field"/> and <paramref name="grid"/> when given (made from the map otherwise); the grid first, as the server has it.</summary>
    private BotArena(MapDefinition map, Battlefield? field, NavGrid? grid, BattleMode mode, int? durationTicks, uint seed, IReadOnlyList<ZoneRing>? zone,
        IReadOnlyList<Crate>? crates)
    {
        Map = map;
        grid ??= map.NewNavGrid();
        Sim = new Simulation(field ?? map.NewBattlefield());
        Vision = new VisionTracker(Sim);
        Rules = new MatchRules(mode, Sim, map.Spawns, durationTicks, map.Bases, seed, zone, crates);
        Director = new BotDirector(Sim, Vision, Rules, map, grid);
    }

    /// <summary>
    /// A royale on <paramref name="map"/> as the server starts one (stage 6A plan, task 6; crates from 6B's task 6): the
    /// battlefield and the grid, the rings drawn on them from <paramref name="seed"/> (<see cref="ZonePicker"/>, with its
    /// salt) and the crates placed (<see cref="CratePlacer"/>, with its salt) before any tank, then the rules with the seed,
    /// the rings and the crates, and the director on the same grid.
    /// </summary>
    public static BotArena Royale(MapDefinition map, uint seed) => Zoned(map, seed, BattleMode.Royale);

    /// <summary>Duos on <paramref name="map"/> as the server starts one (stage 6C plan, task 5): as <see cref="Royale"/>, the mode duos.</summary>
    public static BotArena Duos(MapDefinition map, uint seed) => Zoned(map, seed, BattleMode.Duos);

    private static BotArena Zoned(MapDefinition map, uint seed, BattleMode mode)
    {
        Battlefield field = map.NewBattlefield();
        NavGrid grid = map.NewNavGrid();
        ZoneRing[] rings = ZonePicker.Pick(field, grid, map.Spawns.Solo, seed ^ ZonePicker.SeedSalt);
        Crate[] crates = CratePlacer.Place(field, grid, map.Spawns.Solo, seed ^ CratePlacer.SeedSalt);
        return new BotArena(map, field, grid, mode, null, seed, rings, crates);
    }

    public MapDefinition Map { get; }
    public Simulation Sim { get; }
    public VisionTracker Vision { get; }
    public MatchRules Rules { get; }
    public BotDirector Director { get; }

    /// <summary>Every event so far, unless <see cref="Recording"/> is off.</summary>
    public List<GameEvent> Events { get; } = new(4096);

    /// <summary>Whether <see cref="Events"/> and the stuck watch are kept (off for allocation tests).</summary>
    public bool Recording { get; set; } = true;

    /// <summary>When set, each tick's <see cref="VisionTracker.Update"/> wall-clock time is added to it, ms (stage 6A plan, task 6: the cost of 16 tanks).</summary>
    public List<double>? VisionTimes { get; set; }

    /// <summary>
    /// The flat 400 m arena (201 × 201 samples 2 m apart, a 10 m border), its spawns round (90, 200) and (310, 200), its
    /// bases at (45, 200) and (355, 200), its lanes and cover points, and the 3 × 3 waypoints; or other ground, props,
    /// waypoints, or no bot data.
    /// </summary>
    public static MapDefinition FlatMap(HeightField? terrain = null, IReadOnlyList<Prop>? props = null,
        IReadOnlyList<Vector2>? waypoints = null, bool botData = true)
    {
        HeightField ground = terrain ?? Ground400.Flat();
        Prop[] all = props?.ToArray() ?? Array.Empty<Prop>();
        return new MapDefinition("arena", "arena", () => new Battlefield(ground, all, Border), Spawns, waypoints ?? Waypoints, Bases,
            botData ? Bots : null);
    }

    /// <summary>A player's tank on <paramref name="team"/>'s spawns, as the server puts one in (its commands all zero until ordered).</summary>
    public int Join(TankSpec spec, byte team) => Rules.Join(_nextSlot++, spec, team);

    /// <summary>A bot: joined like a player, then handed to the director, as the server does.</summary>
    public Bot Bot(TankSpec spec, byte team, uint seed) => Director.Add(Join(spec, team), seed);

    /// <summary>A bot put at <paramref name="at"/> facing <paramref name="yaw"/>, unprotected.</summary>
    public Bot Bot(TankSpec spec, byte team, uint seed, Vector2 at, float yaw)
    {
        Bot bot = Bot(spec, team, seed);
        Place(bot.TankId, at, yaw);
        return bot;
    }

    /// <summary>A tank nobody drives, put at <paramref name="at"/> facing <paramref name="yaw"/> (east when null), unprotected.</summary>
    public int Idle(TankSpec spec, byte team, Vector2 at, float? yaw = null)
    {
        int id = Join(spec, team);
        Place(id, at, yaw ?? East);
        return id;
    }

    /// <summary>Moves a tank, turret and gun straight ahead, and takes its spawn protection off.</summary>
    public void Place(int id, Vector2 at, float yaw)
    {
        ref TankState t = ref Sim.TankRef(id);
        t.Position = at;
        t.Yaw = yaw;
        t.TurretYaw = 0f;
        t.ProtectedUntil = 0;
        (_anchor[id], _anchorYaw[id], _anchorTick[id]) = (at, yaw, Sim.Tick);
    }

    /// <summary>The command a tank that isn't a bot gets every tick from now on (its tick filled in).</summary>
    public void Order(int id, InputCmd cmd) => _orders[id] = cmd;

    public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

    /// <summary>The command tank <paramref name="id"/> got for the last tick played.</summary>
    public InputCmd LastInput(int id) => _inputs[Sim.SlotOf(id)];

    /// <summary>
    /// The longest a bot has spent getting nowhere, s: waiting for a path, driving or backing off, or holding short of where
    /// its role ends (<see cref="AtRoleEnd"/>), without moving <see cref="BotDirector.StuckDistance"/> from one spot or
    /// turning <see cref="BotDirector.StuckTurn"/> from one heading: the bot's own stuck rule (5C stalemate fix; it was
    /// 2 m and no turn, which counted turning round on the spot, about 7 s for a heavy, as stuck), measured since the last
    /// progress rather than over the bot's 3 s windows.
    /// </summary>
    public float LongestStuckSeconds(int botId) => _longestStuck[botId] / (float)Tps;

    /// <summary>
    /// Whether a holding bot is where its role ends: falling back, inside its own base; an attacker, inside the enemy base;
    /// a defender, within <see cref="CoverReach"/> of its cover point (of its base centre when it gave every cover point
    /// up). A retreating or wandering bot holds wherever it is meant to; so does any bot on a map without bases.
    /// </summary>
    public bool AtRoleEnd(Bot bot)
    {
        TankState t = Tank(bot.TankId);
        if (bot.Retreating || Map.Bases.Count == 0) return true;
        if (bot.FallingBack) return InBase(t.Position, t.Team);
        return bot.Role switch
        {
            BotRole.Attack => InBase(t.Position, (byte)(1 - t.Team)),
            BotRole.Defend => Vector2.Distance(t.Position, bot.Cover >= 0 ? Director.CoverOf(t.Team)[bot.Cover] : BaseOf(t.Team).Centre) <= CoverReach,
            _ => true,
        };
    }

    /// <summary>
    /// The live attackers (not called back, not retreating) that are neither inside the enemy base nor engaging, each as
    /// "tank N at (x, z), d m from the enemy base, activity": for a whole match's end (stage 5C task 9). Allocates.
    /// </summary>
    public List<string> AttackersShortOfTheEnemyBase()
    {
        var shortOf = new List<string>();
        foreach (Bot bot in Director.Bots)
        {
            int slot = Sim.SlotOf(bot.TankId);
            if (bot.Role != BotRole.Attack || slot < 0 || bot.FallingBack || bot.Retreating) continue;
            TankState t = Sim.Tanks[slot];
            if (t.IsDestroyed || bot.Activity == BotActivity.Engaging) continue;
            TeamBase enemy = BaseOf((byte)(1 - t.Team));
            if (InBase(t.Position, enemy.Team)) continue;
            shortOf.Add($"tank {bot.TankId} at {t.Position}, {Vector2.Distance(t.Position, enemy.Centre):F0} m from the enemy base, {bot.Activity}");
        }
        return shortOf;
    }

    private TeamBase BaseOf(byte team) => Map.Bases.Single(b => b.Team == team);

    private bool InBase(Vector2 p, byte team)
    {
        TeamBase b = BaseOf(team);
        return Vector2.Distance(p, b.Centre) <= b.Radius;
    }

    /// <summary>Plays <paramref name="ticks"/> ticks, calling <paramref name="each"/> with the tick after each.</summary>
    public void Run(int ticks, Action<int>? each = null)
    {
        for (int k = 0; k < ticks; k++)
        {
            Tick();
            each?.Invoke(Sim.Tick);
        }
    }

    /// <summary>One tick as the server plays it. Allocation-free with <see cref="Recording"/> off.</summary>
    public void Tick()
    {
        Director.Update();
        int tick = Sim.Tick + 1, n = Sim.TankCount;
        for (int k = 0; k < n; k++)
        {
            int id = Sim.Tanks[k].Id;
            _inputs[k] = Director.Find(id) is not null ? Director.Command(id, tick) : _orders[id] with { Tick = tick };
        }
        Sim.Step(_inputs.AsSpan(0, n));
        if (VisionTimes is { } times)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Vision.Update();
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        else Vision.Update();
        Rules.Update();
        if (!Recording) return;
        foreach (GameEvent e in Sim.Events) Events.Add(e);
        WatchStuck();
    }

    private void WatchStuck()
    {
        IReadOnlyList<Bot> bots = Director.Bots;
        for (int b = 0; b < bots.Count; b++)
        {
            Bot bot = bots[b];
            int id = bot.TankId, slot = Sim.SlotOf(id);
            if (slot < 0) continue;
            ref readonly TankState t = ref Sim.Tanks[slot];
            bool trying = !t.IsDestroyed && (bot.Activity is BotActivity.Waiting or BotActivity.Driving or BotActivity.Reversing ||
                                             (bot.Activity == BotActivity.Holding && !AtRoleEnd(bot)));
            if (!trying || Vector2.Distance(t.Position, _anchor[id]) >= BotDirector.StuckDistance ||
                MathF.Abs(Angles.Wrap(t.Yaw - _anchorYaw[id])) >= BotDirector.StuckTurn)
            {
                (_anchor[id], _anchorYaw[id], _anchorTick[id]) = (t.Position, t.Yaw, Sim.Tick);
                continue;
            }
            _longestStuck[id] = Math.Max(_longestStuck[id], Sim.Tick - _anchorTick[id]);
        }
    }

    private static SpawnLayout MakeSpawns()
    {
        Vector2[] offsets = { new(0, 0), new(0, -16), new(0, 16), new(-12, -8), new(-12, 8) };
        var team = new SpawnPoint[10];
        for (int i = 0; i < 5; i++)
        {
            team[i] = new SpawnPoint(Centre0 + offsets[i], East, 0);
            team[5 + i] = new SpawnPoint(Centre1 - offsets[i], West, 1);
        }
        var solo = new SpawnPoint[10];
        for (int i = 0; i < 10; i++) solo[i] = team[i] with { Team = 0 };
        return new SpawnLayout(team, solo);
    }
}
