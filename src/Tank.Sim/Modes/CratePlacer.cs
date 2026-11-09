using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Navigation;

namespace Tank.Sim.Modes;

/// <summary>
/// Places a match's crates from the match seed (stage 6 design §4.1): <see cref="Count"/> of them, <see cref="HouseShare"/> beside
/// the standing houses (<see cref="HouseMin"/>–<see cref="HouseMax"/> m from a footprint), the rest by the roads and at the
/// forests' edges, <see cref="Spacing"/> apart, on ground that is <see cref="Clear"/> (dry, level, open on the navigation grid and
/// reachable from the spawn points, off the props, away from the spawns and the edge). It reads the navigation grid, so it runs
/// only on the server and in the simulation's tests; clients learn crates over the wire. Deterministic for a seed: only its own
/// <see cref="XorShift"/> and the field's state are read, so the same seed on the same field gives the same crates. Allocates
/// (once a match); the draws themselves don't.
/// </summary>
public static class CratePlacer
{
    /// <summary>The server draws with XorShift(matchSeed ^ SeedSalt), so the crates don't follow the spawn order or the rings drawn from the same seed.</summary>
    public const uint SeedSalt = 0x0C8A_7E55u;

    public const int Count = 48;

    /// <summary>29 of 48 by a house, 19 by roads and forest edges.</summary>
    public const float HouseShare = 0.6f;

    /// <summary>Half of the non-house crates, rounded up, go by roads (10 of 19); the rest by forests (9).</summary>
    public const float RoadShare = 0.5f;

    /// <summary>Metres from the house's footprint.</summary>
    public const float HouseMin = 4f, HouseMax = 15f;

    /// <summary>A Road surface sample within this many metres: "by the road".</summary>
    public const float RoadReach = 8f;

    /// <summary>At least <see cref="ForestTrees"/> trunks within <see cref="ForestReach"/> m and none within <see cref="TrunkClear"/>: "by the forest".</summary>
    public const float ForestReach = 20f;
    public const int ForestTrees = 3;

    /// <summary>No tree trunk within 3 m; blockers' footprints at least 2 m away (rocks, walls, fences, houses: <see cref="HouseMin"/> covers houses).</summary>
    public const float TrunkClear = 3f, PropClear = 2f;

    /// <summary>Between crates; from any spawn point (or they'd be eaten at tick 0); inside the playable area.</summary>
    public const float Spacing = 25f, SpawnClear = 20f, EdgeMargin = 10f;

    public const float MaxSlopeDegrees = 8f;

    /// <summary>Draws for one match, half of them for the house crates; fewer than <see cref="Count"/> crates when they run out (the caller logs it).</summary>
    public const int Attempts = 40_000;

    /// <summary>The kinds' shares, in <see cref="CrateKind"/> order: repair 25 %, ammo, armour and reload 20 %, optics 15 % (design §4.1).</summary>
    private static readonly int[] SharePercent = { 25, 20, 20, 20, 15 };

    /// <summary>The road samples' step, m (the surface map's cell).</summary>
    private const float RoadStep = 2f;

    /// <summary>The blocker query's radius: everything whose footprint could be within <see cref="PropClear"/> is found well inside it.</summary>
    private const float BlockerSearch = 12f;

    /// <summary>The slope is checked at the point and this far either side of it along both axes.</summary>
    private const float SlopeStep = 1f;

    private static readonly float MinNormalY = MathF.Cos(MaxSlopeDegrees * MathF.PI / 180f);

    /// <summary>
    /// The kinds for <paramref name="count"/> crates: each kind's share (<see cref="SharePercent"/>) rounded to the nearest whole crate,
    /// half up, the rounding's surplus taken off the rarest kinds (optics first) and a shortfall made up with repair crates, so 48 →
    /// 12, 10, 10, 10, 6 and 10 → 3, 2, 2, 2, 1; then shuffled (Fisher–Yates) with <paramref name="rng"/>.
    /// </summary>
    public static CrateKind[] Kinds(int count, ref XorShift rng)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Span<int> share = stackalloc int[CrateRules.Kinds];
        int total = 0;
        for (int k = 0; k < share.Length; k++)
        {
            share[k] = (count * SharePercent[k] + 50) / 100;
            total += share[k];
        }
        for (int k = share.Length - 1; total > count && k >= 0; k--)
        {
            int surplus = Math.Min(share[k], total - count);
            share[k] -= surplus;
            total -= surplus;
        }
        share[0] += count - total;

        var kinds = new CrateKind[count];
        int n = 0;
        for (int k = 0; k < share.Length; k++)
            for (int c = 0; c < share[k]; c++)
                kinds[n++] = (CrateKind)k;
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        return kinds;
    }

    /// <summary>
    /// <see cref="Count"/> crates (fewer only if the map runs out of places within <see cref="Attempts"/> draws: logged by the caller),
    /// ids 0..n−1 in the order placed, house crates first. The house crates: a standing house drawn at random, a point drawn
    /// <see cref="HouseMin"/>–<see cref="HouseMax"/> m beyond its footprint in a random direction, kept if <see cref="ByHouse"/>,
    /// <see cref="Clear"/> and <see cref="Spacing"/> from the crates so far, until round(Count × HouseShare) stand or half the draws are
    /// spent (none when the map has no house). The rest: points drawn uniformly over the playable area less <see cref="EdgeMargin"/>,
    /// not by a house, clear and spaced: first the road crates (<see cref="RoadShare"/> of the rest, rounded half up: <see cref="ByRoad"/>),
    /// then the forest crates (the remainder: <see cref="ByForest"/>), so the ids run house → road → forest. The open phases share
    /// the draws the house phase left: at most half of them on the roads, the remainder on the forests (an exhausted road phase, as
    /// on a map without roads, can't starve the forests); a point by both a road and a forest serves either phase. The three
    /// quotas (29 houses / 10 roads / 9 forests on the riverland) are targets, not guarantees: a phase that runs out of draws
    /// is made up by the next, and fewer than <see cref="Count"/> come back when <see cref="Attempts"/> run out (the caller logs
    /// it); riverland seeds 1–50 fill all three, within 1091 of the 40 000 draws. Reads <paramref name="field"/> and
    /// <paramref name="grid"/>, changes neither.
    /// </summary>
    /// <param name="seed">Taken as it is: the server passes the match seed ^ <see cref="SeedSalt"/>.</param>
    public static Crate[] Place(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(spawns);
        return Place(field, grid, spawns, seed, ZonePicker.Land(field, grid, spawns));
    }

    /// <summary>As <see cref="Place(Battlefield, NavGrid, IReadOnlyList{SpawnPoint}, uint)"/> with the land (<see cref="ZonePicker.Land"/>) computed already (tests).</summary>
    internal static Crate[] Place(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed, bool[] land) =>
        Place(field, grid, spawns, seed, land, out _);

    /// <summary>As above; <paramref name="attempts"/>: the draws spent (tests).</summary>
    internal static Crate[] Place(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed, bool[] land, out int attempts)
    {
        RequireLand(grid, land);
        var rng = new XorShift(seed);
        CrateKind[] kinds = Kinds(Count, ref rng);
        var houses = new List<int>();
        for (int i = 0; i < field.Props.Count; i++)
            if (field.Props[i].Kind == PropKind.House && field.StandingAt(i)) houses.Add(i);
        int houseQuota = houses.Count == 0 ? 0 : (int)MathF.Round(Count * HouseShare);

        var placed = new Vector2[Count];
        int n = 0;
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        attempts = 0;
        for (int houseDraws = Attempts / 2; n < houseQuota && attempts < houseDraws; attempts++)
        {
            ref readonly Prop house = ref field.PropAt(houses[(int)(rng.NextUInt() % (uint)houses.Count)]);
            float angle = rng.NextFloat() * MathF.Tau, distance = HouseMin + rng.NextFloat() * (HouseMax - HouseMin);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 p = house.Footprint.Center + direction * (house.Footprint.Radius(direction) + distance);
            if (!IsSpaced(placed, n, p) || !ByHouse(field, p, buffer) || !Clear(field, grid, land, spawns, p, buffer)) continue;
            placed[n++] = p;
        }
        int rest = Count - houseQuota, roadQuota = (int)MathF.Round(rest * RoadShare, MidpointRounding.AwayFromZero);
        float low = field.Border + EdgeMargin, spanX = field.Terrain.SizeX - 2f * low, spanZ = field.Terrain.SizeZ - 2f * low;
        for (int roadDraws = attempts + (Attempts - attempts) / 2, roadGoal = n + roadQuota; n < roadGoal && attempts < roadDraws; attempts++)
        {
            var p = new Vector2(low + rng.NextFloat() * spanX, low + rng.NextFloat() * spanZ);
            if (!IsSpaced(placed, n, p) || !ByRoad(field, p) || ByHouse(field, p, buffer) || !Clear(field, grid, land, spawns, p, buffer)) continue;
            placed[n++] = p;
        }
        for (; n < Count && attempts < Attempts; attempts++)
        {
            var p = new Vector2(low + rng.NextFloat() * spanX, low + rng.NextFloat() * spanZ);
            if (!IsSpaced(placed, n, p) || !ByForest(field, p, buffer) || ByHouse(field, p, buffer) || !Clear(field, grid, land, spawns, p, buffer)) continue;
            placed[n++] = p;
        }

        var crates = new Crate[n];
        for (int i = 0; i < n; i++) crates[i] = new Crate((byte)i, placed[i], kinds[i]);
        return crates;
    }

    /// <summary>Whether some standing house's footprint is <see cref="HouseMin"/>–<see cref="HouseMax"/> m from <paramref name="p"/>.</summary>
    public static bool ByHouse(Battlefield field, Vector2 p)
    {
        ArgumentNullException.ThrowIfNull(field);
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        return ByHouse(field, p, buffer);
    }

    private static bool ByHouse(Battlefield field, Vector2 p, Span<int> buffer)
    {
        foreach (int i in field.Near(PropSet.Blockers, p, HouseMax, buffer))
        {
            ref readonly Prop prop = ref field.PropAt(i);
            if (prop.Kind != PropKind.House || !field.StandingAt(i)) continue;
            float d = prop.Footprint.DistanceToPoint(p);
            if (d >= HouseMin && d <= HouseMax) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="p"/> is on a road, or a point of the <see cref="RoadStep"/> m lattice round it within <see cref="RoadReach"/> is.</summary>
    public static bool ByRoad(Battlefield field, Vector2 p)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.SurfaceAt(p) == SurfaceKind.Road) return true;
        for (float dz = -RoadReach; dz <= RoadReach; dz += RoadStep)
        for (float dx = -RoadReach; dx <= RoadReach; dx += RoadStep)
            if (dx * dx + dz * dz <= RoadReach * RoadReach && field.SurfaceAt(new Vector2(p.X + dx, p.Y + dz)) == SurfaceKind.Road) return true;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="p"/> is at a wood's edge: at least <see cref="ForestTrees"/> standing trunks within <see cref="ForestReach"/>,
    /// none nearer than <see cref="TrunkClear"/> (centre to centre) and no crown (<see cref="Prop.FoliageRadius"/>) over it. The rule
    /// knows no forest outlines, so a clearing between the trunks counts too.
    /// </summary>
    public static bool ByForest(Battlefield field, Vector2 p)
    {
        ArgumentNullException.ThrowIfNull(field);
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        return ByForest(field, p, buffer);
    }

    private static bool ByForest(Battlefield field, Vector2 p, Span<int> buffer)
    {
        int trunks = 0;
        foreach (int i in field.Near(PropSet.Foliage, p, ForestReach, buffer))
        {
            ref readonly Prop prop = ref field.PropAt(i);
            if (prop.Kind != PropKind.Tree || !field.StandingAt(i)) continue;
            float d = Vector2.Distance(prop.Footprint.Center, p);
            if (d < TrunkClear || d <= prop.FoliageRadius) return false;
            if (d <= ForestReach) trunks++;
        }
        return trunks >= ForestTrees;
    }

    /// <summary>
    /// The ground is fit for a crate: dry, slope at most <see cref="MaxSlopeDegrees"/> (<see cref="Terrain.HeightField.NormalAt"/> at the
    /// point and <see cref="SlopeStep"/> m either side of it), inside [Border + EdgeMargin, Size − Border − EdgeMargin]², the nav cell open
    /// and in <paramref name="land"/> (<see cref="ZonePicker.Land"/>: reachable dry land), no standing blocker's footprint within
    /// <see cref="PropClear"/>, no standing trunk within <see cref="TrunkClear"/>, no spawn point within <see cref="SpawnClear"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="land"/> is not one flag per cell of <paramref name="grid"/>.</exception>
    public static bool Clear(Battlefield field, NavGrid grid, bool[] land, IReadOnlyList<SpawnPoint> spawns, Vector2 p)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(spawns);
        RequireLand(grid, land);
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        return Clear(field, grid, land, spawns, p, buffer);
    }

    private static bool Clear(Battlefield field, NavGrid grid, bool[] land, IReadOnlyList<SpawnPoint> spawns, Vector2 p, Span<int> buffer)
    {
        float low = field.Border + EdgeMargin;
        if (!(p.X >= low && p.X <= field.Terrain.SizeX - low && p.Y >= low && p.Y <= field.Terrain.SizeZ - low)) return false;   // NaN fails too
        if (field.WaterDepthAt(p) > 0f || !IsLevel(field, p)) return false;
        (int i, int j) = NavGrid.CellOf(p);
        if (!grid.IsOpen(i, j) || !land[j * grid.CellsX + i]) return false;
        for (int s = 0; s < spawns.Count; s++)
            if (Vector2.DistanceSquared(spawns[s].Position, p) < SpawnClear * SpawnClear) return false;
        foreach (int k in field.Near(PropSet.Blockers, p, BlockerSearch, buffer))
        {
            ref readonly Prop prop = ref field.PropAt(k);
            if (prop.Kind == PropKind.Tree || !field.StandingAt(k)) continue;   // trunks: below
            if (prop.Footprint.DistanceToPoint(p) < PropClear) return false;
        }
        foreach (int k in field.Near(PropSet.Foliage, p, TrunkClear, buffer))
        {
            ref readonly Prop prop = ref field.PropAt(k);
            if (prop.Kind == PropKind.Tree && field.StandingAt(k) && Vector2.DistanceSquared(prop.Footprint.Center, p) < TrunkClear * TrunkClear) return false;
        }
        return true;
    }

    private static bool IsLevel(Battlefield field, Vector2 p) =>
        field.Terrain.NormalAt(p.X, p.Y).Y >= MinNormalY
        && field.Terrain.NormalAt(p.X + SlopeStep, p.Y).Y >= MinNormalY && field.Terrain.NormalAt(p.X - SlopeStep, p.Y).Y >= MinNormalY
        && field.Terrain.NormalAt(p.X, p.Y + SlopeStep).Y >= MinNormalY && field.Terrain.NormalAt(p.X, p.Y - SlopeStep).Y >= MinNormalY;

    private static bool IsSpaced(Vector2[] placed, int count, Vector2 p)
    {
        for (int k = 0; k < count; k++)
            if (Vector2.DistanceSquared(placed[k], p) < Spacing * Spacing) return false;
        return true;
    }

    private static void RequireLand(NavGrid grid, bool[] land)
    {
        ArgumentNullException.ThrowIfNull(land);
        if (land.Length != grid.CellsX * grid.CellsZ)
            throw new ArgumentException($"{land.Length} land flags for a {grid.CellsX} × {grid.CellsZ} grid.", nameof(land));
    }
}
