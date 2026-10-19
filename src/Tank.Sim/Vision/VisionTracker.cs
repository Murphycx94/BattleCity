using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// Who can see whom (stage 3 design §2.3), kept beside a <see cref="Simulation"/>: call <see cref="Update"/> after every
/// <see cref="Simulation.Step"/>. On the first call, and then every <see cref="UpdateInterval"/>-th tick (5 Hz), it has
/// every live, watching tank look at every live enemy (<see cref="Spotting.Sees"/>) and notes, per target and team,
/// the last tick a tank of that team saw it. A tank stays visible to a team for <see cref="LingerTicks"/> after that; it
/// is always visible to its own team, and a wreck to everyone (each update notes it as seen by every team, so a tank
/// revived where it lay stays in view for the linger rather than vanishing until the next update; one that comes back
/// somewhere else — a respawn — starts unseen, or its new position would be given away). What it knows about a tank follows
/// the tank's id, so tanks removed from the battle (the slots after them move down, see <see cref="Simulation.RemoveTank"/>)
/// and tanks added later are taken in their stride. Nothing allocates after construction.
/// </summary>
public sealed class VisionTracker
{
    public const int UpdateInterval = 6;
    public const int LingerTicks = 2 * SimConstants.TickRate;
    public const int MaxTeams = SimConstants.MaxTeams;
    private const int NeverSeen = int.MinValue / 2;

    private readonly Simulation _sim;
    private readonly int[] _ids = new int[Simulation.MaxTanks];      // the tank in each slot at the last call; follows the layout
    private int[] _lastSeen = new int[Simulation.MaxTanks * MaxTeams];   // [slot * MaxTeams + team]
    private bool[] _blind = new bool[Simulation.MaxTanks];
    private bool[] _moving = new bool[Simulation.MaxTanks];
    private float[] _lastYaw = new float[Simulation.MaxTanks];
    private bool[] _wasWreck = new bool[Simulation.MaxTanks];       // destroyed at the last call
    private Vector2[] _wreckAt = new Vector2[Simulation.MaxTanks];  // where it lay
    private int[] _lastSeenScratch = new int[Simulation.MaxTanks * MaxTeams];   // the rows rebuilt when the layout changes
    private bool[] _blindScratch = new bool[Simulation.MaxTanks];
    private bool[] _movingScratch = new bool[Simulation.MaxTanks];
    private float[] _lastYawScratch = new float[Simulation.MaxTanks];
    private bool[] _wasWreckScratch = new bool[Simulation.MaxTanks];
    private Vector2[] _wreckAtScratch = new Vector2[Simulation.MaxTanks];
    private int _count;            // slots followed
    private int _lastUpdate = -1;  // the tick of the last update; -1 before the first

    public VisionTracker(Simulation sim)
    {
        _sim = sim;
        Array.Fill(_lastSeen, NeverSeen);
    }

    /// <summary>A tank that doesn't watch is never an observer (a practice dummy), though others still see it.</summary>
    public void SetWatching(int tankId, bool watching)
    {
        Sync(_sim.Tanks);
        _blind[SlotOf(tankId)] = !watching;
    }

    /// <summary>
    /// Call after every step: on the first call and every <see cref="UpdateInterval"/>-th tick, works out who sees whom.
    /// Another call on the same tick does nothing (it would take the hulls for still).
    /// </summary>
    public void Update()
    {
        ReadOnlySpan<TankState> tanks = _sim.Tanks;
        Sync(tanks);
        int tick = _sim.Tick;
        if (tick == _lastUpdate || (_lastUpdate >= 0 && tick % UpdateInterval != 0)) return;
        _lastUpdate = tick;
        for (int j = 0; j < tanks.Length; j++)
        {
            ref readonly TankState t = ref tanks[j];
            _moving[j] = MathF.Abs(t.Speed) > Spotting.MovingSpeed || MathF.Abs(Angles.Wrap(t.Yaw - _lastYaw[j])) > Spotting.TurningAngle;
            _lastYaw[j] = t.Yaw;
        }
        for (int j = 0; j < tanks.Length; j++)
        {
            ref readonly TankState target = ref tanks[j];
            if (target.IsDestroyed)
            {
                for (int team = 0; team < MaxTeams; team++) _lastSeen[j * MaxTeams + team] = tick;   // wrecks are seen by all
                continue;
            }
            for (int i = 0; i < tanks.Length; i++)
            {
                ref readonly TankState observer = ref tanks[i];
                if (observer.Team == target.Team || observer.IsDestroyed || _blind[i]) continue;
                int seen = j * MaxTeams + observer.Team;
                if (_lastSeen[seen] == tick) continue;   // a team mate already saw it this time
                if (Spotting.Sees(observer, target, _sim.Field, tick, _moving[j])) _lastSeen[seen] = tick;
            }
        }
    }

    /// <summary>Brings the rows into line with the simulation before anything reads or writes them.</summary>
    private void Sync(ReadOnlySpan<TankState> tanks)
    {
        FollowSlots(tanks);
        ForgetTheRespawned(tanks);
    }

    /// <summary>
    /// A wreck that is back on its tracks somewhere else than where it lay has respawned: nobody has seen it yet, and
    /// its new heading is where it starts from, not a turn.
    /// </summary>
    private void ForgetTheRespawned(ReadOnlySpan<TankState> tanks)
    {
        for (int j = 0; j < tanks.Length; j++)
        {
            ref readonly TankState t = ref tanks[j];
            if (_wasWreck[j] && !t.IsDestroyed && t.Position != _wreckAt[j])
            {
                Array.Fill(_lastSeen, NeverSeen, j * MaxTeams, MaxTeams);
                _lastYaw[j] = t.Yaw;
            }
            _wasWreck[j] = t.IsDestroyed;
            if (t.IsDestroyed) _wreckAt[j] = t.Position;
        }
    }

    /// <summary>Whether tank <paramref name="tankId"/> is visible to <paramref name="team"/> as of the last update.</summary>
    public bool IsVisible(int tankId, byte team)
    {
        if (team >= MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {MaxTeams - 1}.");
        Sync(_sim.Tanks);
        int slot = SlotOf(tankId);
        ref readonly TankState tank = ref _sim.Tanks[slot];
        if (tank.Team == team || tank.IsDestroyed) return true;
        return _sim.Tick - _lastSeen[slot * MaxTeams + team] <= LingerTicks;
    }

    /// <summary>
    /// What <paramref name="observerId"/> looking at <paramref name="targetId"/> would find right now, with the target
    /// moving or not as of the last update: the numbers behind the rule, for a debug display.
    /// </summary>
    public Sighting Inspect(int observerId, int targetId)
    {
        Sync(_sim.Tanks);
        int target = SlotOf(targetId);
        return Spotting.Look(_sim.Tanks[SlotOf(observerId)], _sim.Tanks[target], _sim.Field, _sim.Tick, _moving[target]);
    }

    /// <summary>
    /// Brings the per-slot rows into line with the simulation's slots. Nothing to do while the same tanks sit in the same
    /// slots; otherwise each tank's row is found by its id and moved to its new slot (a tank that is new gets a fresh
    /// row: unseen, watching, still). Swaps the row arrays with scratch ones rather than allocating.
    /// </summary>
    private void FollowSlots(ReadOnlySpan<TankState> tanks)
    {
        bool same = tanks.Length == _count;
        for (int i = 0; same && i < tanks.Length; i++) same = _ids[i] == tanks[i].Id;
        if (same) return;
        for (int i = 0; i < tanks.Length; i++)
        {
            int old = OldSlotOf(tanks[i].Id);
            if (old >= 0)
            {
                Array.Copy(_lastSeen, old * MaxTeams, _lastSeenScratch, i * MaxTeams, MaxTeams);
                _blindScratch[i] = _blind[old];
                _movingScratch[i] = _moving[old];
                _lastYawScratch[i] = _lastYaw[old];
                _wasWreckScratch[i] = _wasWreck[old];
                _wreckAtScratch[i] = _wreckAt[old];
            }
            else
            {
                Array.Fill(_lastSeenScratch, NeverSeen, i * MaxTeams, MaxTeams);
                _blindScratch[i] = false;
                _movingScratch[i] = false;
                _lastYawScratch[i] = tanks[i].Yaw;
                _wasWreckScratch[i] = tanks[i].IsDestroyed;
                _wreckAtScratch[i] = tanks[i].Position;
            }
        }
        (_lastSeen, _lastSeenScratch) = (_lastSeenScratch, _lastSeen);
        (_blind, _blindScratch) = (_blindScratch, _blind);
        (_moving, _movingScratch) = (_movingScratch, _moving);
        (_lastYaw, _lastYawScratch) = (_lastYawScratch, _lastYaw);
        (_wasWreck, _wasWreckScratch) = (_wasWreckScratch, _wasWreck);
        (_wreckAt, _wreckAtScratch) = (_wreckAtScratch, _wreckAt);
        for (int i = 0; i < tanks.Length; i++) _ids[i] = tanks[i].Id;
        _count = tanks.Length;
    }

    private int OldSlotOf(int tankId)
    {
        for (int i = 0; i < _count; i++)
            if (_ids[i] == tankId) return i;
        return -1;
    }

    private int SlotOf(int tankId)
    {
        int slot = _sim.SlotOf(tankId);
        return slot >= 0 ? slot : throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
    }
}
