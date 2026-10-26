using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Modes;

/// <summary>
/// Base capture (stage 5 design §3.2), in whole ticks so it is exact and the same everywhere: a tank earns 1 tick per
/// tick it counts, a point is 30 ticks, a base falls at 3000. Keyed by tank id; allocation-free after construction.
/// <para>
/// Each <see cref="Update"/>, in order: the roster follows the tanks passed in (a tank no longer there goes, with its
/// points and best; a new one starts at 0; a wreck holds 0); every tank that took damage (a hit or splash for more than
/// 0, from anyone) or was destroyed this tick drops to 0 and earns nothing this tick; then on each base, unless a live
/// tank of its own team has its hull centre in the circle, the live attackers in it count, at most
/// <see cref="MaxCounted"/>, lowest ids first, one tick each. A tank reset this tick takes no place among those three:
/// the next in line counts instead. A tank keeps its ticks when it leaves the circle, so a base's total does too.
/// </para>
/// </summary>
public sealed class BaseCapture
{
    /// <summary>One point a second.</summary>
    public const int TicksPerPoint = SimConstants.TickRate;
    public const int PointsToWin = 100;
    public const int TicksToWin = PointsToWin * TicksPerPoint;

    /// <summary>Attackers that count on one base at once, lowest ids first.</summary>
    public const int MaxCounted = 3;

    private struct Entry
    {
        public int Id;
        public byte Team;
        public int Ticks;      // 0…TicksToWin
        public byte Best;      // the most whole points held at once, 0…PointsToWin
        public bool Reset;     // took damage or died this tick: earns nothing
    }

    private readonly TeamBase _base0, _base1;

    // The roster, slot for slot with the last tanks passed in; _spare is the buffer the next roster is built in.
    private Entry[] _roster = new Entry[Simulation.MaxTanks];
    private Entry[] _spare = new Entry[Simulation.MaxTanks];
    private int _count;

    /// <param name="bases">One base per team (<see cref="TeamBase.Check"/>), not none; kept as given.</param>
    public BaseCapture(IReadOnlyList<TeamBase> bases)
    {
        Bases = TeamBase.Check(bases, nameof(bases));
        if (Bases.Count == 0) throw new ArgumentException("Base capture needs the two teams' bases.", nameof(bases));
        foreach (TeamBase b in Bases)
            if (b.Team == 0) _base0 = b; else _base1 = b;
    }

    public IReadOnlyList<TeamBase> Bases { get; }

    /// <summary>
    /// One tick, after <c>Simulation.Step</c>: <paramref name="tanks"/> is <c>Simulation.Tanks</c>, <paramref name="events"/>
    /// <c>Simulation.Events</c>.
    /// </summary>
    public void Update(ReadOnlySpan<TankState> tanks, ReadOnlySpan<GameEvent> events)
    {
        if (tanks.Length > Simulation.MaxTanks)
            throw new ArgumentException($"At most {Simulation.MaxTanks} tanks, got {tanks.Length}.", nameof(tanks));
        FollowRoster(tanks);
        ResetHurt(events);
        Earn(tanks, _base0);
        Earn(tanks, _base1);
        for (int i = 0; i < _count; i++)
        {
            ref Entry e = ref _roster[i];
            e.Best = Math.Max(e.Best, (byte)(e.Ticks / TicksPerPoint));
        }
    }

    /// <summary>The attackers' ticks on team <paramref name="baseTeam"/>'s base: the sum over the other team's tanks still in the battle, in the circle or not.</summary>
    public int TicksOn(byte baseTeam)
    {
        byte attackers = (byte)(1 - CheckTeam(baseTeam));
        int sum = 0;
        for (int i = 0; i < _count; i++)
            if (_roster[i].Team == attackers) sum += _roster[i].Ticks;
        return sum;
    }

    /// <summary>The base's whole points, at most 100: what the wire and the HUD show. 100 only once it has fallen.</summary>
    public byte Progress(byte baseTeam) => (byte)Math.Min(PointsToWin, TicksOn(baseTeam) / TicksPerPoint);

    public bool IsCaptured(byte baseTeam) => TicksOn(baseTeam) >= TicksToWin;

    /// <summary>The tank's own ticks; 0 for a tank not in the battle.</summary>
    public int TicksOf(int tankId)
    {
        int i = IndexOf(tankId);
        return i < 0 ? 0 : _roster[i].Ticks;
    }

    /// <summary>The most whole points the tank held at once since it came into the battle (0–100); 0 for an unknown tank.</summary>
    public byte BestOf(int tankId)
    {
        int i = IndexOf(tankId);
        return i < 0 ? (byte)0 : _roster[i].Best;
    }

    /// <summary>Rebuilds the roster in the order of <paramref name="tanks"/>, carrying over what the known tanks had.</summary>
    private void FollowRoster(ReadOnlySpan<TankState> tanks)
    {
        for (int i = 0; i < tanks.Length; i++)
        {
            int was = IndexOf(tanks[i].Id);
            Entry e = was < 0 ? new Entry { Id = tanks[i].Id } : _roster[was];
            e.Team = tanks[i].Team;
            e.Reset = false;
            if (tanks[i].IsDestroyed) e.Ticks = 0;   // a wreck never keeps points, even if its TankDestroyed never came
            _spare[i] = e;
        }
        (_roster, _spare) = (_spare, _roster);
        _count = tanks.Length;
    }

    /// <summary>
    /// Damage is known from the events alone: <see cref="GameEventType.TankHit"/> and <see cref="GameEventType.SplashDamage"/>
    /// for more than 0, and <see cref="GameEventType.TankDestroyed"/>. Any future way for a tank to lose health (ramming,
    /// a fall, …) must emit one of them with its damage, or attackers keep their points through it (a test pins the
    /// event kinds).
    /// </summary>
    private void ResetHurt(ReadOnlySpan<GameEvent> events)
    {
        foreach (ref readonly GameEvent ev in events)
        {
            bool hurt = ev.Type switch
            {
                GameEventType.TankHit or GameEventType.SplashDamage => ev.Damage > 0,
                GameEventType.TankDestroyed => true,
                _ => false,
            };
            if (!hurt) continue;
            int i = IndexOf(ev.TargetTankId);
            if (i < 0) continue;
            _roster[i].Ticks = 0;
            _roster[i].Reset = true;
        }
    }

    /// <summary>Roster slot i is tanks[i] (see <see cref="FollowRoster"/>).</summary>
    private void Earn(ReadOnlySpan<TankState> tanks, TeamBase b)
    {
        byte attackers = (byte)(1 - b.Team);
        float r2 = b.Radius * b.Radius;
        for (int i = 0; i < tanks.Length; i++)
            if (tanks[i].Team == b.Team && !tanks[i].IsDestroyed && Vector2.DistanceSquared(tanks[i].Position, b.Centre) <= r2)
                return;   // a live defender: nobody earns here this tick

        long last = long.MinValue;   // the ids picked so far are all ≤ this: pick the lowest above it
        for (int picked = 0; picked < MaxCounted; picked++)
        {
            int best = -1;
            for (int i = 0; i < tanks.Length; i++)
            {
                ref readonly TankState t = ref tanks[i];
                if (t.Team != attackers || t.IsDestroyed || _roster[i].Reset || t.Id <= last) continue;
                if (Vector2.DistanceSquared(t.Position, b.Centre) > r2) continue;
                if (best < 0 || t.Id < tanks[best].Id) best = i;
            }
            if (best < 0) return;
            last = tanks[best].Id;
            ref Entry e = ref _roster[best];
            e.Ticks = Math.Min(TicksToWin, e.Ticks + 1);
        }
    }

    private int IndexOf(int tankId)
    {
        for (int i = 0; i < _count; i++)
            if (_roster[i].Id == tankId) return i;
        return -1;
    }

    private static byte CheckTeam(byte baseTeam) => baseTeam <= 1
        ? baseTeam
        : throw new ArgumentOutOfRangeException(nameof(baseTeam), baseTeam, "Only teams 0 and 1 have a base.");
}
