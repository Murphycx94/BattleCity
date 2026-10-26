using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps.Building;
using Tank.Sim.Maps.Riverland;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps;

/// <summary>
/// 1200 m × 1200 m "riverland" (riverland design §3): a river from north to south-east with two fords, a lake in the
/// south-west, a plateau with two ramps in the north-west, hills, dunes in the south-east, roads and dirt tracks, a town
/// on both banks, three villages and five farms; rocks, bush patches, five forests and lone trees.
/// Everything is generated from <see cref="Seed"/> in a fixed order, so <see cref="Create"/> gives the same map every time
/// on one platform; another CPU or maths library may round its sines, cosines and arctangents differently and move
/// things by a hair. That never splits a match: clients don't generate match maps, they build the server's
/// <see cref="MapData"/> (<see cref="MapDefinition.Data"/>).
/// </summary>
public static class RiverlandMap
{
    public const string Id = "riverland";
    public const int Seed = 20261019;
    public const int Samples = 601;
    public const float CellSize = 2f;
    public const float Border = 30f;
    public const float WaterLevel = 0f;

    // ---- spawns and bot waypoints (riverland design §3.10) ----
    // Found with the riverland probe (dev scripts) and checked by RiverlandSpawnTests: flat ground (under 8° within 10 m),
    // at least 10 m from every rock, house, wall, fence, tree, the water and the houses' plots, outside the forests, all
    // in one piece of passable ground; the spawns also 15 m or more from every rock.

    /// <summary>
    /// Team 0 west, under the plateau's south cliff; team 1 east, between the east forest and the dunes: 899 m apart,
    /// five spots each 8–14 m apart in a pentagon whose tip points at the other team.
    /// </summary>
    private static readonly Vector2 Team0Centre = new(200, 376), Team1Centre = new(984, 816);
    private static readonly Vector2[] Team0Spots = { new(207, 376), new(202, 383), new(194, 380), new(194, 372), new(202, 369) };
    private static readonly Vector2[] Team1Spots = { new(977, 816), new(982, 823), new(990, 820), new(990, 812), new(982, 809) };

    /// <summary>Sixteen spots for a free-for-all, spread over the map at least 268 m apart, each facing the middle.</summary>
    private static readonly Vector2[] SoloSpots =
    {
        new(1140, 60), new(116, 96), new(636, 160), new(356, 280), new(1132, 360), new(580, 428), new(60, 444), new(856, 480),
        new(372, 604), new(1140, 808), new(584, 816), new(876, 868), new(268, 892), new(500, 1104), new(60, 1116), new(784, 1140),
    };

    // After the spot arrays: static initialisers run in order.
    /// <summary>Where the tanks of a match start: <see cref="SpawnLayout.Team"/> 5 west (team 0) + 5 east (team 1), <see cref="SpawnLayout.Solo"/> 16.</summary>
    public static readonly SpawnLayout Spawns = new(TeamSpawns(), SoloSpawns());

    /// <summary>Forty points on open ground for the bots to drive between, at least 137 m apart, 8 to 12 in each quarter of the map.</summary>
    public static readonly IReadOnlyList<Vector2> Waypoints = new Vector2[]
    {
        new(428, 60), new(1140, 60), new(880, 68), new(112, 96), new(276, 132), new(636, 160), new(1016, 208), new(848, 212),
        new(520, 276), new(160, 280), new(356, 280), new(748, 320), new(992, 356), new(1132, 364), new(580, 428), new(60, 444),
        new(256, 444), new(420, 444), new(716, 456), new(852, 484), new(372, 604), new(172, 616), new(564, 624), new(760, 684),
        new(248, 740), new(424, 764), new(1000, 804), new(1140, 808), new(584, 816), new(748, 824), new(876, 872), new(60, 880),
        new(272, 896), new(448, 932), new(708, 964), new(644, 1096), new(500, 1104), new(60, 1120), new(284, 1140), new(784, 1140),
    };

    // ---- the test drive (riverland design §3.11) ----

    /// <summary>How far west of the town, along the east-west road, the test drive starts.</summary>
    private const float SpawnBeforeTown = 150f;

    /// <summary>The test drive's spawn: on the east-west road's centre line, <see cref="SpawnBeforeTown"/> m before the town.</summary>
    public static readonly Vector2 Spawn = RoadPointBefore(L.EastWestRoad, L.Town, SpawnBeforeTown);

    /// <summary>Faces east, down the road into the town.</summary>
    public static readonly float SpawnYaw = Angles.YawOf(new Vector2(1, 0));

    /// <summary>
    /// Eight practice targets (riverland design §3.11), in this order: a medium tank on the road at the town's west end,
    /// facing the spawn 80 m away; a heavy tank in the town behind a house that hides it from the spawn until it falls; a
    /// light tank side-on in the town's ford; a tank destroyer under a tree at the east forest's edge across the river; a
    /// medium tank in a bush on the west bank; a heavy tank on the plateau's south-east corner, 272 m away and 23 m up; a
    /// light tank on the west bank's beach; a medium tank side-on in the open, about 230 m south-east (mostly south) of the
    /// spawn. All face the spawn unless turned; all stand clear of rocks, houses, walls, fences and tree trunks.
    /// </summary>
    public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
    {
        Facing(new Vector2(630, 553), turnDegrees: 0, TankCatalog.Medium),
        Facing(new Vector2(655, 591), turnDegrees: 0, TankCatalog.Heavy),
        Facing(new Vector2(700, 554), turnDegrees: 90, TankCatalog.Light),
        Facing(new Vector2(884.5f, 578), turnDegrees: 0, TankCatalog.Td),
        Facing(new Vector2(604, 404), turnDegrees: 0, TankCatalog.Medium),
        Facing(new Vector2(413, 316), turnDegrees: 0, TankCatalog.Heavy),
        Facing(new Vector2(641, 694), turnDegrees: 45, TankCatalog.Light),
        Facing(new Vector2(618, 770), turnDegrees: 90, TankCatalog.Medium),
    };

    /// <summary>The indices in <see cref="TargetSpots"/> of the targets, by where they stand.</summary>
    public const int RoadTarget = 0, HouseTarget = 1, FordTarget = 2, ForestTarget = 3, BushTarget = 4,
                     PlateauTarget = 5, BeachTarget = 6, FieldTarget = 7;

    /// <summary>
    /// Places on the map the test drive's cameras and the tests refer to: the town, its ford and the dune track's, the
    /// east forest's west edge where the east-west road enters it, the plateau's south-east corner up on its rim, the
    /// dunes, the lake and the middle of the map.
    /// </summary>
    public static readonly RiverlandLandmarks Landmarks = new(
        Town: L.Town,
        Ford: L.Fords[0].Centre,
        SouthFord: L.Fords[1].Centre,
        ForestEdge: ForestEdgeOnRoad(),
        PlateauRim: PlateauCornerRim(),
        Dunes: new Vector2(1015, 1025),
        Lake: L.LakeCentre,
        MapCentre: new Vector2(L.Size / 2, L.Size / 2));

    /// <summary>How far a forest's ragged outline reaches in and out of its rectangle in <see cref="Forests"/>, m.</summary>
    public const float ForestJitter = L.ForestJitter;

    /// <summary>
    /// The five forests (riverland design §3.7) by name, each the rectangle its noisy outline is drawn round (up to
    /// <see cref="ForestJitter"/> m in or out): east, west, north, north-east, centre. The client mixes its trees' kinds by them.
    /// </summary>
    public static readonly IReadOnlyList<RiverlandForest> Forests = PublicForests();

    /// <summary>
    /// Generates the whole map (riverland design §3.12): the ground, its kinds and the water with the houses' plots
    /// levelled; then the rocks, bushes and trees around the houses. Props are numbered from 1 by kind, whatever order
    /// they were placed in: rocks, bushes, trees, houses, then the yard walls and fences. This is the generator's raw
    /// output; games play on <see cref="MapCatalog.Riverland"/>'s, its heights rounded to whole centimetres.
    /// </summary>
    public static Battlefield Create()
    {
        RiverlandShape shape = RiverlandTerrain.Build();
        SurfaceKind[] surface = RiverlandSurface.Build(shape);
        var terrain = new HeightField(Samples, Samples, CellSize, shape.Heights);
        List<Prop> props = RiverlandScatter.Place(shape, terrain, firstId: 1);
        props.AddRange(shape.Villages.HouseProps(props.Count + 1));
        props.AddRange(shape.Villages.WallProps(terrain, props.Count + 1));
        return new Battlefield(terrain, props, Border, new SurfaceMap(Samples - 1, Samples - 1, CellSize, surface), WaterLevel);
    }

    private static SpawnPoint[] TeamSpawns()
    {
        var points = new SpawnPoint[Team0Spots.Length + Team1Spots.Length];
        for (int i = 0; i < Team0Spots.Length; i++) points[i] = new SpawnPoint(Team0Spots[i], Angles.YawOf(Team1Centre - Team0Spots[i]), 0);
        for (int i = 0; i < Team1Spots.Length; i++) points[Team0Spots.Length + i] = new SpawnPoint(Team1Spots[i], Angles.YawOf(Team0Centre - Team1Spots[i]), 1);
        return points;
    }

    private static SpawnPoint[] SoloSpawns()
    {
        var middle = new Vector2(L.Size / 2, L.Size / 2);
        return SoloSpots.Select(p => new SpawnPoint(p, Angles.YawOf(middle - p), 0)).ToArray();
    }

    /// <summary>A <paramref name="spec"/> target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
    private static TargetSpot Facing(Vector2 position, float turnDegrees, TankSpec spec) =>
        new(position, Angles.Wrap(Angles.YawOf(Spawn - position) + Angles.Deg(turnDegrees)), spec);

    /// <summary>The point <paramref name="distance"/> m back along <paramref name="road"/> from where it passes nearest <paramref name="place"/>.</summary>
    private static Vector2 RoadPointBefore(RoadLine road, Vector2 place, float distance)
    {
        road.Line.DistanceTo(place, out float along);
        return road.Line.PointAt(along - distance);
    }

    /// <summary>Where the east-west road, heading east from the town's ford, first enters the east forest's outline.</summary>
    private static Vector2 ForestEdgeOnRoad()
    {
        Polyline road = L.EastWestRoad.Line;
        road.DistanceTo(L.Fords[0].Centre, out float along);
        for (; along < road.Length; along += 1f)
            if (L.ForestDepth(L.Forests[0], road.PointAt(along)) > 0) return road.PointAt(along);
        throw new InvalidOperationException("The east-west road never enters the east forest.");
    }

    private static RiverlandForest[] PublicForests()
    {
        string[] names = { "east", "west", "north", "north-east", "centre" };   // in RiverlandLayout.Forests' order
        if (names.Length != L.Forests.Length) throw new InvalidOperationException("Every forest needs a name.");
        return L.Forests.Select((forest, i) => new RiverlandForest(names[i], forest.Min, forest.Max)).ToArray();
    }

    /// <summary>How far past the top of the cliff <see cref="RiverlandLandmarks.PlateauRim"/> lies, m.</summary>
    private const float RimInset = 3f;

    /// <summary>
    /// The plateau's south-east corner, up on its rim: walking from the corner of its rectangle towards its middle, the
    /// first point <see cref="RimInset"/> m past the top of the cliff.
    /// </summary>
    private static Vector2 PlateauCornerRim()
    {
        Vector2 corner = new(L.Plateau.Max.X, L.Plateau.Max.Y), inward = Vector2.Normalize(L.Plateau.Centre - corner);
        for (float d = 0; d < 100; d += 1f)
        {
            Vector2 p = corner + inward * d;
            if (L.PlateauEdgeDistance(p) <= -L.CliffWidth - RimInset) return p;
        }
        throw new InvalidOperationException("No plateau top near its south-east corner.");
    }
}

/// <summary>Named places on the riverland (see <see cref="RiverlandMap.Landmarks"/>), ground positions: X is world X, Y is world Z.</summary>
public sealed record RiverlandLandmarks(Vector2 Town, Vector2 Ford, Vector2 SouthFord, Vector2 ForestEdge,
    Vector2 PlateauRim, Vector2 Dunes, Vector2 Lake, Vector2 MapCentre);

/// <summary>One of the riverland's forests (see <see cref="RiverlandMap.Forests"/>): its rectangle on the ground, X world X, Y world Z.</summary>
public sealed record RiverlandForest(string Name, Vector2 Min, Vector2 Max);
