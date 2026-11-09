using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Navigation;

/// <summary>
/// Where bots can drive and what it costs (stage 5 design §4.1): a grid of CellSize-metre cells over the map, each
/// holding the milliseconds a tank at ReferenceSpeed needs to cross it, or Blocked. Built once per map from the untouched
/// battlefield (MapDefinition.NewNavGrid), copied for each match and kept in step with that match's props by Refresh
/// (and Sync, after props fell unheard). Integers throughout, so a search is exact and the same everywhere.
/// </summary>
/// <remarks>
/// Cell (i, j) covers x ∈ [i·CellSize, (i + 1)·CellSize), z likewise, and is judged at its centre. One private rule,
/// <see cref="Evaluate"/>, prices a cell for both <see cref="Build"/> and <see cref="Refresh"/>, so re-evaluating the
/// cells a fallen prop touches always gives what building afresh would. Tank types differ only by a factor common to
/// the whole path (a fast tank is fast everywhere), so every bot shares one grid and the same shortest paths.
/// <para>A prop going down never closes a cell or makes one dearer: a fallen wall's or house's rubble lies within the
/// <see cref="Clearance"/> its standing footprint closed (rubble spreads <see cref="Prop.RubbleSpread"/>, its corners
/// under 0.71 m out), a felled tree takes its <see cref="TreeCost"/> away, and nothing is priced below
/// <see cref="MinCost"/>. So a search can carry on over a refresh: the costs it went by are never too low, and every
/// cell it took stays open. Debug builds check this on every re-evaluated cell. (Props standing back up is the
/// sandbox's <c>RestoreProps</c>, never a match's.)</para>
/// </remarks>
public sealed class NavGrid
{
    public const float CellSize = 4f;

    /// <summary>A standing rock's, house's or wall's footprint grown by this closes the cells whose centres it covers, m.</summary>
    public const float Clearance = 3f;

    /// <summary>Cells whose centres lie less than this inside the playable area are closed, m.</summary>
    public const float EdgeMargin = 2f;

    /// <summary>The speed the costs are reckoned at, m/s.</summary>
    public const float ReferenceSpeed = 10f;

    /// <summary>A cell of flat dry grass: 1000 × CellSize / ReferenceSpeed ms.</summary>
    public const int GrassCost = 400;

    /// <summary>Added per standing trunk whose centre is in the cell (a quarter of grass each), up to <see cref="MaxTreeCost"/>.</summary>
    public const int TreeCost = 100, MaxTreeCost = 400;

    public const ushort Blocked = ushort.MaxValue;

    /// <summary>The steepest slope every tank climbs: the smallest <see cref="TankSpec.MaxClimb"/> in <see cref="TankCatalog.All"/>, radians.</summary>
    public static readonly float MaxSlope = SmallestClimb();

    /// <summary>Ground whose normal's Y is below this is steeper than <see cref="MaxSlope"/>.</summary>
    private static readonly float MinNormalY = MathF.Cos(MaxSlope);

    /// <summary>Where slope and ground are sampled round a cell's centre (the slope at the centre too).</summary>
    private static readonly Vector2[] Probes = { new(-1, -1), new(1, -1), new(-1, 1), new(1, 1) };

    /// <summary>A trunk whose centre is in a cell lies within this of the cell's centre.</summary>
    private const float HalfDiagonal = CellSize * 0.7072f;

    /// <summary><see cref="CellOf"/> gives −1 (off the grid) for a coordinate this many cells or more from the origin, or NaN.</summary>
    private const float FarCells = 1 << 24;

    private readonly ushort[] _cells;   // row-major: (i, j) at j × CellsX + i

    private NavGrid(int cellsX, int cellsZ, ushort[] cells, int minCost, int version)
    {
        CellsX = cellsX;
        CellsZ = cellsZ;
        _cells = cells;
        MinCost = minCost;
        Version = version;
    }

    /// <summary>Cells across x: (int)(SizeX / CellSize). Riverland 300, sandbox 100.</summary>
    public int CellsX { get; }

    public int CellsZ { get; }

    /// <summary>
    /// The cheapest open cell when built (1 when none is open): the heuristic's unit. <see cref="Refresh"/> and
    /// <see cref="Sync"/> keep every cell at least this, so it stays admissible.
    /// </summary>
    public int MinCost { get; private set; }

    /// <summary>Goes up by one whenever <see cref="Refresh"/> or <see cref="Sync"/> changes a cell.</summary>
    public int Version { get; private set; }

    /// <summary>Tests and the path finder: every cell's cost, row-major (j × CellsX + i).</summary>
    internal ReadOnlySpan<ushort> Costs => _cells;

    /// <summary>Prices every cell of <paramref name="field"/> as its props stand now.</summary>
    public static NavGrid Build(Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        int cellsX = (int)(field.Terrain.SizeX / CellSize), cellsZ = (int)(field.Terrain.SizeZ / CellSize);
        if (cellsX < 1 || cellsZ < 1)
            throw new ArgumentException($"A {field.Terrain.SizeX} × {field.Terrain.SizeZ} m map has no whole {CellSize} m cell.", nameof(field));
        var cells = new ushort[cellsX * cellsZ];
        var buffer = new int[Battlefield.QueryBufferSize];
        int min = int.MaxValue;
        for (int j = 0; j < cellsZ; j++)
        for (int i = 0; i < cellsX; i++)
        {
            ushort cost = Evaluate(field, i, j, buffer);
            cells[j * cellsX + i] = cost;
            if (cost != Blocked && cost < min) min = cost;
        }
        return new NavGrid(cellsX, cellsZ, cells, min == int.MaxValue ? 1 : min, 0);
    }

    /// <summary>The same cells in an array of its own, with the same <see cref="MinCost"/> and <see cref="Version"/>.</summary>
    public NavGrid Copy() => new(CellsX, CellsZ, (ushort[])_cells.Clone(), MinCost, Version);

    /// <summary>The cell's cost in ms at <see cref="ReferenceSpeed"/>; <see cref="Blocked"/> when closed or off the grid.</summary>
    public ushort CostAt(int i, int j) => OnGrid(i, j) ? _cells[j * CellsX + i] : Blocked;

    /// <summary>False when closed or off the grid.</summary>
    public bool IsOpen(int i, int j) => CostAt(i, j) != Blocked;

    /// <summary>Whether the cell under <paramref name="p"/> (x, z) is open; false off the grid or for a coordinate that is not finite.</summary>
    public bool IsOpenAt(Vector2 p)
    {
        (int i, int j) = CellOf(p);
        return IsOpen(i, j);
    }

    /// <summary>(floor(x / CellSize), floor(z / CellSize)); may be off the grid (−1 for NaN or a coordinate far beyond any map).</summary>
    public static (int I, int J) CellOf(Vector2 p) => (Floor(p.X), Floor(p.Y));

    /// <summary>((i + 0.5) × CellSize, (j + 0.5) × CellSize).</summary>
    public static Vector2 CentreOf(int i, int j) => new((i + 0.5f) * CellSize, (j + 0.5f) * CellSize);

    /// <summary>The open cell whose centre is nearest p, within radius; ties: lower j, then lower i.</summary>
    /// <remarks>
    /// Looks at every cell whose centre may lie within <paramref name="radius"/> (a box of them, clamped to the grid) in
    /// row order and keeps the first nearest, so it is exactly the nearest, ties included. Allocation-free.
    /// </remarks>
    public bool TryNearestOpen(Vector2 p, float radius, out int i, out int j)
    {
        (i, j) = (-1, -1);
        if (!(radius >= 0f) || !float.IsFinite(p.X) || !float.IsFinite(p.Y)) return false;
        int i0 = ClampCell(MathF.Floor((p.X - radius) / CellSize) - 1f, CellsX), i1 = ClampCell(MathF.Floor((p.X + radius) / CellSize) + 1f, CellsX);
        int j0 = ClampCell(MathF.Floor((p.Y - radius) / CellSize) - 1f, CellsZ), j1 = ClampCell(MathF.Floor((p.Y + radius) / CellSize) + 1f, CellsZ);
        float best = radius * radius;
        bool found = false;
        for (int cj = j0; cj <= j1; cj++)
        for (int ci = i0; ci <= i1; ci++)
        {
            if (_cells[cj * CellsX + ci] == Blocked) continue;
            float d = Vector2.DistanceSquared(CentreOf(ci, cj), p);
            if (d > best || (found && d == best)) continue;
            (best, i, j, found) = (d, ci, cj, true);
        }
        return found;
    }

    /// <summary>
    /// Re-evaluates the cells the props in <paramref name="events"/>' PropDestroyed entries change: a house, brick or stone
    /// wall (its cells reopen, its rubble slows) or a tree (its cell gets cheaper). Fences and bushes change nothing.
    /// Allocation-free. Returns how many cells changed.
    /// </summary>
    /// <remarks>
    /// A wall or house re-evaluates every cell whose centre may lie in its footprint's bounding box grown by
    /// <see cref="Clearance"/> + <see cref="Prop.RubbleSpread"/> (all it closed and all its rubble covers); a tree, the cell
    /// its trunk stands in. The props' state is read from <paramref name="field"/> as it is now, whatever the events say.
    /// </remarks>
    /// <exception cref="ArgumentException">An event names a prop <paramref name="field"/> does not have.</exception>
    public int Refresh(Battlefield field, ReadOnlySpan<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(field);
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        int changed = 0;
        foreach (ref readonly GameEvent e in events)
        {
            if (e.Type == GameEventType.PropDestroyed) changed += RefreshProp(field, field.IndexOf(e.PropId), buffer);
        }
        if (changed > 0) Version++;
        return changed;
    }

    /// <summary>
    /// Re-evaluates the cells round every prop of <paramref name="field"/> that is down now, as <see cref="Refresh"/>
    /// would for its PropDestroyed event: catches the grid up with props that fell while no events reached it (at the
    /// start of a match, or after skipped ticks). On a grid already in step it changes nothing. Allocation-free. Returns
    /// how many cells changed.
    /// </summary>
    public int Sync(Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        int changed = 0;
        for (int k = 0; k < field.Props.Count; k++)
            if (!field.StandingAt(k)) changed += RefreshProp(field, k, buffer);
        if (changed > 0) Version++;
        return changed;
    }

    /// <summary>
    /// Tests only (random grids): sets one cell's cost (<see cref="Blocked"/> closes it), lowering <see cref="MinCost"/> to
    /// it when cheaper so the heuristic stays admissible, and bumps <see cref="Version"/>.
    /// </summary>
    internal void SetCostForTests(int i, int j, ushort cost)
    {
        if (!OnGrid(i, j)) throw new ArgumentOutOfRangeException(nameof(i), $"Cell ({i}, {j}) is off the {CellsX} × {CellsZ} grid.");
        if (cost == 0) throw new ArgumentOutOfRangeException(nameof(cost), cost, "A cell costs at least 1.");
        _cells[j * CellsX + i] = cost;
        if (cost != Blocked && cost < MinCost) MinCost = cost;
        Version++;
    }

    private bool OnGrid(int i, int j) => (uint)i < (uint)CellsX && (uint)j < (uint)CellsZ;

    /// <summary>Re-evaluates the cells prop <paramref name="index"/> (by index) touches: a wall's or house's reach, a tree's cell; others none.</summary>
    private int RefreshProp(Battlefield field, int index, Span<int> buffer)
    {
        ref readonly Prop prop = ref field.PropAt(index);
        if (prop.IsWall) return RefreshAround(field, prop.Footprint, buffer);
        if (prop.Kind != PropKind.Tree) return 0;
        (int i, int j) = CellOf(prop.Footprint.Center);
        return OnGrid(i, j) ? Reevaluate(field, i, j, buffer) : 0;
    }

    private int RefreshAround(Battlefield field, in Obb2 footprint, Span<int> buffer)
    {
        const float Grow = Clearance + Prop.RubbleSpread;
        float reachX = footprint.Radius(Vector2.UnitX) + Grow, reachZ = footprint.Radius(Vector2.UnitY) + Grow;
        Vector2 c = footprint.Center;
        int i0 = ClampCell(MathF.Floor((c.X - reachX) / CellSize), CellsX), i1 = ClampCell(MathF.Floor((c.X + reachX) / CellSize), CellsX);
        int j0 = ClampCell(MathF.Floor((c.Y - reachZ) / CellSize), CellsZ), j1 = ClampCell(MathF.Floor((c.Y + reachZ) / CellSize), CellsZ);
        int changed = 0;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            changed += Reevaluate(field, i, j, buffer);
        return changed;
    }

    /// <summary>Prices cell (i, j) afresh, never below <see cref="MinCost"/>; 1 if that changed it.</summary>
    private int Reevaluate(Battlefield field, int i, int j, Span<int> buffer)
    {
        ushort cost = Evaluate(field, i, j, buffer);
        if (cost != Blocked && cost < MinCost) cost = (ushort)MinCost;
        ref ushort cell = ref _cells[j * CellsX + i];
        if (cell == cost) return 0;
        Debug.Assert(cell == Blocked || cost <= cell, $"Cell ({i}, {j}) went from {cell} to {cost}: a prop falling may only open a cell or make it cheaper.");
        cell = cost;
        return 1;
    }

    /// <summary>
    /// What crossing cell (i, j) costs, judged at its centre c:
    /// <list type="number">
    /// <item>c less than <see cref="EdgeMargin"/> inside the playable area: <see cref="Blocked"/>;</item>
    /// <item>the steepest ground at c and at the four points (±1, ±1) m round it (the least normal Y, ny) steeper than
    /// <see cref="MaxSlope"/>: <see cref="Blocked"/>;</item>
    /// <item>a standing rock, house or wall within <see cref="Clearance"/> of c: <see cref="Blocked"/>;</item>
    /// <item>else the speed factor is the mean of the ground's or water's (<see cref="Battlefield.SpeedFactorAt"/>) at the
    /// four points, × <see cref="TankMotion.RubbleSpeedFactor"/> when c lies on a fallen wall's or house's rubble, × ny²
    /// (uphill is slower by cos² of the slope; the grid has no direction, so downhill is priced the same);</item>
    /// <item>and the cost round(<see cref="GrassCost"/> / factor) + <see cref="TreeCost"/> per standing trunk whose centre
    /// is in the cell (at most <see cref="MaxTreeCost"/>), within 1 … 65534.</item>
    /// </list>
    /// </summary>
    private static ushort Evaluate(Battlefield field, int i, int j, Span<int> buffer)
    {
        HeightField terrain = field.Terrain;
        Vector2 c = CentreOf(i, j);
        float low = field.Border + EdgeMargin;
        if (c.X < low || c.Y < low || c.X > terrain.SizeX - low || c.Y > terrain.SizeZ - low) return Blocked;

        float ny = terrain.NormalAt(c.X, c.Y).Y, factor = 0f;
        foreach (Vector2 d in Probes)
        {
            Vector2 s = c + d;
            ny = MathF.Min(ny, terrain.NormalAt(s.X, s.Y).Y);
            factor += field.SpeedFactorAt(s);
        }
        if (ny < MinNormalY) return Blocked;

        foreach (int k in field.Near(PropSet.ShellStoppers, c, Clearance, buffer))
            if (field.StandingAt(k) && field.PropAt(k).Footprint.DistanceToPoint(c) <= Clearance) return Blocked;

        factor *= 0.25f;
        foreach (int k in field.Near(PropSet.Walls, c, 0f, buffer))
            if (!field.StandingAt(k) && field.PropAt(k).RubbleFootprint.Contains(c))
            {
                factor *= TankMotion.RubbleSpeedFactor;
                break;
            }
        factor *= ny * ny;

        int trees = 0;
        foreach (int k in field.Near(PropSet.Foliage, c, HalfDiagonal, buffer))
        {
            ref readonly Prop prop = ref field.PropAt(k);
            if (prop.Kind == PropKind.Tree && field.StandingAt(k) && CellOf(prop.Footprint.Center) == (i, j)) trees++;
        }

        int cost = (int)MathF.Round(GrassCost / factor) + System.Math.Min(MaxTreeCost, TreeCost * trees);
        return (ushort)System.Math.Clamp(cost, 1, Blocked - 1);
    }

    private static int Floor(float v)
    {
        float cell = MathF.Floor(v / CellSize);
        return cell > -FarCells && cell < FarCells ? (int)cell : -1;
    }

    /// <summary>A cell index along an axis, from a whole float (any size, ±∞ included), clamped to 0 … cells − 1.</summary>
    private static int ClampCell(float cell, int cells) => (int)System.Math.Clamp(cell, 0f, cells - 1);

    private static float SmallestClimb()
    {
        float least = float.PositiveInfinity;
        foreach (TankSpec spec in TankCatalog.All) least = MathF.Min(least, spec.MaxClimb);
        return least;
    }
}
