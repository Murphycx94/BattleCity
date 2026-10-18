using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// Who can see whom (stage 3 design §2.3), kept beside a <see cref="Simulation"/>: call <see cref="Update"/> after every
/// <see cref="Simulation.Step"/>. On the first call, and then every <see cref="UpdateInterval"/>-th tick (5 Hz), it has
/// every live, watching tank look at every live enemy (<see cref="Spotting.Sees"/>) and notes, per target and team,
/// the last tick a tank of that team saw it. A tank stays visible to a team for <see cref="LingerTicks"/> after that; it
/// is always visible to its own team, and a wreck to everyone (each update notes it as seen by every team, so a revived
/// tank stays in view for the linger rather than vanishing until the next update). Tanks are kept by slot in <see cref="Simulation.Tanks"/>
/// (slots never change: tanks are only ever added). Nothing allocates after construction.
/// </summary>
public sealed class VisionTracker
{
    public const int UpdateInterval = 6;
    public const int LingerTicks = 2 * SimConstants.TickRate;
    public const int MaxTeams = SimConstants.MaxTeams;
    private const int NeverSeen = int.MinValue / 2;

    private readonly Simulation _sim;
    private readonly int[] _lastSeen = new int[Simulation.MaxTanks * MaxTeams];   // [slot * MaxTeams + team]
    private readonly bool[] _blind = new bool[Simulation.MaxTanks];
    private readonly bool[] _moving = new bool[Simulation.MaxTanks];
    private readonly float[] _lastYaw = new float[Simulation.MaxTanks];
    private int _known;            // slots whose last yaw has been noted
    private int _lastUpdate = -1;  // the tick of the last update; -1 before the first

    public VisionTracker(Simulation sim)
    {
        _sim = sim;
        Array.Fill(_lastSeen, NeverSeen);
    }

    /// <summary>A tank that doesn't watch is never an observer (a practice dummy), though others still see it.</summary>
    public void SetWatching(int tankId, bool watching) => _blind[SlotOf(tankId)] = !watching;

    /// <summary>
    /// Call after every step: on the first call and every <see cref="UpdateInterval"/>-th tick, works out who sees whom.
    /// Another call on the same tick does nothing (it would take the hulls for still).
    /// </summary>
    public void Update()
    {
        ReadOnlySpan<TankState> tanks = _sim.Tanks;
        for (; _known < tanks.Length; _known++) _lastYaw[_known] = tanks[_known].Yaw;
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

    /// <summary>Whether tank <paramref name="tankId"/> is visible to <paramref name="team"/> as of the last update.</summary>
    public bool IsVisible(int tankId, byte team)
    {
        if (team >= MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {MaxTeams - 1}.");
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
        int target = SlotOf(targetId);
        return Spotting.Look(_sim.Tanks[SlotOf(observerId)], _sim.Tanks[target], _sim.Field, _sim.Tick, _moving[target]);
    }

    private int SlotOf(int tankId)
    {
        ReadOnlySpan<TankState> tanks = _sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
            if (tanks[i].Id == tankId) return i;
        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
    }
}
