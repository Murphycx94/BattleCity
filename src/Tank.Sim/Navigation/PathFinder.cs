using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Navigation;

/// <summary>
/// A* over a NavGrid (stage 5 design §4.1): 8 directions (a diagonal only where both side cells are open), octile
/// heuristic × MinCost, step cost (cost(a) + cost(b)) / 2 straight and (cost(a) + cost(b)) × 707 / 1000 diagonal, all in
/// int; arrays sized to the grid at construction with a generation counter, so a search allocates nothing and can be
/// continued over several calls. A found path is smoothed into sparse points.
/// </summary>
/// <remarks>
/// <para>The open set is an indexed binary heap (decrease-key in place, no duplicates), ordered by f, then h (nearer the
/// goal first), then cell index: integers only, so the same search expands the same cells in the same order everywhere.
/// The heuristic counts a diagonal as MinCost × 1414 / 1000 rounded down once per diagonal, never more than any
/// diagonal step costs, so it is consistent and the first time the goal leaves the heap its cost is the least there is.</para>
/// <para>A search carries on over a grid that changes under it (props falling between calls): a change only ever opens
/// a cell or makes one cheaper (<see cref="NavGrid"/>), so the costs the search went by are never too low, every cell
/// it took stays open and the heuristic stays admissible; the path it finds is a drivable one, if maybe not the
/// cheapest on the grid as it is now. Starting over instead would never end with trees falling every tick. However
/// often the grid changes, a search stops at <see cref="MaxExpansions"/> expansions in all.</para>
/// <para>A search may also be given <see cref="NavDisc"/>s, what the grid can't see (wrecks and tanks standing still,
/// 5C batch B review follow-up), fixed for the whole search: the cells whose centres lie within a disc's radius +
/// <see cref="NavGrid.Clearance"/> of its centre are closed for it, while expanding, for the corners a diagonal cuts, for
/// snapping the goal and for smoothing, and its smoothed segments keep the radius + <see cref="HullClearance"/> off the
/// centre. The start's own cell is never closed; and a disc the start lies within is shrunk to the start's distance from
/// its centre (a tank beside a wreck is never boxed in by the clearance round it, nor sent nearer it). The grid is left
/// as it is, so it only ever opens or gets cheaper under a search.</para>
/// <para>Not thread-safe: one finder serves one caller at a time (the match's <c>PathService</c>).</para>
/// </remarks>
public sealed class PathFinder
{
    /// <summary>A search that has expanded this many nodes gives up: <see cref="PathStatus.NoPath"/>.</summary>
    public const int MaxExpansions = 60_000;

    /// <summary>A longer path keeps its first this many cells (and its points stop there).</summary>
    public const int MaxPathCells = 1024;

    /// <summary>Smoothed points kept; a longer path is cut there (the bot replans at its end).</summary>
    public const int MaxPoints = 64;

    /// <summary>Smoothing looks at most this many cells ahead.</summary>
    public const int MaxSegmentCells = 32;

    /// <summary>A start or goal in a closed cell moves to the nearest open one within this, m.</summary>
    public const float SnapRadius = 12f;

    /// <summary>A smoothed segment keeps this far from standing rock, house and wall footprints, m.</summary>
    public const float HullClearance = 2.5f;

    /// <summary>A smoothed segment may cost at most this fraction more than the A* cells it replaces.</summary>
    public const float SmoothingExcess = 0.05f;

    /// <summary>The most <see cref="NavDisc"/>s a search takes: one a tank.</summary>
    public const int MaxDiscs = Simulation.MaxTanks;

    /// <summary>Two cell-edge crossings along a segment closer than this (of its length) are one, through a corner.</summary>
    private const double CornerTolerance = 1e-9;

    /// <summary>The neighbours in the order they are looked at: four straight, then four diagonal.</summary>
    private static readonly int[] StepI = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly int[] StepJ = { 0, 0, 1, -1, 1, 1, -1, -1 };
    private const int Straights = 4;

    private readonly NavGrid _grid;
    private readonly Battlefield _field;
    private readonly int _maxExpansions;

    // Per cell, sized to the grid once. A cell's g, h and parent hold for this search only when _seen[cell] is the
    // current generation; it is done with when _closed[cell] is.
    private readonly int[] _g;
    private readonly int[] _h;
    private readonly int[] _parent;
    private readonly int[] _seen;
    private readonly int[] _closed;
    private readonly int[] _heap;        // cells, a binary min-heap by (f, h, cell)
    private readonly int[] _heapIndex;   // where each seen, unclosed cell is in _heap
    private readonly int[] _discClosed;  // closed by a disc for this search when the current generation

    private readonly int[] _cells = new int[MaxPathCells];
    private readonly Vector2[] _points = new Vector2[MaxPoints];
    private readonly int[] _props = new int[Battlefield.QueryBufferSize];

    // The search's discs: centres, the radius closing cells and the radius segments keep off (both shrunk to the start's
    // distance for a disc it lies within).
    private readonly Vector2[] _discCentre = new Vector2[MaxDiscs];
    private readonly float[] _discClose = new float[MaxDiscs];
    private readonly float[] _discKeep = new float[MaxDiscs];
    private int _discCount;

    private int _generation;
    private int _heapCount;
    private int _cellCount;
    private int _pointCount;
    private Vector2 _from, _to, _end;
    private int _goal, _goalI, _goalJ;
    private int _straightUnit, _diagonalUnit;

    public PathFinder(NavGrid grid, Battlefield field) : this(grid, field, MaxExpansions)
    {
    }

    /// <param name="maxExpansions">Tests: the cap in place of <see cref="MaxExpansions"/>.</param>
    internal PathFinder(NavGrid grid, Battlefield field, int maxExpansions)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(field);
        if (maxExpansions < 1) throw new ArgumentOutOfRangeException(nameof(maxExpansions), maxExpansions, "A search needs at least one expansion.");
        if (grid.CellsX != (int)(field.Terrain.SizeX / NavGrid.CellSize) || grid.CellsZ != (int)(field.Terrain.SizeZ / NavGrid.CellSize))
            throw new ArgumentException($"A {grid.CellsX} × {grid.CellsZ} grid is not of the {field.Terrain.SizeX} × {field.Terrain.SizeZ} m battlefield.", nameof(grid));
        _grid = grid;
        _field = field;
        _maxExpansions = maxExpansions;
        int count = grid.CellsX * grid.CellsZ;
        _g = new int[count];
        _h = new int[count];
        _parent = new int[count];
        _seen = new int[count];
        _closed = new int[count];
        _heap = new int[count];
        _heapIndex = new int[count];
        _discClosed = new int[count];
    }

    public PathStatus Status { get; private set; }

    /// <summary>Nodes expanded in the current or last search, over all its calls.</summary>
    public int Expansions { get; private set; }

    /// <summary>
    /// After <see cref="PathStatus.Found"/>: g of the goal, ms at <see cref="NavGrid.ReferenceSpeed"/> (by the costs the
    /// search went by: no less than the path's cost on the grid now, if that changed under it).
    /// </summary>
    public int Cost { get; private set; }

    /// <summary>
    /// After <see cref="PathStatus.Found"/>: the smoothed points to drive through from the start (left out), the last one
    /// the goal itself (or its snapped cell's centre). The first may be the start cell's centre and the last but one the
    /// goal cell's, where the way straight from the start or to the goal doesn't fit (<see cref="Smooth"/>).
    /// </summary>
    public ReadOnlySpan<Vector2> Points => _points.AsSpan(0, _pointCount);

    /// <summary>Tests: after <see cref="PathStatus.Found"/>, the A* cells (j × CellsX + i), start to goal.</summary>
    internal ReadOnlySpan<int> Cells => _cells.AsSpan(0, _cellCount);

    /// <summary>Tests: the generation counter (the search under way, or the last one).</summary>
    internal int Generation => _generation;

    /// <summary>Tests: sets the generation counter, so the next searches cross its wrapping round.</summary>
    internal void SetGenerationForTests(int generation) => _generation = generation;

    /// <summary>
    /// Starts a search from <paramref name="from"/> to <paramref name="to"/> (each snapped to the nearest open cell within
    /// <see cref="SnapRadius"/> when its own is closed): <see cref="PathStatus.Pending"/>, or <see cref="PathStatus.NoPath"/>
    /// when either end has no open cell that near (or is not finite). Forgets the last search.
    /// </summary>
    public PathStatus Begin(Vector2 from, Vector2 to) => Begin(from, to, ReadOnlySpan<NavDisc>.Empty);

    /// <summary>
    /// <see cref="Begin(Vector2, Vector2)"/> round <paramref name="discs"/> as well (see the remarks): copied, they hold for
    /// the whole search, however many calls it takes.
    /// </summary>
    /// <exception cref="ArgumentException">More than <see cref="MaxDiscs"/> discs, or one not finite or of a negative radius.</exception>
    public PathStatus Begin(Vector2 from, Vector2 to, ReadOnlySpan<NavDisc> discs)
    {
        if (discs.Length > MaxDiscs) throw new ArgumentException($"At most {MaxDiscs} discs; {discs.Length} given.", nameof(discs));
        for (int k = 0; k < discs.Length; k++)
        {
            NavDisc disc = discs[k];
            if (!disc.IsValid) throw new ArgumentException($"Disc {k} is {disc}: a finite centre and a radius of 0 or more, please.", nameof(discs));
            float near = Vector2.Distance(from, disc.Centre);
            _discCentre[k] = disc.Centre;
            _discClose[k] = MathF.Min(disc.Radius + NavGrid.Clearance, near);
            _discKeep[k] = MathF.Min(disc.Radius + HullClearance, near);
        }
        _discCount = discs.Length;
        (_from, _to) = (from, to);
        return Start();
    }

    /// <summary>
    /// Expands at most <paramref name="budget"/> nodes of a pending search; on reaching the goal traces and smooths the
    /// path in the same call: <see cref="PathStatus.Found"/>. A search that is not pending is left as it is.
    /// </summary>
    public PathStatus Continue(int budget)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget), budget, "A budget can't be negative.");
        if (Status != PathStatus.Pending) return Status;

        ReadOnlySpan<ushort> costs = _grid.Costs;
        for (int n = 0; n < budget; n++)
        {
            if (_heapCount == 0) return Status = PathStatus.NoPath;
            int cell = Pop();
            _closed[cell] = _generation;
            Expansions++;
            if (cell == _goal)
            {
                Finish(costs);
                return Status = PathStatus.Found;
            }
            Expand(cell, costs);
            if (Expansions >= _maxExpansions) return Status = PathStatus.NoPath;
        }
        if (_heapCount == 0) Status = PathStatus.NoPath;
        return Status;
    }

    /// <summary><see cref="Begin(Vector2, Vector2)"/>, then <see cref="Continue"/> to the end: tests and tools.</summary>
    public PathStatus FindNow(Vector2 from, Vector2 to) => FindNow(from, to, ReadOnlySpan<NavDisc>.Empty);

    /// <summary><see cref="Begin(Vector2, Vector2, ReadOnlySpan{NavDisc})"/>, then <see cref="Continue"/> to the end: tests and tools.</summary>
    public PathStatus FindNow(Vector2 from, Vector2 to, ReadOnlySpan<NavDisc> discs) =>
        Begin(from, to, discs) == PathStatus.Pending ? Continue(_maxExpansions) : Status;

    private PathStatus Start()
    {
        NextGeneration();
        (_heapCount, _cellCount, _pointCount, Expansions, Cost) = (0, 0, 0, 0, 0);
        if (!TrySnap(_from, out int si, out int sj, out _)) return Status = PathStatus.NoPath;
        int width = _grid.CellsX;
        int start = sj * width + si;
        CloseDiscs();
        _discClosed[start] = 0;   // the start's cell is never closed
        if (!TrySnapGoal(out bool snapped)) return Status = PathStatus.NoPath;
        _goal = _goalJ * width + _goalI;
        _end = snapped ? NavGrid.CentreOf(_goalI, _goalJ) : _to;
        _straightUnit = _grid.MinCost;
        _diagonalUnit = _grid.MinCost * 1414 / 1000;
        See(start, 0, Heuristic(si, sj), -1);
        Push(start);
        return Status = PathStatus.Pending;
    }

    private void NextGeneration()
    {
        if (_generation == int.MaxValue)
        {
            Array.Clear(_seen);
            Array.Clear(_closed);
            Array.Clear(_discClosed);
            _generation = 0;
        }
        _generation++;
    }

    /// <summary>The open cell under <paramref name="p"/>, else the nearest open one within <see cref="SnapRadius"/> (the grid's alone).</summary>
    private bool TrySnap(Vector2 p, out int i, out int j, out bool snapped)
    {
        (i, j) = NavGrid.CellOf(p);
        snapped = !_grid.IsOpen(i, j);
        return !snapped || _grid.TryNearestOpen(p, SnapRadius, out i, out j);
    }

    /// <summary>Marks the cells whose centres lie within a disc's closing radius as closed for this search.</summary>
    private void CloseDiscs()
    {
        int width = _grid.CellsX, height = _grid.CellsZ;
        for (int k = 0; k < _discCount; k++)
        {
            Vector2 c = _discCentre[k];
            float r = _discClose[k];
            if (!(r > 0f)) continue;
            int i0 = System.Math.Max(0, (int)MathF.Floor((c.X - r) / NavGrid.CellSize)), i1 = System.Math.Min(width - 1, (int)MathF.Floor((c.X + r) / NavGrid.CellSize));
            int j0 = System.Math.Max(0, (int)MathF.Floor((c.Y - r) / NavGrid.CellSize)), j1 = System.Math.Min(height - 1, (int)MathF.Floor((c.Y + r) / NavGrid.CellSize));
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                if (Vector2.DistanceSquared(NavGrid.CentreOf(i, j), c) < r * r) _discClosed[j * width + i] = _generation;
        }
    }

    /// <summary>Whether cell (i, j) is closed for this search: off the grid, closed on it, or under a disc.</summary>
    private bool IsClosed(int i, int j) =>
        !_grid.IsOpen(i, j) || _discClosed[j * _grid.CellsX + i] == _generation;

    /// <summary>
    /// The goal's cell when it is open for this search, else the one whose centre is nearest the goal within
    /// <see cref="SnapRadius"/> (ties: lower j, then lower i, as <see cref="NavGrid.TryNearestOpen"/>); without discs,
    /// exactly the grid's.
    /// </summary>
    private bool TrySnapGoal(out bool snapped)
    {
        if (_discCount == 0) return TrySnap(_to, out _goalI, out _goalJ, out snapped);
        (_goalI, _goalJ) = NavGrid.CellOf(_to);
        snapped = IsClosed(_goalI, _goalJ);
        if (!snapped) return true;
        if (!float.IsFinite(_to.X) || !float.IsFinite(_to.Y)) return false;
        int i0 = System.Math.Max(0, (int)MathF.Floor((_to.X - SnapRadius) / NavGrid.CellSize) - 1);
        int i1 = System.Math.Min(_grid.CellsX - 1, (int)MathF.Floor((_to.X + SnapRadius) / NavGrid.CellSize) + 1);
        int j0 = System.Math.Max(0, (int)MathF.Floor((_to.Y - SnapRadius) / NavGrid.CellSize) - 1);
        int j1 = System.Math.Min(_grid.CellsZ - 1, (int)MathF.Floor((_to.Y + SnapRadius) / NavGrid.CellSize) + 1);
        float best = SnapRadius * SnapRadius;
        bool found = false;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            if (IsClosed(i, j)) continue;
            float d = Vector2.DistanceSquared(NavGrid.CentreOf(i, j), _to);
            if (d > best || (found && d == best)) continue;
            (best, _goalI, _goalJ, found) = (d, i, j, true);
        }
        return found;
    }

    /// <summary>Octile distance to the goal in whole steps of the cheapest straight and diagonal moves there are.</summary>
    private int Heuristic(int i, int j)
    {
        int dx = System.Math.Abs(i - _goalI), dz = System.Math.Abs(j - _goalJ);
        int diagonals = System.Math.Min(dx, dz);
        return _straightUnit * (System.Math.Max(dx, dz) - diagonals) + _diagonalUnit * diagonals;
    }

    private void Expand(int cell, ReadOnlySpan<ushort> costs)
    {
        int width = _grid.CellsX, height = _grid.CellsZ;
        int ci = cell % width, cj = cell / width;
        int here = costs[cell], g = _g[cell];
        for (int d = 0; d < StepI.Length; d++)
        {
            int ni = ci + StepI[d], nj = cj + StepJ[d];
            if ((uint)ni >= (uint)width || (uint)nj >= (uint)height) continue;
            int next = nj * width + ni;
            int there = costs[next];
            if (there == NavGrid.Blocked || _closed[next] == _generation || _discClosed[next] == _generation) continue;
            int step;
            if (d < Straights) step = (here + there) / 2;
            else if (Shut(costs, cj * width + ni) || Shut(costs, nj * width + ci)) continue;   // no corner cutting
            else step = (here + there) * 707 / 1000;

            int ng = g + step;
            if (_seen[next] != _generation)
            {
                See(next, ng, Heuristic(ni, nj), cell);
                Push(next);
            }
            else if (ng < _g[next])
            {
                _g[next] = ng;
                _parent[next] = cell;
                SiftUp(_heapIndex[next]);
            }
        }
    }

    /// <summary>Whether a cell on the grid is closed for this search, on the grid or under a disc.</summary>
    private bool Shut(ReadOnlySpan<ushort> costs, int cell) => costs[cell] == NavGrid.Blocked || _discClosed[cell] == _generation;

    private void See(int cell, int g, int h, int parent)
    {
        _seen[cell] = _generation;
        _g[cell] = g;
        _h[cell] = h;
        _parent[cell] = parent;
    }

    // ---- the heap ----

    /// <summary>Whether cell a leaves the heap before cell b: lower f, then lower h, then lower index.</summary>
    private bool Before(int a, int b)
    {
        int fa = _g[a] + _h[a], fb = _g[b] + _h[b];
        if (fa != fb) return fa < fb;
        if (_h[a] != _h[b]) return _h[a] < _h[b];
        return a < b;
    }

    private void Push(int cell)
    {
        int at = _heapCount++;
        _heap[at] = cell;
        _heapIndex[cell] = at;
        SiftUp(at);
    }

    private int Pop()
    {
        int top = _heap[0];
        int last = _heap[--_heapCount];
        if (_heapCount > 0)
        {
            _heap[0] = last;
            _heapIndex[last] = 0;
            SiftDown(0);
        }
        return top;
    }

    private void SiftUp(int at)
    {
        int cell = _heap[at];
        while (at > 0)
        {
            int up = (at - 1) / 2;
            int other = _heap[up];
            if (!Before(cell, other)) break;
            _heap[at] = other;
            _heapIndex[other] = at;
            at = up;
        }
        _heap[at] = cell;
        _heapIndex[cell] = at;
    }

    private void SiftDown(int at)
    {
        int cell = _heap[at];
        while (true)
        {
            int child = 2 * at + 1;
            if (child >= _heapCount) break;
            if (child + 1 < _heapCount && Before(_heap[child + 1], _heap[child])) child++;
            int other = _heap[child];
            if (!Before(other, cell)) break;
            _heap[at] = other;
            _heapIndex[other] = at;
            at = child;
        }
        _heap[at] = cell;
        _heapIndex[cell] = at;
    }

    // ---- the path ----

    /// <summary>Traces the goal back to the start into <see cref="Cells"/> and smooths it into <see cref="Points"/>.</summary>
    private void Finish(ReadOnlySpan<ushort> costs)
    {
        Cost = _g[_goal];
        int length = 0;
        for (int c = _goal; c >= 0; c = _parent[c]) length++;
        _cellCount = System.Math.Min(length, MaxPathCells);
        int k = length - 1;
        for (int c = _goal; c >= 0; c = _parent[c], k--)
            if (k < MaxPathCells) _cells[k] = c;
        Smooth(costs, complete: length <= MaxPathCells);
    }

    /// <summary>
    /// From each anchor a (the start's cell first), the farthest path cell k ≤ a + <see cref="MaxSegmentCells"/> whose
    /// centre a leg from a's centre reaches (<see cref="LegFits"/>); the next cell when none further does. Its centre is
    /// the next point and the next anchor. Then the two ends, which aren't cell centres (<see cref="EndAtGoal"/>,
    /// <see cref="StartFromHere"/>).
    /// </summary>
    private void Smooth(ReadOnlySpan<ushort> costs, bool complete)
    {
        int last = _cellCount - 1, a = 0, before = 0, first = last;
        Span<int> dearest = stackalloc int[MaxSegmentCells + 1];
        while (a < last && _pointCount < MaxPoints)
        {
            int far = System.Math.Min(a + MaxSegmentCells, last), max = 0;
            for (int k = a; k <= far; k++) dearest[k - a] = max = System.Math.Max(max, costs[_cells[k]]);
            int best = a + 1;
            for (int k = far; k > a + 1; k--)
                if (LegFits(costs, CentreAt(a), CentreAt(k), a, k, dearest[k - a], 0))
                {
                    best = k;
                    break;
                }
            if (_pointCount == 0) first = best;
            _points[_pointCount++] = CentreAt(best);
            (before, a) = (a, best);
        }
        if (_pointCount == 0) _points[_pointCount++] = _end;   // the start's cell is the goal's
        else if (complete && a == last) EndAtGoal(costs, before);
        StartFromHere(costs, first);
    }

    /// <summary>
    /// The last point, the goal cell's centre, becomes the goal itself (<c>_end</c>) when the leg to it from the anchor
    /// <paramref name="before"/> fits; else the goal follows its cell's centre (or, with no room left, the path ends at the
    /// centre: the bot is in the goal's cell there).
    /// </summary>
    private void EndAtGoal(ReadOnlySpan<ushort> costs, int before)
    {
        int last = _cellCount - 1;
        Vector2 centre = CentreAt(last);
        if (_end == centre) return;   // the goal was snapped to it
        double extra = WithinCell(costs, last, centre, _end);
        if (LegFits(costs, CentreAt(before), _end, before, last, Dearest(costs, before, last), extra)) _points[_pointCount - 1] = _end;
        else if (_pointCount < MaxPoints) _points[_pointCount++] = _end;
    }

    /// <summary>
    /// When the bot can't drive straight from where it asked from (<c>_from</c>: anywhere in the start cell, or out of it
    /// if snapped) to the first point (the anchor <paramref name="first"/>'s centre, or the goal), the start cell's centre
    /// goes in front (the last point dropped if there is no room: the bot replans there).
    /// </summary>
    private void StartFromHere(ReadOnlySpan<ushort> costs, int first)
    {
        Vector2 centre = CentreAt(0), to = _points[0];
        if (to == centre) return;
        (int i, int j) = NavGrid.CellOf(_from);
        if (j * _grid.CellsX + i == _cells[0])
        {
            double extra = WithinCell(costs, 0, _from, centre);
            if (to == _end && first == _cellCount - 1) extra += WithinCell(costs, first, CentreAt(first), _end);
            if (LegFits(costs, _from, to, 0, first, Dearest(costs, 0, first), extra)) return;
        }
        int keep = System.Math.Min(_pointCount, MaxPoints - 1);
        _points.AsSpan(0, keep).CopyTo(_points.AsSpan(1));
        _points[0] = centre;
        _pointCount = keep + 1;
    }

    /// <summary>
    /// Whether a leg from <paramref name="from"/> to <paramref name="to"/>, standing for path cells
    /// <paramref name="ka"/> … <paramref name="kb"/>, may be driven instead: over open cells none dearer than
    /// <paramref name="max"/> (the dearest of those path cells), costing at most <see cref="SmoothingExcess"/> more than
    /// the search did between them (g at kb − g at ka, plus <paramref name="extra"/> for ends off the cells' centres), and
    /// keeping <see cref="HullClearance"/> from every standing rock, house and wall. g is the search's own, as good as a
    /// cost on the grid now: it never fell since (<see cref="NavGrid"/>).
    /// </summary>
    private bool LegFits(ReadOnlySpan<ushort> costs, Vector2 from, Vector2 to, int ka, int kb, int max, double extra)
    {
        double allowed = (_g[_cells[kb]] - _g[_cells[ka]] + extra) * (1.0 + SmoothingExcess);
        return LineFits(costs, from, to, max, allowed) && KeepsClear(from, to);
    }

    /// <summary>
    /// Walks the segment a → b over every cell it passes through and, where it passes through a corner, the two cells
    /// beside the corner as well: false as soon as one costs more than <paramref name="max"/> (a closed one, or one off
    /// the grid, costs more than anything) or the cells' costs, each × the length of the segment inside it in cells,
    /// come to more than <paramref name="allowed"/>.
    /// </summary>
    private bool LineFits(ReadOnlySpan<ushort> costs, Vector2 a, Vector2 b, int max, double allowed)
    {
        double x = a.X / NavGrid.CellSize, z = a.Y / NavGrid.CellSize;
        double dx = b.X / NavGrid.CellSize - x, dz = b.Y / NavGrid.CellSize - z;
        int i = (int)System.Math.Floor(x), j = (int)System.Math.Floor(z);
        int si = System.Math.Sign(dx), sj = System.Math.Sign(dz);
        // Along the segment (t from 0 to 1): where it next crosses a cell edge across x and across z, and how far apart
        // those crossings are.
        double stepX = si != 0 ? 1.0 / System.Math.Abs(dx) : double.PositiveInfinity;
        double stepZ = sj != 0 ? 1.0 / System.Math.Abs(dz) : double.PositiveInfinity;
        double nextX = si > 0 ? (i + 1 - x) * stepX : si < 0 ? (x - i) * stepX : double.PositiveInfinity;
        double nextZ = sj > 0 ? (j + 1 - z) * stepZ : sj < 0 ? (z - j) * stepZ : double.PositiveInfinity;
        double length = System.Math.Sqrt(dx * dx + dz * dz), t = 0, cost = 0;
        while (true)
        {
            int here = CostAt(costs, i, j);
            if (here > max) return false;
            double leave = System.Math.Min(System.Math.Min(nextX, nextZ), 1.0);
            cost += here * (leave - t) * length;
            if (cost > allowed) return false;
            if (leave >= 1.0 - CornerTolerance) return true;
            t = leave;
            if (System.Math.Abs(nextX - nextZ) <= CornerTolerance)
            {
                if (CostAt(costs, i + si, j) > max || CostAt(costs, i, j + sj) > max) return false;
                (i, j, nextX, nextZ) = (i + si, j + sj, nextX + stepX, nextZ + stepZ);
            }
            else if (nextX < nextZ) (i, nextX) = (i + si, nextX + stepX);
            else (j, nextZ) = (j + sj, nextZ + stepZ);
        }
    }

    /// <summary>
    /// Whether the segment a → b keeps <see cref="HullClearance"/> from every standing rock, house and wall, and each
    /// disc's keeping radius from its centre.
    /// </summary>
    private bool KeepsClear(Vector2 a, Vector2 b)
    {
        for (int k = 0; k < _discCount; k++)
            if (PointToSegment(_discCentre[k], a, b) < _discKeep[k]) return false;
        foreach (int k in _field.Along(PropSet.ShellStoppers, a, b, HullClearance, _props))
            if (_field.StandingAt(k) && DistanceToSegment(_field.PropAt(k).Footprint, a, b) < HullClearance)
                return false;
        return true;
    }

    /// <summary>Path cell k's centre.</summary>
    private Vector2 CentreAt(int k) => NavGrid.CentreOf(_cells[k] % _grid.CellsX, _cells[k] / _grid.CellsX);

    /// <summary>The dearest of path cells ka … kb.</summary>
    private int Dearest(ReadOnlySpan<ushort> costs, int ka, int kb)
    {
        int max = 0;
        for (int k = ka; k <= kb; k++) max = System.Math.Max(max, costs[_cells[k]]);
        return max;
    }

    /// <summary>What going from p to q inside path cell k costs: its cost × the distance in cells.</summary>
    private double WithinCell(ReadOnlySpan<ushort> costs, int k, Vector2 p, Vector2 q) =>
        costs[_cells[k]] * (double)Vector2.Distance(p, q) / NavGrid.CellSize;

    /// <summary>Cell (i, j)'s cost; <see cref="NavGrid.Blocked"/> off the grid or under a disc.</summary>
    private int CostAt(ReadOnlySpan<ushort> costs, int i, int j)
    {
        if ((uint)i >= (uint)_grid.CellsX || (uint)j >= (uint)_grid.CellsZ) return NavGrid.Blocked;
        int cell = j * _grid.CellsX + i;
        return _discClosed[cell] == _generation ? NavGrid.Blocked : costs[cell];
    }

    /// <summary>The least distance between <paramref name="box"/> and the segment a → b (0 where they meet).</summary>
    private static float DistanceToSegment(in Obb2 box, Vector2 a, Vector2 b)
    {
        Vector2 right = box.AxisRight, forward = box.AxisForward, half = box.HalfExtents;
        Vector2 p = new(Vector2.Dot(a - box.Center, right), Vector2.Dot(a - box.Center, forward));
        Vector2 q = new(Vector2.Dot(b - box.Center, right), Vector2.Dot(b - box.Center, forward));
        if (Crosses(p, q - p, half)) return 0f;
        // Apart, two convex shapes are nearest at a corner of one of them.
        float least = MathF.Min(box.DistanceToPoint(a), box.DistanceToPoint(b));
        for (int c = 0; c < 4; c++)
        {
            var corner = new Vector2(c % 2 == 0 ? -half.X : half.X, c < 2 ? -half.Y : half.Y);
            least = MathF.Min(least, PointToSegment(corner, p, q));
        }
        return least;
    }

    /// <summary>Whether the segment p → p + d meets the box ±half (Liang–Barsky).</summary>
    private static bool Crosses(Vector2 p, Vector2 d, Vector2 half)
    {
        float t0 = 0f, t1 = 1f;
        return Clip(-d.X, p.X + half.X, ref t0, ref t1) && Clip(d.X, half.X - p.X, ref t0, ref t1)
            && Clip(-d.Y, p.Y + half.Y, ref t0, ref t1) && Clip(d.Y, half.Y - p.Y, ref t0, ref t1);
    }

    /// <summary>Narrows [t0, t1] to where denominator × t ≤ numerator; false when nothing is left.</summary>
    private static bool Clip(float denominator, float numerator, ref float t0, ref float t1)
    {
        if (denominator == 0f) return numerator >= 0f;
        float t = numerator / denominator;
        if (denominator < 0f)
        {
            if (t > t1) return false;
            if (t > t0) t0 = t;
        }
        else
        {
            if (t < t0) return false;
            if (t < t1) t1 = t;
        }
        return true;
    }

    private static float PointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 0f ? System.Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return Vector2.Distance(point, a + ab * t);
    }
}
