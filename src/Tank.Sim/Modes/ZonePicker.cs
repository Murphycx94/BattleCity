using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;

namespace Tank.Sim.Modes;

/// <summary>
/// Picks a royale's rings once, at the start of the match (stage 6 design §3.1). It reads the navigation grid, so it runs
/// only on the server (and in the simulation's tests); clients never pick, they are sent each ring as its waiting begins.
/// </summary>
public static class ZonePicker
{
    /// <summary>Draws for each ring's centre before it falls back to scanning for the nearest land cell.</summary>
    public const int Tries = 64;

    /// <summary>The server draws with XorShift(matchSeed ^ SeedSalt), so the rings don't follow the spawn order drawn from the same seed.</summary>
    public const uint SeedSalt = 0x20E0_6A6Au;

    /// <summary>The open cell nearest a spawn point within this starts the search for reachable land, m (PathFinder.SnapRadius).</summary>
    private const float SpawnSnap = PathFinder.SnapRadius;

    private static readonly int[] StepI = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly int[] StepJ = { 0, 0, 1, -1, 1, 1, -1, -1 };
    private const int Straights = 4;

    /// <summary>
    /// The six rings of a match (stage 6 design §3.1): ring 0 = <see cref="ZoneSchedule.Outer"/>, then for k = 1–5 a centre drawn
    /// uniformly in the disc of radius r_{k−1} − r_k round ring k − 1's centre (angle 2π·u, distance (r_{k−1} − r_k)·√v, u and v
    /// from one <see cref="XorShift"/> seeded with <paramref name="seed"/>), so ring k lies wholly inside ring k − 1, and kept only
    /// where the point itself is dry (<c>WaterDepthAt ≤ 0</c>) and its cell is <see cref="Land"/>. After <see cref="Tries"/> misses
    /// the centre is the centre of the land cell nearest ring k − 1's centre among those whose centres lie in that disc (a
    /// deterministic scan, row by row, the first of equals kept); a land cell's centre is dry by <see cref="Land"/>'s rule. Radii
    /// from the schedule, never more than ring k − 1's (on a map smaller than the riverland the first rings are as big as ring 0).
    /// Reads <paramref name="grid"/> and <paramref name="field"/>, changes neither. Allocates (once a match).
    /// </summary>
    /// <param name="seed">Taken as it is: the server passes the match seed ^ <see cref="SeedSalt"/>.</param>
    /// <exception cref="InvalidOperationException">No cell is land: the spawn points don't stand on open, dry cells (a broken map).</exception>
    public static ZoneRing[] Pick(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed) =>
        Pick(field, grid, spawns, seed, out _);

    /// <summary>As <see cref="Pick(Battlefield, NavGrid, IReadOnlyList{SpawnPoint}, uint)"/>; <paramref name="scanned"/>: the rings whose draws all missed (tests).</summary>
    internal static ZoneRing[] Pick(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed, out int scanned)
    {
        bool[] land = Land(field, grid, spawns);
        if (Array.IndexOf(land, true) < 0)
            throw new InvalidOperationException("No cell of the map is land the spawn points reach: they must stand on open, dry cells.");
        var rings = new ZoneRing[1 + ZoneSchedule.Rings];
        rings[0] = ZoneSchedule.Outer(field);
        var random = new XorShift(seed);
        scanned = 0;
        for (int k = 1; k <= ZoneSchedule.Rings; k++)
        {
            ZoneRing outer = rings[k - 1];
            float radius = MathF.Min(ZoneSchedule.Stage(k).Radius, outer.Radius);
            float reach = outer.Radius - radius;
            bool drawn = false;
            Vector2 centre = outer.Centre;
            for (int attempt = 0; attempt < Tries && !drawn; attempt++)
            {
                float angle = 2f * MathF.PI * random.NextFloat(), distance = reach * MathF.Sqrt(random.NextFloat());
                Vector2 c = outer.Centre + distance * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                if (!IsDryLand(field, grid, land, c)) continue;
                (centre, drawn) = (c, true);
            }
            if (!drawn)
            {
                scanned++;
                // No land cell in the disc at all keeps ring k − 1's centre (still nested). That can't happen on the riverland: ring 1's
                // disc is 386 m round the map's middle, and from ring 2 on ring k − 1's centre lies in a land cell, whose centre is within
                // half a cell's diagonal (2.9 m) of it, well inside every disc there (≥ 60 m), so the scan finds that cell at least. Only
                // a map so small that the radii are clamped to ring 0's (the sandbox: discs of 0 m, never a royale) can get here.
                if (NearestLand(grid, land, outer.Centre, reach, out Vector2 nearest)) centre = nearest;
            }
            rings[k] = new ZoneRing(centre, radius);
        }
        return rings;
    }

    /// <summary>
    /// Per grid cell (j × CellsX + i): open on <paramref name="grid"/>, dry at its centre (<c>WaterDepthAt ≤ 0</c>) and reachable from
    /// a spawn point's cell (<see cref="NavGrid.TryNearestOpen"/> within 12 m) over open cells, 8 ways, a diagonal only where both side
    /// cells are open (as <see cref="PathFinder"/> moves). Water cells are passed through (the fords), never kept. Allocates.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="grid"/> is not the size of <paramref name="field"/>'s grid.</exception>
    public static bool[] Land(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(spawns);
        int width = grid.CellsX, depth = grid.CellsZ;
        if (width != (int)(field.Terrain.SizeX / NavGrid.CellSize) || depth != (int)(field.Terrain.SizeZ / NavGrid.CellSize))
            throw new ArgumentException($"A {width} × {depth} grid is not the {field.Terrain.SizeX} × {field.Terrain.SizeZ} m field's.", nameof(grid));

        var seen = new bool[width * depth];
        var queue = new int[width * depth];   // every cell joins it at most once
        int head = 0, tail = 0;
        foreach (SpawnPoint spawn in spawns)
        {
            if (!grid.TryNearestOpen(spawn.Position, SpawnSnap, out int si, out int sj)) continue;
            int start = sj * width + si;
            if (seen[start]) continue;
            seen[start] = true;
            queue[tail++] = start;
        }

        var land = new bool[width * depth];
        while (head < tail)
        {
            int cell = queue[head++];
            int ci = cell % width, cj = cell / width;
            land[cell] = field.WaterDepthAt(NavGrid.CentreOf(ci, cj)) <= 0f;
            for (int s = 0; s < StepI.Length; s++)
            {
                int ni = ci + StepI[s], nj = cj + StepJ[s];
                if (!grid.IsOpen(ni, nj)) continue;
                if (s >= Straights && (!grid.IsOpen(ni, cj) || !grid.IsOpen(ci, nj))) continue;   // no corner cutting
                int next = nj * width + ni;
                if (seen[next]) continue;
                seen[next] = true;
                queue[tail++] = next;
            }
        }
        return land;
    }

    /// <summary>Whether <paramref name="p"/> is dry and in a <see cref="Land"/> cell.</summary>
    private static bool IsDryLand(Battlefield field, NavGrid grid, bool[] land, Vector2 p)
    {
        (int i, int j) = NavGrid.CellOf(p);
        return (uint)i < (uint)grid.CellsX && (uint)j < (uint)grid.CellsZ && land[j * grid.CellsX + i] && field.WaterDepthAt(p) <= 0f;
    }

    /// <summary>
    /// The centre of the <see cref="Land"/> cell nearest <paramref name="from"/> among those whose centres lie within
    /// <paramref name="reach"/> of it; ties: lower j, then lower i (row order, the first kept). False when there is none.
    /// </summary>
    private static bool NearestLand(NavGrid grid, bool[] land, Vector2 from, float reach, out Vector2 centre)
    {
        centre = from;
        int i0 = Math.Max(0, (int)MathF.Floor((from.X - reach) / NavGrid.CellSize)), i1 = Math.Min(grid.CellsX - 1, (int)MathF.Floor((from.X + reach) / NavGrid.CellSize));
        int j0 = Math.Max(0, (int)MathF.Floor((from.Y - reach) / NavGrid.CellSize)), j1 = Math.Min(grid.CellsZ - 1, (int)MathF.Floor((from.Y + reach) / NavGrid.CellSize));
        float best = reach * reach;
        bool found = false;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            if (!land[j * grid.CellsX + i]) continue;
            Vector2 c = NavGrid.CentreOf(i, j);
            float d = Vector2.DistanceSquared(c, from);
            if (d > best || (found && d == best)) continue;
            (best, centre, found) = (d, c, true);
        }
        return found;
    }
}
