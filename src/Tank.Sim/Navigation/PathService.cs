using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Navigation;

/// <summary>
/// The match's searches (stage 5 design §4.1): one PathFinder shared by every bot, ExpansionsPerTick expansions a tick
/// between them, served round-robin in tank-id order (after the last one served, wrapping), at most one search finished a
/// tick. Each registered tank has one request slot; a new request replaces its pending one. The service holds the
/// match's grid and battlefield together: the match's props reach the grid through <see cref="Refresh"/> and
/// <see cref="Sync"/>, never through a battlefield passed in.
/// </summary>
/// <remarks>
/// <para>A call of <see cref="Run"/> works on one search: it goes on with the search under way, or begins the first
/// pending request after the tank served last (a request whose end has no open cell near it fails there and then, spends
/// nothing, and the next one is begun), and continues it with the whole budget. If the search finishes, its result goes
/// into the tank's slot and the call ends, whatever budget is left, so tracing and smoothing a path happen at most once a
/// call. The finder never expands more than it is given, so a call never spends more than its budget; a search carries
/// on over grid changes (<see cref="PathFinder"/>), so one that needs more than a call's budget still finishes, within
/// <see cref="PathFinder.MaxExpansions"/> in all.</para>
/// <para>A tank that asks again while its search is under way gives that search up and goes to the back of the round
/// (as if just served): asking every tick can't hold the finder.</para>
/// <para>What the grid can't see (5C batch B review follow-up): every <see cref="Observe"/> notes the battle's tanks, and
/// each search, as it begins, is given the wrecks and the live tanks that have stood still for <see cref="StillTicks"/>
/// (within <see cref="StillDistance"/> of one spot), the asking tank left out, as <see cref="NavDisc"/>s it steers round
/// (<see cref="PathFinder"/>). They hold for the whole search, so a search still carries on over a changing battle.</para>
/// <para>Slots are fixed arrays in ascending tank id, filled by <see cref="Register"/>; after that nothing allocates.
/// Not thread-safe: it belongs to the match's tick.</para>
/// </remarks>
public sealed class PathService
{
    /// <summary>Expansions a tick shared by every bot's searches: about 0.3 ms on the riverland.</summary>
    public const int ExpansionsPerTick = 3000;

    /// <summary>A live tank that has stood still this long is in the way: a disc for the others' searches (3 s).</summary>
    public const int StillTicks = 3 * SimConstants.TickRate;

    /// <summary>A tank that has moved this far from where it was last still counts as moving again, m.</summary>
    public const float StillDistance = 1f;

    private const int None = -1;

    private readonly NavGrid _grid;
    private readonly Battlefield _field;
    private readonly PathFinder _finder;

    // The tanks observed last, in the order given (the battle's slots): each one's id, where it last started standing
    // still and since when; the two arrays of each kind take turns, so a tank keeps its watch when the slots move.
    private readonly int[][] _watchId = { new int[Simulation.MaxTanks], new int[Simulation.MaxTanks] };
    private readonly Vector2[][] _stillAt = { new Vector2[Simulation.MaxTanks], new Vector2[Simulation.MaxTanks] };
    private readonly int[][] _stillSince = { new int[Simulation.MaxTanks], new int[Simulation.MaxTanks] };
    private int _watchCount, _watch;   // _watch: the arrays in use

    // The obstacles as of the last Observe: whose they are and their discs; and one search's discs.
    private readonly int[] _obstacleId = new int[Simulation.MaxTanks];
    private readonly NavDisc[] _obstacles = new NavDisc[Simulation.MaxTanks];
    private readonly NavDisc[] _discs = new NavDisc[Simulation.MaxTanks];
    private int _obstacleCount;

    // Per slot, in ascending tank id.
    private readonly int[] _ids = new int[Simulation.MaxTanks];
    private readonly Vector2[] _from = new Vector2[Simulation.MaxTanks];
    private readonly Vector2[] _to = new Vector2[Simulation.MaxTanks];
    private readonly PathStatus[] _status = new PathStatus[Simulation.MaxTanks];
    private readonly int[] _pointCount = new int[Simulation.MaxTanks];
    private readonly Vector2[] _points = new Vector2[Simulation.MaxTanks * PathFinder.MaxPoints];   // slot s: s × MaxPoints …

    private int _count;
    private int _activeId = None;    // the tank whose search the finder holds (begun, pending), or None
    private int _lastServedId = None;

    /// <param name="grid">The match's own grid (a <see cref="NavGrid.Copy"/>), in step with <paramref name="field"/>.</param>
    /// <param name="field">The match's battlefield.</param>
    public PathService(NavGrid grid, Battlefield field)
    {
        _finder = new PathFinder(grid, field);
        (_grid, _field) = (grid, field);
    }

    /// <summary>
    /// Brings the grid in step with the battlefield's props after one step's <paramref name="events"/>
    /// (<see cref="NavGrid.Refresh"/>): the cells round every prop a PropDestroyed event names. Once a tick, before
    /// <see cref="Run"/>. A search under way carries on. Allocation-free. Returns how many cells changed.
    /// </summary>
    /// <exception cref="ArgumentException">An event names a prop the battlefield does not have.</exception>
    public int Refresh(ReadOnlySpan<GameEvent> events) => _grid.Refresh(_field, events);

    /// <summary>
    /// Catches the grid up with every prop that is down now, events or not (<see cref="NavGrid.Sync"/>): at the start of
    /// a match and after ticks whose events were missed. Allocation-free. Returns how many cells changed.
    /// </summary>
    public int Sync() => _grid.Sync(_field);

    /// <summary>
    /// Notes the battle's <paramref name="tanks"/> at <paramref name="tick"/>, once a tick before <see cref="Run"/> (and
    /// before the first): which are wrecks, and which live ones have stood still for <see cref="StillTicks"/>: the
    /// obstacles the searches begun from now on steer round. A tank first seen, or moved <see cref="StillDistance"/> or
    /// more from where it last stood, starts standing still now; one no longer in <paramref name="tanks"/> is forgotten.
    /// Allocation-free.
    /// </summary>
    /// <exception cref="ArgumentException">More than <see cref="Simulation.MaxTanks"/> tanks.</exception>
    public void Observe(ReadOnlySpan<TankState> tanks, int tick)
    {
        if (tanks.Length > Simulation.MaxTanks) throw new ArgumentException($"At most {Simulation.MaxTanks} tanks.", nameof(tanks));
        int old = _watch, now = 1 - _watch;
        int[] oldId = _watchId[old], id = _watchId[now];
        Vector2[] oldAt = _stillAt[old], at = _stillAt[now];
        int[] oldSince = _stillSince[old], since = _stillSince[now];
        _obstacleCount = 0;
        for (int k = 0; k < tanks.Length; k++)
        {
            ref readonly TankState t = ref tanks[k];
            int was = Watched(oldId, t.Id, k);
            (id[k], at[k], since[k]) = (t.Id, t.Position, tick);
            if (was >= 0 && Vector2.Distance(t.Position, oldAt[was]) < StillDistance) (at[k], since[k]) = (oldAt[was], oldSince[was]);
            if (t.IsDestroyed || tick - since[k] >= StillTicks)
                (_obstacleId[_obstacleCount], _obstacles[_obstacleCount++]) = (t.Id, new NavDisc(t.Position, NavDisc.RadiusOf(t.Spec)));
        }
        (_watch, _watchCount) = (now, tanks.Length);
    }

    /// <summary>Where <paramref name="tankId"/> was among the tanks observed before (looked for from <paramref name="hint"/> on, then from the start), or −1.</summary>
    private int Watched(int[] ids, int tankId, int hint)
    {
        for (int k = hint; k < _watchCount; k++)
            if (ids[k] == tankId) return k;
        for (int k = 0; k < System.Math.Min(hint, _watchCount); k++)
            if (ids[k] == tankId) return k;
        return None;
    }

    /// <summary>Tests: the obstacles as of the last <see cref="Observe"/>.</summary>
    internal int ObstacleCount => _obstacleCount;

    /// <summary>Searches finished, found or not (a replaced search doesn't count): tests and the log.</summary>
    public int Searches { get; private set; }

    /// <summary>Of <see cref="Searches"/>, those that ended in <see cref="PathStatus.NoPath"/>.</summary>
    public int Failures { get; private set; }

    /// <summary>Tests: the tank served last (its search finished, or given up by its asking again), or −1.</summary>
    internal int LastServed => _lastServedId;

    /// <summary>Tests: the expansions the last <see cref="Run"/> spent.</summary>
    internal int LastRunExpansions { get; private set; }

    /// <summary>Gives <paramref name="tankId"/> (a simulation tank id, from 1) a request slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is below 1.</exception>
    /// <exception cref="ArgumentException">It has one already.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Simulation.MaxTanks"/> tanks have slots.</exception>
    public void Register(int tankId)
    {
        if (tankId < 1) throw new ArgumentOutOfRangeException(nameof(tankId), tankId, "Tank ids start at 1.");
        if (SlotOf(tankId) != None) throw new ArgumentException($"Tank {tankId} is registered already.", nameof(tankId));
        if (_count == Simulation.MaxTanks) throw new InvalidOperationException($"At most {Simulation.MaxTanks} tanks.");
        int at = _count;
        while (at > 0 && _ids[at - 1] > tankId)
        {
            Move(at - 1, at);
            at--;
        }
        (_ids[at], _from[at], _to[at], _status[at], _pointCount[at]) = (tankId, default, default, PathStatus.Idle, 0);
        _count++;
    }

    /// <summary>
    /// Asks for a path from <paramref name="from"/> to <paramref name="to"/> for <paramref name="tankId"/>: its status
    /// becomes <see cref="PathStatus.Pending"/>, and whatever it asked for before (pending, or found and not taken) is
    /// dropped. If the finder was searching for it, that search is given up and the tank goes to the back of the round,
    /// as if just served.
    /// </summary>
    /// <exception cref="ArgumentException">The tank isn't registered.</exception>
    public void Request(int tankId, Vector2 from, Vector2 to)
    {
        int slot = RegisteredSlot(tankId);
        (_from[slot], _to[slot], _status[slot], _pointCount[slot]) = (from, to, PathStatus.Pending, 0);
        if (tankId == _activeId) (_activeId, _lastServedId) = (None, tankId);
    }

    /// <summary>
    /// Works on the searches for at most <paramref name="budget"/> expansions, finishing at most one search that expands
    /// anything (see the remarks). Once a tick.
    /// </summary>
    public void Run(int budget = ExpansionsPerTick)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget), budget, "A budget can't be negative.");
        LastRunExpansions = 0;
        if (!TryBeginActive()) return;

        int before = _finder.Expansions;
        PathStatus status = _finder.Continue(budget);
        LastRunExpansions = _finder.Expansions - before;
        if (status != PathStatus.Pending) Finish(status);
    }

    /// <summary>
    /// How <paramref name="tankId"/>'s newest request stands: <see cref="PathStatus.Idle"/> (none, or its path taken),
    /// Pending, Found (until taken) or NoPath (until its next request).
    /// </summary>
    /// <exception cref="ArgumentException">The tank isn't registered.</exception>
    public PathStatus StatusOf(int tankId) => _status[RegisteredSlot(tankId)];

    /// <summary>
    /// After <see cref="PathStatus.Found"/>: copies the path's points (as <see cref="PathFinder.Points"/>) into
    /// <paramref name="into"/>, returns how many, and the tank's status goes back to Idle. Otherwise 0, nothing changed.
    /// </summary>
    /// <exception cref="ArgumentException">The tank isn't registered, or <paramref name="into"/> is too short for the path (which is kept).</exception>
    public int TakePath(int tankId, Span<Vector2> into)
    {
        int slot = RegisteredSlot(tankId);
        if (_status[slot] != PathStatus.Found) return 0;
        int count = _pointCount[slot];
        if (into.Length < count) throw new ArgumentException($"The path has {count} points; room for {into.Length}.", nameof(into));
        _points.AsSpan(slot * PathFinder.MaxPoints, count).CopyTo(into);
        (_status[slot], _pointCount[slot]) = (PathStatus.Idle, 0);
        return count;
    }

    /// <summary>
    /// Makes sure the finder holds a pending search: the one under way, else the next request round-robin. Requests
    /// that fail at Begin are settled on the way. False when nothing is pending.
    /// </summary>
    private bool TryBeginActive()
    {
        while (_activeId == None)
        {
            int id = NextPending();
            if (id == None) return false;
            _activeId = id;
            int slot = SlotOf(id);
            if (_finder.Begin(_from[slot], _to[slot], DiscsFor(id)) != PathStatus.Pending) Finish(PathStatus.NoPath);
        }
        return true;
    }

    /// <summary>The obstacles as of the last <see cref="Observe"/>, but <paramref name="tankId"/>'s own.</summary>
    private ReadOnlySpan<NavDisc> DiscsFor(int tankId)
    {
        int n = 0;
        for (int k = 0; k < _obstacleCount; k++)
            if (_obstacleId[k] != tankId) _discs[n++] = _obstacles[k];
        return _discs.AsSpan(0, n);
    }

    /// <summary>The first tank with a pending request after the one served last, in id order, wrapping; or None.</summary>
    private int NextPending()
    {
        int first = 0;
        while (first < _count && _ids[first] <= _lastServedId) first++;
        for (int k = 0; k < _count; k++)
        {
            int slot = (first + k) % _count;
            if (_status[slot] == PathStatus.Pending) return _ids[slot];
        }
        return None;
    }

    /// <summary>Settles the active search into its tank's slot.</summary>
    private void Finish(PathStatus status)
    {
        int slot = SlotOf(_activeId);
        _status[slot] = status;
        if (status == PathStatus.Found)
        {
            ReadOnlySpan<Vector2> points = _finder.Points;
            points.CopyTo(_points.AsSpan(slot * PathFinder.MaxPoints, PathFinder.MaxPoints));
            _pointCount[slot] = points.Length;
        }
        else Failures++;
        Searches++;
        _lastServedId = _activeId;
        _activeId = None;
    }

    private void Move(int from, int to)
    {
        (_ids[to], _from[to], _to[to], _status[to], _pointCount[to]) = (_ids[from], _from[from], _to[from], _status[from], _pointCount[from]);
        _points.AsSpan(from * PathFinder.MaxPoints, PathFinder.MaxPoints).CopyTo(_points.AsSpan(to * PathFinder.MaxPoints, PathFinder.MaxPoints));
    }

    private int RegisteredSlot(int tankId)
    {
        int slot = SlotOf(tankId);
        return slot != None ? slot : throw new ArgumentException($"Tank {tankId} isn't registered.", nameof(tankId));
    }

    private int SlotOf(int tankId)
    {
        for (int s = 0; s < _count; s++)
            if (_ids[s] == tankId) return s;
        return None;
    }
}
