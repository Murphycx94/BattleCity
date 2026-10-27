using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// The bots of one match (stage 5 design §4): it keeps the match's navigation grid in step with the battlefield, hears
/// the hits and revivals that concern its bots, shares one search budget between them (<see cref="PathService"/>) and
/// gives each bot its command every tick (<see cref="Bot"/>). In a standard battle the bots attack, defend, come back
/// when their base falls, retreat when badly hurt and all go forward once their team has no attacker left
/// (BotDirector.Roles.cs); outside one every bot wanders between the
/// map's waypoints along searched paths (design §4.2), retreating to a team mate when badly hurt.
/// </summary>
/// <remarks>
/// The server's tick: <see cref="Update"/> once, then <see cref="Command"/> for each bot's tank, then the step. Everything
/// is decided by the bots' own seeded random numbers and the simulation's state, bots in ascending tank id, the searches
/// served in tank-id order (design §4.4), so a match replays from its seeds. Neither <see cref="Update"/> nor
/// <see cref="Command"/> allocates once the bots are added; <see cref="Describe"/> and <see cref="Summary"/> do (they are
/// for the log at the start and the end). Not thread-safe: it belongs to the match's tick.
/// </remarks>
public sealed partial class BotDirector
{
    /// <summary>A lane's via point counts as passed this close, m.</summary>
    public const float ViaRadius = 12f;

    /// <summary>A wander waypoint counts as reached this close, m.</summary>
    public const float WaypointRadius = 6f;

    /// <summary>While driving, a bot asks for its path again this often (design §4.1: every 5 s).</summary>
    public const int ReplanTicks = 5 * SimConstants.TickRate;

    /// <summary>A driving bot checks this often that it is getting anywhere (design §4.3: 3 s).</summary>
    public const int StuckTicks = 3 * SimConstants.TickRate;

    /// <summary>Moved less than this in <see cref="StuckTicks"/> while driving …, m.</summary>
    public const float StuckDistance = 1f;

    /// <summary>… and turned less than this meanwhile: stuck, radians.</summary>
    public static readonly float StuckTurn = Angles.Deg(20);

    /// <summary>A stuck bot backs off this long, then asks for a new path (design §4.3: 1.5 s).</summary>
    public const int ReverseTicks = 45;

    /// <summary>Stuck this many times on one leg, a bot gives the leg up.</summary>
    public const int StuckRetries = 3;

    /// <summary>A bot fires only while slower than this (design §4.3: 2 km/h), m/s.</summary>
    public const float FireSpeed = 2f / 3.6f;

    // Standard battle (stage 5C task 7): retreat, falling back, capture points, defenders.

    /// <summary>Below this share of its health a bot pulls back.</summary>
    public const float RetreatHealth = 0.3f;

    /// <summary>A retreat ends this close to the team mate, m (or inside its own base).</summary>
    public const float RetreatArrive = 15f;

    /// <summary>Bots called back when the own base starts to fall.</summary>
    public const int FallBackCount = 2;

    /// <summary>The base score hasn't risen for this long (and no enemy is near): the bots called back return to their roles.</summary>
    public const int ReleaseTicks = 10 * SimConstants.TickRate;

    /// <summary>… no enemy the team sees within the base's radius plus this, m.</summary>
    public const float ReleaseClear = 30f;

    /// <summary>Capture and fall-back points lie within the base's radius less this of its centre, m.</summary>
    public const float CaptureMargin = 6f;

    /// <summary>A team's capture points at least this far apart, m (up to 8 draws).</summary>
    public const float CaptureSpacing = 8f;

    /// <summary>At most this many defenders a team.</summary>
    public const int MaxDefenders = 2;

    /// <summary>The points a bot's path buffer holds.</summary>
    public const int MaxPathPoints = PathFinder.MaxPoints;

    /// <summary>
    /// An attacker (or a bot called back) that gave its final leg up outside its circle holds this long with a new point
    /// drawn, then tries again (5C batch B review follow-up: 8 s, so the hold alone stays under the 10 s stuck limit).
    /// </summary>
    public const int RetryTicks = 8 * SimConstants.TickRate;

    // Team-mates (5C batch C review follow-up): a team-mate's tank shoved by a bot is a correction its player sees.

    /// <summary>
    /// A driving bot bends its way away from a live team-mate whose circle (half the hull's diagonal) would come within
    /// this of its own in the next <see cref="MateLookAhead"/> seconds, m: about 8 m between two mediums' centres.
    /// </summary>
    public const float MateGap = 1.5f;

    /// <summary>How far ahead a driving bot looks for team-mates it would come close to, s.</summary>
    public const float MateLookAhead = 1.5f;

    /// <summary>
    /// A bot that stops to fire doesn't stop with its circle within this of a live team-mate's (about 4 m between two
    /// hulls side by side), m: it rolls on or back along its hull first …
    /// </summary>
    public const float MateStopGap = 4f;

    /// <summary>… for at most this long, then stops wherever it is.</summary>
    public const int ShiftTicks = 3 * SimConstants.TickRate;

    private readonly List<Bot> _bots = new(Simulation.MaxTanks);
    private bool _assigned;
    private bool _started;     // Update has run
    private int _heardTick;    // the simulation tick whose events were read last

    /// <param name="grid">The match's own grid (<see cref="MapDefinition.NewNavGrid"/>), from the same map as <paramref name="sim"/>'s battlefield.</param>
    public BotDirector(Simulation sim, VisionTracker vision, MatchRules rules, MapDefinition map, NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(grid);
        (Sim, Vision, Rules, Map, Grid) = (sim, vision, rules, map, grid);
        Paths = new PathService(grid, sim.Field);   // grid and field bound together from here on
        Bots = new ReadOnlyCollection<Bot>(_bots);
        SetUpStandard();
    }

    /// <summary>The bots, in ascending tank id.</summary>
    public IReadOnlyList<Bot> Bots { get; }

    /// <summary>The match's navigation grid, kept in step with its battlefield by <see cref="Update"/>.</summary>
    public NavGrid Grid { get; }

    /// <summary>The match's searches.</summary>
    public PathService Paths { get; }

    internal Simulation Sim { get; }
    internal VisionTracker Vision { get; }
    internal MatchRules Rules { get; }
    internal MapDefinition Map { get; }

    /// <summary>
    /// Hands tank <paramref name="tankId"/> to a bot with its own random numbers from <paramref name="seed"/>; normally
    /// before <see cref="AssignRoles"/>. A bot added later gets its role at once by the same rules (BotDirector.Roles.cs).
    /// </summary>
    /// <exception cref="ArgumentException">The tank isn't in the simulation, or has a bot already.</exception>
    public Bot Add(int tankId, uint seed)
    {
        if (Sim.SlotOf(tankId) < 0) throw new ArgumentException($"No tank {tankId} in the battle.", nameof(tankId));
        if (Find(tankId) is not null) throw new ArgumentException($"Tank {tankId} has a bot already.", nameof(tankId));
        Paths.Register(tankId);
        var bot = new Bot(this, tankId, seed);
        int at = 0;
        while (at < _bots.Count && _bots[at].TankId < tankId) at++;
        _bots.Insert(at, bot);
        if (_assigned) GiveLateRole(bot);
        return bot;
    }

    /// <summary>
    /// Gives every bot its role, once, after the last <see cref="Add"/> (<see cref="Update"/> does it if nobody has; later
    /// calls do nothing): in a standard battle attack or defend (see BotDirector.Roles.cs), otherwise wander between the
    /// map's waypoints (design §4.2).
    /// </summary>
    public void AssignRoles()
    {
        if (_assigned) return;
        _assigned = true;
        GiveOutRoles();
    }

    /// <summary>The bot of tank <paramref name="tankId"/>, or null.</summary>
    public Bot? Find(int tankId)
    {
        for (int b = 0; b < _bots.Count; b++)
            if (_bots[b].TankId == tankId) return _bots[b];
        return null;
    }

    /// <summary>
    /// Tests: sends <paramref name="bot"/> along an attack lane: its <paramref name="via"/> points in order, then to
    /// <paramref name="end"/> (in a standard battle, inside the enemy base) to hold there.
    /// </summary>
    internal void Attack(Bot bot, int lane, IReadOnlyList<Vector2> via, Vector2 end)
    {
        byte team = TeamOf(bot);
        bot.Attack(lane, via, end, _standard && team <= 1 ? _bases[1 - team] : default);
    }

    /// <summary>
    /// Once a tick, before the commands (a second call in the same tick does nothing). The step's events: props that
    /// fell reach the grid (all props down are re-read when ticks went unheard, and on the first call), hits on a bot
    /// reach its target picker, a revived bot starts its leg afresh. Then the roles if nobody gave them out, the team
    /// watch (a standard battle: calling bots back to a falling base and letting them go, sending a team with no attacker
    /// left forward), the tanks for the searches to
    /// steer round (wrecks, and tanks standing still: <see cref="PathService.Observe"/>) and the searches' budget for
    /// the tick.
    /// </summary>
    public void Update()
    {
        int tick = Sim.Tick;
        if (_started && tick == _heardTick) return;
        if (!_started) Paths.Sync();   // props down before the bots came
        else if (tick == _heardTick + 1)
        {
            Paths.Refresh(Sim.Events);
            Hear();
        }
        else
        {
            Paths.Sync();
            for (int b = 0; b < _bots.Count; b++) _bots[b].CatchUp(Sim);
            Hear();
        }
        (_started, _heardTick) = (true, tick);
        AssignRoles();
        if (_standard) WatchTeams(tick);
        Paths.Observe(Sim.Tanks, tick);
        Paths.Run();
    }

    /// <summary>The hits and revivals in the last step's events.</summary>
    private void Hear()
    {
        foreach (ref readonly GameEvent e in Sim.Events)
        {
            if (e.Type is GameEventType.TankHit or GameEventType.SplashDamage) Find(e.TargetTankId)?.Picker.NoteHit(e.TankId, e.Tick);
            else if (e.Type == GameEventType.TankRevived && Find(e.TargetTankId) is { } bot && IsAlive(bot, out Vector2 at)) bot.Revived(at);
        }
    }

    /// <summary>
    /// The command for bot tank <paramref name="tankId"/> at <paramref name="tick"/> (the tick being commanded:
    /// <c>Sim.Tick + 1</c>), after this tick's <see cref="Update"/> (asserted in Debug). A tank gone from the battle, or a
    /// wreck, holds still.
    /// </summary>
    /// <exception cref="ArgumentException">No bot drives that tank.</exception>
    public InputCmd Command(int tankId, int tick)
    {
        Debug.Assert(_started && _heardTick == Sim.Tick, $"Command at simulation tick {Sim.Tick} without Update this tick (last at {(_started ? _heardTick : -1)}).");
        Bot bot = Find(tankId) ?? throw new ArgumentException($"Tank {tankId} has no bot.", nameof(tankId));
        AssignRoles();
        return bot.Command(tick);
    }

    /// <summary>
    /// The bots' roles, for the log at the start of the match (allocates): "team 0 — 3 attack (north 1, middle 1, south 1),
    /// 1 defend; team 1 — no attackers, 2 defend" in a standard battle (a team without bots left out), "4 wander" otherwise.
    /// </summary>
    public string Describe()
    {
        if (_standard && _assigned) return DescribeStandard();
        int wander = 0, attack = 0, defend = 0;
        foreach (Bot bot in _bots)
        {
            switch (bot.Role)
            {
                case BotRole.Wander: wander++; break;
                case BotRole.Attack: attack++; break;
                case BotRole.Defend: defend++; break;
            }
        }
        var parts = new List<string>(3);
        if (wander > 0) parts.Add($"{wander} wander");
        if (attack > 0) parts.Add($"{attack} attack");
        if (defend > 0) parts.Add($"{defend} defend");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>
    /// What the bots did, for the log at the end of the match (allocates): "team 0 fell back 2 times, team 1 0 times;
    /// 3 stucks; 412 paths, 2 not found" (the fall-backs in a standard battle only, of the teams that have bots).
    /// </summary>
    public string Summary()
    {
        int stucks = 0;
        foreach (Bot bot in _bots) stucks += bot.Stucks;
        return $"{(_standard ? FallBacksText() : "")}{stucks} stucks; {Paths.Searches} paths, {Paths.Failures} not found";
    }
}
