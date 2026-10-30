using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;

namespace Tank.Net.Tests.Battle;

/// <summary>Small map definitions for the server's map tests: quick to generate, with spawns and waypoints of their own.</summary>
internal static class TestMaps
{
    /// <summary>The small map's centre: the team spawns face across it, the free-for-all ones towards it.</summary>
    private static readonly Vector2 Middle = new(100, 100);

    /// <summary>Team 0 west, team 1 east, two each, facing each other.</summary>
    public static readonly IReadOnlyList<SpawnPoint> TeamSpawns = new SpawnPoint[]
    {
        new(new Vector2(40, 100), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(40, 110), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(160, 100), Angles.YawOf(-Vector2.UnitX), 1),
        new(new Vector2(160, 110), Angles.YawOf(-Vector2.UnitX), 1),
    };

    /// <summary>
    /// Four corners for a free-for-all, each facing the middle; the first two (where the first two tanks start) are
    /// opposite, 226 m apart, so two tanks standing still don't see each other.
    /// </summary>
    public static readonly IReadOnlyList<SpawnPoint> SoloSpawns = new[] { new Vector2(20, 20), new Vector2(180, 180), new Vector2(20, 180), new Vector2(180, 20) }
        .Select(p => new SpawnPoint(p, Angles.YawOf(Middle - p), 0))
        .ToArray();

    public static readonly SpawnLayout Spawns = new(TeamSpawns, SoloSpawns);

    /// <summary>Four points between the spawns, none on one: at least 36 m from the team spawns, 80 m from the free-for-all ones.</summary>
    public static readonly IReadOnlyList<Vector2> Waypoints = new Vector2[] { new(60, 140), new(140, 60), new(100, 20), new(100, 180) };

    /// <summary>A server with a fixed seed whose room starts on <paramref name="map"/>, the only map it may switch to.</summary>
    public static BattleServerOptions Options(MapDefinition map) => new() { Seed = 1, Map = map, Maps = new[] { map } };

    /// <summary>
    /// A map whose generator throws "no ground" (a bug the server must survive), once <paramref name="gate"/> opens if
    /// one is given (at most 30 s).
    /// </summary>
    public static MapDefinition Broken(string id, ManualResetEventSlim? gate = null) => new(id, id, () =>
    {
        gate?.Wait(TimeSpan.FromSeconds(30));
        throw new InvalidOperationException("no ground");
    }, Spawns, Waypoints);

    /// <summary>A 200 m × 200 m flat map (101 × 101 samples, 2 m apart, border 4) without props.</summary>
    public static MapDefinition Small(string id) => new(id, id, SmallField, Spawns, Waypoints);

    /// <summary>
    /// The small map, but its generator first calls <paramref name="onCreate"/> and then waits for
    /// <paramref name="gate"/> (at most 30 s, so a failed test can't leave a worker blocked for good).
    /// </summary>
    public static MapDefinition Gated(string id, ManualResetEventSlim gate, Action onCreate) => new(id, id, () =>
    {
        onCreate();
        if (!gate.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException($"The gate of map {id} was never opened.");
        return SmallField();
    }, Spawns, Waypoints);

    private static Battlefield SmallField() => TestWorld.FlatField();

    // ---- the small map with bases, for standard battles (stage 5 design §3) ----

    /// <summary>
    /// The small map's bases, both between the spawns (team 0's spawns at x 40, team 1's at x 160): each 35 m from the
    /// other team's spawns, so an attacker is in the circle 20 m after setting off. (A test map need not keep a base
    /// behind its own team.)
    /// </summary>
    public static readonly IReadOnlyList<TeamBase> StandardBases = new TeamBase[]
    {
        new(0, new Vector2(125, 105), TeamBase.StandardRadius),
        new(1, new Vector2(75, 105), TeamBase.StandardRadius),
    };

    /// <summary>The wall of <see cref="StandardWalled"/>: stone, north–south along this x from z 20 to z 190.</summary>
    public const float StandardWallX = 115f;

    /// <summary>The small map with <see cref="StandardBases"/>.</summary>
    public static MapDefinition Standard(string id) => new(id, id, SmallField, Spawns, Waypoints, StandardBases);

    /// <summary>
    /// <see cref="Standard"/> with a stone wall along x <see cref="StandardWallX"/> (z 20–190): it hides team 1's base
    /// (x 75) and whoever is in it from team 1's spawns (x 160).
    /// </summary>
    public static MapDefinition StandardWalled(string id) => new(id, id, WalledField, Spawns, Waypoints, StandardBases);

    /// <summary>
    /// A royale's three solo spawns on the walled map: two west of the wall 30 m apart and facing east (one in the other's
    /// sights), one east of it facing west, which the wall hides from both.
    /// </summary>
    public static readonly IReadOnlyList<SpawnPoint> RoyaleSoloSpawns = new SpawnPoint[]
    {
        new(new Vector2(40, 100), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(70, 100), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(170, 100), Angles.YawOf(-Vector2.UnitX), 0),
    };

    /// <summary>
    /// A stand-in for the riverland in the server's royale tests: the walled small map under the riverland's id (a royale is
    /// held there only), with <see cref="RoyaleSoloSpawns"/>. Quick to generate, and shots at 30 m hit.
    /// </summary>
    public static MapDefinition RoyaleStandIn() =>
        new(RiverlandMap.Id, "河谷（测试）", WalledField, new SpawnLayout(TeamSpawns, RoyaleSoloSpawns), Waypoints);

    private static Battlefield WalledField()
    {
        HeightField terrain = TestWorld.Flat();
        Prop[] wall = new WallLine(PropKind.StoneWall, new Vector2(StandardWallX, 20), new Vector2(StandardWallX, 190)).Segments(terrain, 1).ToArray();
        return TestWorld.Field(terrain, wall);
    }

    // ---- the mixed map: road, sand, water, trees and a house (riverland design §7) ----

    public const int MixedSamples = 151;
    public const float MixedCell = 2f, MixedBorder = 6f;
    public const float MixedGround = 1f, MixedWaterLevel = 0f, DitchFloor = -1f, FordFloor = -0.5f;

    /// <summary>The ditch runs north–south along this x; its floor is 10 m either side, its sand banks 6 m beyond that.</summary>
    public const float DitchX = 150f, DitchHalfWidth = 10f, BankWidth = 6f;

    /// <summary>The road runs east–west between these z (8 m), and <see cref="RoadZ"/> is its centre line.</summary>
    public const float RoadNorth = 140f, RoadSouth = 148f, RoadZ = 144f;

    /// <summary>
    /// The tree row: <see cref="TreeCount"/> trunks <see cref="TreeSpacing"/> apart on the road's centre line, centred
    /// on x <see cref="TreeRowX"/>, so a tank driving along the road ploughs through them one after another.
    /// </summary>
    public const float TreeRowX = 200f, TreeSpacing = 7f, TreeTrunk = 0.5f, TreeHeight = 12f;
    public const int TreeCount = 10;

    /// <summary>The house: 12 m long (east–west), 7 m wide, 7.5 m to the ridge.</summary>
    public static readonly Vector2 HouseCenter = new(250, 100);
    public const float HouseLength = 12f, HouseWidth = 7f, HouseHeight = 7.5f;

    /// <summary>Team 0 west, team 1 east, the first of each on the road, facing each other.</summary>
    public static readonly IReadOnlyList<SpawnPoint> MixedTeamSpawns = new SpawnPoint[]
    {
        new(new Vector2(40, RoadZ), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(40, 160), Angles.YawOf(Vector2.UnitX), 0),
        new(new Vector2(260, RoadZ), Angles.YawOf(-Vector2.UnitX), 1),
        new(new Vector2(260, 160), Angles.YawOf(-Vector2.UnitX), 1),
    };

    private static readonly Vector2 MixedMiddle = new(150, 150);

    public static readonly IReadOnlyList<SpawnPoint> MixedSoloSpawns = new[] { new Vector2(30, 30), new Vector2(270, 270), new Vector2(30, 270), new Vector2(270, 30) }
        .Select(p => new SpawnPoint(p, Angles.YawOf(MixedMiddle - p), 0))
        .ToArray();

    public static readonly IReadOnlyList<Vector2> MixedWaypoints = new Vector2[] { new(80, 80), new(220, 220), new(80, 220), new(220, 60) };

    /// <summary>
    /// A 300 m × 300 m map (151 × 151 samples, 2 m apart, border 6) with every kind of ground and both new props: flat
    /// grass 1 m up; a ditch x 140–160 (floor −1 m under water level 0, a dirt bed) whose banks fall over 6 m of sand on
    /// either side; an east–west road z 140–148 that runs down the banks and crosses by a ford (floor −0.5 m), sand and
    /// dirt there like the riverland's; a row of ten trees on the road east of the ditch (<see cref="TreeRowX"/>) and a
    /// house at (250, 100).
    /// </summary>
    public static MapDefinition Mixed(string id = "mixed") =>
        new(id, id, MixedField, new SpawnLayout(MixedTeamSpawns, MixedSoloSpawns), MixedWaypoints);

    private static Battlefield MixedField()
    {
        const int n = MixedSamples, cells = MixedSamples - 1;
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            heights[j * n + i] = MixedHeight(i * MixedCell, j * MixedCell);

        var surface = new SurfaceKind[cells * cells];
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
            surface[j * cells + i] = MixedSurface((i + 0.5f) * MixedCell, (j + 0.5f) * MixedCell);

        var props = new List<Prop>();
        float firstTree = TreeRowX - (TreeCount - 1) * TreeSpacing / 2;
        for (int k = 0; k < TreeCount; k++)
            props.Add(new Prop(props.Count + 1, PropKind.Tree, new Obb2(new Vector2(firstTree + k * TreeSpacing, RoadZ), new Vector2(TreeTrunk), 0f),
                MixedGround - WallLine.Sink, TreeHeight + WallLine.Sink));
        props.Add(new Prop(props.Count + 1, PropKind.House, new Obb2(HouseCenter, new Vector2(HouseLength / 2, HouseWidth / 2), 0f),
            MixedGround - WallLine.Sink, HouseHeight + WallLine.Sink));

        return new Battlefield(new HeightField(n, n, MixedCell, heights), props, MixedBorder,
            new SurfaceMap(cells, cells, MixedCell, surface), MixedWaterLevel);
    }

    private static bool OnRoad(float z) => z >= RoadNorth && z <= RoadSouth;

    /// <summary>Flat ground, the ditch's floor (the ford's on the road) and its banks falling to it in a straight line.</summary>
    private static float MixedHeight(float x, float z)
    {
        float floor = OnRoad(z) ? FordFloor : DitchFloor;
        float d = MathF.Abs(x - DitchX);
        if (d <= DitchHalfWidth) return floor;
        if (d >= DitchHalfWidth + BankWidth) return MixedGround;
        return floor + (MixedGround - floor) * (d - DitchHalfWidth) / BankWidth;
    }

    private static SurfaceKind MixedSurface(float x, float z)
    {
        float d = MathF.Abs(x - DitchX);
        if (d < DitchHalfWidth) return SurfaceKind.Dirt;
        if (d < DitchHalfWidth + BankWidth) return SurfaceKind.Sand;
        return z >= RoadNorth && z < RoadSouth ? SurfaceKind.Road : SurfaceKind.Grass;
    }
}
