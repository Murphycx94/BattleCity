using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Maps;

/// <summary>
/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks, twelve wide patches of tall bushes, a walled
/// yard about 40 m west of the spawn, a few walls and fences near the spawn and seven practice targets. The spawn faces
/// the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
/// </summary>
public static class SandboxMap
{
    public const string Id = "sandbox";
    public const int Seed = 20261013;
    public const int Samples = 201;
    public const float CellSize = 2f;
    public const float Border = 10f;
    private const int RockTarget = 88, MaxRockAttempts = 200;   // place until the target, giving up after this many tries
    // Rock and bush sizes are internal: the riverland's rocks and bushes are made the same (riverland design §3.8–3.9).
    internal const float MinRockHalfX = 2f, MaxRockHalfX = 4.5f, MinRockHalfZ = 2f, MaxRockHalfZ = 5.5f;
    internal const float MinRockHeight = 2.5f, MaxRockHeight = 6f;   // above the ground; the base sinks a further RockSink
    internal const float RockSink = 1f;
    private const float RockMargin = 30f, RockSpan = (Samples - 1) * CellSize - 2 * RockMargin;   // rock centres lie this far inside the map edge
    private const float SpawnClearance = 30f;
    private const float RockPatchClearance = 8f;   // a rock's bounding circle stays this far from every bush patch centre
    private const float RockSpacing = 0.5f;        // rock centres at least this × the sum of bounding radii apart: piles, not stacks

    public static readonly Vector2 Spawn = new(200, 330);

    /// <summary>Faces the big hill to the north.</summary>
    public const float SpawnYaw = 0f;

    /// <summary>Rocks, bushes, targets, walls and fences keep this far from the straight lane between the spawn and the big hill.</summary>
    internal const float LaneHalfWidth = 6f;
    internal static readonly Vector2 BigHill = new(200, 150);

    /// <summary>
    /// Centres of the bush patches, hand-placed on flat ground (under 10° within 13 m), at least 45 m apart and spread over
    /// the whole map; the first is the one near the spawn.
    /// </summary>
    public static readonly IReadOnlyList<Vector2> BushPatches = new Vector2[]
    {
        new(222, 296), new(126, 294), new(306, 280), new(256, 214), new(350, 206), new(116, 188),
        new(278, 128), new(200, 52), new(60, 60), new(340, 60), new(60, 340), new(340, 340),
    };

    /// <summary>Every bush of a patch has its centre within this distance of the patch centre.</summary>
    public const float BushPatchRadius = 16f;
    internal const float BushSpread = 9f;          // bush centres lie within this radius, so a patch is at most ~27 m across
    private const int MinBushes = 16, MaxBushes = 22;
    internal const float MinBushRadius = 3.2f, MaxBushRadius = 4.6f;
    private const int SpawnPatch = 0;
    internal const float MinCentreBushRadius = 3.8f;   // the first bush sits exactly on the patch centre: a guaranteed hiding spot
    private const float MinSpawnPatchCentreBushRadius = 4.0f;   // the patch beside the spawn gets an extra-roomy centre bush
    internal const float MinBushHeight = 4.2f, MaxBushHeight = 5.2f;
    internal const float BushSink = 0.3f;          // footprint starts this far below the ground, like the rocks
    internal const float BushMaxSlope = 10f * MathF.PI / 180f;
    private const float BushSpawnClearance = 20f;
    internal const float BushOverlap = 0.35f;      // neighbours may overlap: centres at least this × the sum of radii apart
    internal const int BushAttemptsPerPatch = 200;

    /// <summary>
    /// Seven practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes, walls and
    /// the spawn-to-hill lane, all four kinds of tank among them. Seen from the spawn they show, in order: a medium tank's
    /// front (most range demos shoot this one), a heavy tank's side, a light tank at 45°, a tank destroyer's rear, a medium
    /// tank's front half hidden behind a rock, a heavy tank's front far away (180–200 m), and a medium tank's front
    /// inside the yard, out of sight behind its stone wall (<see cref="YardTarget"/>).
    /// </summary>
    public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
    {
        Facing(new Vector2(160, 304), turnDegrees: 0, TankCatalog.Medium),
        Facing(new Vector2(248, 300), turnDegrees: 90, TankCatalog.Heavy),
        Facing(new Vector2(216, 244), turnDegrees: 45, TankCatalog.Light),
        Facing(new Vector2(140, 256), turnDegrees: 180, TankCatalog.Td),
        Facing(new Vector2(280, 328), turnDegrees: 0, TankCatalog.Medium),
        Facing(new Vector2(380, 272), turnDegrees: 0, TankCatalog.Heavy),
        Facing(new Vector2(160, 340), turnDegrees: 0, TankCatalog.Medium),
    };

    /// <summary>The index in <see cref="TargetSpots"/> of the target inside the yard.</summary>
    public const int YardTarget = 6;

    /// <summary>The kind of tank that drives <see cref="PatrolRoute"/> (stage 3 design §4.2).</summary>
    public static TankSpec PatrolSpec => TankCatalog.Light;

    /// <summary>Where the patrolling target starts, facing its first point (the bush patch east of it).</summary>
    public static readonly Vector2 PatrolStart = new(270, 278);

    /// <summary>
    /// The patrolling target's loop, about 120 m east and north-east of the spawn: it stops 10 s in the middle of two
    /// bush patches (306, 280) and (256, 214), driving round the west of the hill between them. Clear of rocks, walls,
    /// targets and the spawn-to-hill lane; it starts at <see cref="PatrolStart"/>, the last point.
    /// </summary>
    public static readonly IReadOnlyList<PatrolPoint> PatrolRoute = new PatrolPoint[]
    {
        new(BushPatches[2], 10f),
        new(new Vector2(272, 266), 0f),
        new(new Vector2(262, 238), 0f),
        new(BushPatches[3], 10f),
        new(new Vector2(242, 238), 0f),
        new(new Vector2(242, 262), 0f),
        new(PatrolStart, 0f),
    };

    /// <summary>
    /// Where the two teams of a match start (stage 4 design §2.3): five spots each on open flat ground about 280 m apart,
    /// team 0 south-west of the middle, team 1 north-east, every tank facing the other side.
    /// </summary>
    private static readonly Vector2 Team0Centre = new(100, 300), Team1Centre = new(300, 100);
    private static readonly Vector2[] Team0Spots = { new(100, 300), new(110, 303), new(103, 310), new(93, 307), new(90, 297) };
    private static readonly Vector2[] Team1Spots = { new(300, 100), new(310, 100), new(305, 109), new(295, 109), new(290, 100) };

    /// <summary>Ten spots for a free-for-all, spread over the map at least 100 m apart, each facing the middle.</summary>
    private static readonly Vector2[] SoloSpots =
    {
        new(76, 60), new(360, 360), new(40, 360), new(352, 84), new(200, 228),
        new(48, 208), new(212, 76), new(344, 224), new(224, 360), new(132, 148),
    };

    // After the spot arrays: static initialisers run in order.
    public static readonly SpawnLayout Spawns = new(TeamSpawns(), SoloSpawns());

    /// <summary>
    /// The teams' bases for a standard battle (stage 5 design §3.2), found with the riverland probe's <c>bases</c> command
    /// and checked by MapBaseTests: flat open ground about 50 m behind each team's spawns (team 0's 51 m south-west of its
    /// nearest spot, team 1's 44 m north-east), one rock at each rim, 361 m apart.
    /// </summary>
    public static readonly IReadOnlyList<TeamBase> Bases = new TeamBase[]
    {
        new(0, new Vector2(40, 306), TeamBase.StandardRadius),
        new(1, new Vector2(300, 56), TeamBase.StandardRadius),
    };

    /// <summary>
    /// The bots' attack lanes and cover points (stage 5 design §4.2), found with the riverland probe's <c>nav</c> command
    /// and checked by MapBotDataTests on the navigation grid. After <see cref="Bases"/>: static initialisers run in order.
    /// Lanes from team 0's side (team 1 drives them backwards): <c>north</c>, <c>middle</c> and <c>south</c>, the flank
    /// (133 m off the line between the bases). Four cover points a team on the side the lanes come from.
    /// </summary>
    public static readonly BotMapData Bots = new(
        new BotLane[]
        {
            new("north", new Vector2[] { new(100, 220), new(150, 120), new(226, 70) }),
            new("middle", new Vector2[] { new(166, 238), new(240, 160) }),
            new("south", new Vector2[] { new(180, 320), new(280, 260), new(330, 160) }),
        },
        new Vector2[] { new(70, 334), new(94, 322), new(78, 290), new(58, 278) },
        new Vector2[] { new(274, 114), new(254, 106), new(322, 114), new(286, 86) });

    /// <summary>Twenty points on open ground for the bots to drive between, at least 45 m apart and covering the map.</summary>
    public static readonly IReadOnlyList<Vector2> Waypoints = new Vector2[]
    {
        new(192, 216), new(40, 40), new(340, 40), new(360, 360), new(40, 360), new(40, 192), new(360, 188), new(212, 360),
        new(184, 40), new(288, 272), new(124, 128), new(260, 104), new(124, 312), new(48, 116), new(116, 208), new(284, 180),
        new(56, 280), new(360, 112), new(112, 52), new(284, 348),
    };

    private static SpawnPoint[] TeamSpawns()
    {
        var points = new SpawnPoint[Team0Spots.Length + Team1Spots.Length];
        for (int i = 0; i < Team0Spots.Length; i++) points[i] = new SpawnPoint(Team0Spots[i], Angles.YawOf(Team1Centre - Team0Spots[i]), 0);
        for (int i = 0; i < Team1Spots.Length; i++) points[Team0Spots.Length + i] = new SpawnPoint(Team1Spots[i], Angles.YawOf(Team0Centre - Team1Spots[i]), 1);
        return points;
    }

    private static SpawnPoint[] SoloSpawns()
    {
        var middle = new Vector2(200, 200);
        return SoloSpots.Select(p => new SpawnPoint(p, Angles.YawOf(middle - p), 0)).ToArray();
    }

    /// <summary>
    /// A place to watch the patrol from: open ground 109 m south-west of its first bush patch, in sight of it (from the
    /// spawn, rocks and the hill hide both patches). From here a medium tank sees the patrol in the open, loses it once it
    /// stands in the bushes (two of them hide it: 40.5 m) and sees it again for 3 s after each shot it fires (117 m).
    /// </summary>
    public static readonly Vector2 PatrolWatch = new(224, 352);

    /// <summary>
    /// Hand-placed walls and fences (stage 2C design §3.6). The yard, 18 × 12.8 m inside around (160, 340): a stone wall
    /// on the east side, facing the spawn; brick on the north and south; brick on the west with a gate of three fence
    /// panels in the middle. The east and west walls run the full z 333–347; the north and south walls fit between them,
    /// 1 cm short of their inner faces. Then cover near the spawn: a brick wall north-north-west of it, a stone wall to
    /// the east and two rows of fence panels, one north-east of it and one on the way to the yard. Seen from the spawn
    /// none of them hides the first six targets.
    /// </summary>
    public static readonly IReadOnlyList<WallLine> WallLines = LayWallLines();

    // The yard's edges. Consts, not static readonly fields: WallLines is initialised first, and would see those as 0.
    private const float YardWest = 151f, YardEast = 169f;     // where the north and south walls end
    private const float YardNorth = 333f, YardSouth = 347f;   // where the side walls end: the north and south walls' outer faces (−Z is north)
    private const float GateNorth = 337f, GateSouth = 343f;   // the fence gate in the west wall
    private const float CornerGap = 0.01f;                    // from the north and south walls' ends to the side walls

    private static WallLine[] LayWallLines()
    {
        float brick = WallLine.Thickness(PropKind.BrickWall) / 2, stone = WallLine.Thickness(PropKind.StoneWall) / 2;
        float east = YardEast + CornerGap + stone, west = YardWest - CornerGap - brick;   // x 169.51 and 150.69
        float north = YardNorth + brick, south = YardSouth - brick;                       // z 333.3 and 346.7
        return new WallLine[]
        {
            new(PropKind.StoneWall, new Vector2(east, YardNorth), new Vector2(east, YardSouth)),
            new(PropKind.BrickWall, new Vector2(YardWest, north), new Vector2(YardEast, north)),
            new(PropKind.BrickWall, new Vector2(YardWest, south), new Vector2(YardEast, south)),
            new(PropKind.BrickWall, new Vector2(west, YardNorth), new Vector2(west, GateNorth)),
            new(PropKind.Fence, new Vector2(west, GateNorth), new Vector2(west, GateSouth)),
            new(PropKind.BrickWall, new Vector2(west, GateSouth), new Vector2(west, YardSouth)),
            new(PropKind.BrickWall, new Vector2(186, 302), new Vector2(192, 302)),   // about 30 m NNW of the spawn
            new(PropKind.StoneWall, new Vector2(243, 320), new Vector2(247, 320)),   // about 45 m E
            new(PropKind.Fence, new Vector2(210, 318), new Vector2(216, 318)),       // the fence row NE of the spawn
            new(PropKind.Fence, new Vector2(182, 330), new Vector2(182, 336)),       // the fence row on the way to the yard
        };
    }

    private static readonly TerrainGenerator.Hill[] Hills =
    {
        new(BigHill.X, BigHill.Y, 70, 22),
        new(90, 250, 45, 12),
        new(310, 230, 30, 16),
        new(140, 90, 35, 9),
    };

    public static Battlefield Create()
    {
        HeightField terrain = GenerateTerrain();
        List<Prop> props = ScatterRocks(terrain);
        props.AddRange(PlantBushes(terrain, props));   // after the rocks, so rock ids (and their shapes) don't change
        foreach (WallLine line in WallLines) props.AddRange(line.Segments(terrain, props.Count + 1));   // and the walls after both
        return new Battlefield(terrain, props, Border);
    }

    internal static HeightField GenerateTerrain() => TerrainGenerator.Generate(Seed, Samples, CellSize, roughness: 4f, Hills);

    /// <summary>
    /// 16–22 round bushes per patch on flat ground, clear of rocks, the spawn and the spawn-to-hill lane. The first sits on
    /// the patch centre (at least 3.8 m, 4.0 m for the spawn patch); throws if even the minimum size doesn't fit there.
    /// </summary>
    internal static List<Prop> PlantBushes(HeightField terrain, List<Prop> rocks)
    {
        var bushes = new List<Prop>();
        int nextId = rocks.Count + 1;
        for (int patch = 0; patch < BushPatches.Count; patch++)
        {
            var rng = PatchRng(patch);
            int wanted = WantedBushes(ref rng);
            int planted = 0;
            for (int attempt = 0; attempt < BushAttemptsPerPatch && planted < wanted; attempt++)
            {
                float angle = rng.NextFloat() * Angles.TwoPi, reach = MathF.Sqrt(rng.NextFloat()) * BushSpread;
                float radius = MinBushRadius + rng.NextFloat() * (MaxBushRadius - MinBushRadius);
                float minCentre = patch == SpawnPatch ? MinSpawnPatchCentreBushRadius : MinCentreBushRadius;
                if (planted == 0) { reach = 0f; radius = MathF.Max(radius, minCentre); }
                float height = MinBushHeight + rng.NextFloat() * (MaxBushHeight - MinBushHeight);
                Vector2 center = BushPatches[patch] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
                var footprint = new Obb2(center, new Vector2(radius, radius), 0f);
                if (!CanGrowBush(terrain, footprint, rocks, bushes))
                {
                    if (planted > 0) continue;
                    // The centre bush: retry at the minimum size; maps are authored, so if even that fails it's a map bug.
                    footprint = new Obb2(center, new Vector2(minCentre, minCentre), 0f);
                    if (!CanGrowBush(terrain, footprint, rocks, bushes))
                        throw new InvalidOperationException($"Bush patch {patch} at {BushPatches[patch]}: no room for its centre bush (r = {minCentre:F1} m).");
                }
                float ground = terrain.HeightAt(center.X, center.Y);
                bushes.Add(new Prop(nextId++, PropKind.Bush, footprint, ground - BushSink, height + BushSink));
                planted++;
            }
        }
        return bushes;
    }

    private static XorShift PatchRng(int patch) => new((uint)(Seed + 7919 * (patch + 1)));

    private static int WantedBushes(ref XorShift rng) => MinBushes + (int)(rng.NextUInt() % (MaxBushes - MinBushes + 1));

    /// <summary>How many bushes patch <paramref name="patch"/> tries to plant (16–22); crowding may leave it a little short.</summary>
    public static int WantedBushes(int patch)
    {
        XorShift rng = PatchRng(patch);
        return WantedBushes(ref rng);
    }

    private static bool CanGrowBush(HeightField terrain, Obb2 footprint, List<Prop> rocks, List<Prop> bushes)
    {
        Vector2 c = footprint.Center;
        float r = footprint.HalfExtents.X;
        if (MathF.Acos(terrain.NormalAt(c.X, c.Y).Y) > BushMaxSlope) return false;
        if (Vector2.Distance(c, Spawn) - r < BushSpawnClearance) return false;
        if (DistanceToSegment(c, Spawn, BigHill) - r < LaneHalfWidth) return false;
        if (c.X < Border + r || c.Y < Border + r || c.X > terrain.SizeX - Border - r || c.Y > terrain.SizeZ - Border - r) return false;
        foreach (Prop rock in rocks)
            if (Obb2.TryGetPenetration(footprint, rock.Footprint, out _)) return false;
        foreach (Prop bush in bushes)
            if (Vector2.Distance(c, bush.Footprint.Center) < BushOverlap * (r + bush.Footprint.HalfExtents.X)) return false;
        return true;
    }

    /// <summary>
    /// <see cref="RockTarget"/> rocks (fewer only if <see cref="MaxRockAttempts"/> tries run out): kept clear of the spawn, wholly outside the spawn-to-hill lane, off the bush
    /// patch centres, and never stacked on each other (they may cluster into piles).
    /// </summary>
    private static List<Prop> ScatterRocks(HeightField terrain)
    {
        var rng = new XorShift(Seed);
        var rocks = new List<Prop>();
        for (int k = 0; k < MaxRockAttempts && rocks.Count < RockTarget; k++)
        {
            var center = new Vector2(RockMargin + rng.NextFloat() * RockSpan, RockMargin + rng.NextFloat() * RockSpan);
            var half = new Vector2(MinRockHalfX + rng.NextFloat() * (MaxRockHalfX - MinRockHalfX), MinRockHalfZ + rng.NextFloat() * (MaxRockHalfZ - MinRockHalfZ));
            float height = MinRockHeight + rng.NextFloat() * (MaxRockHeight - MinRockHeight);
            float yaw = rng.NextFloat() * Angles.TwoPi;
            var footprint = new Obb2(center, half, yaw);
            if (!CanPlaceRock(footprint, rocks)) continue;
            float ground = terrain.HeightAt(center.X, center.Y);
            // Sink the base so the rock never floats on a slope.
            rocks.Add(new Prop(rocks.Count + 1, PropKind.Rock, footprint, ground - RockSink, height + RockSink));
        }
        return rocks;
    }

    private static bool CanPlaceRock(Obb2 footprint, List<Prop> rocks)
    {
        Vector2 c = footprint.Center;
        float r = footprint.HalfExtents.Length();
        if (Vector2.Distance(c, Spawn) < SpawnClearance) return false;
        if (DistanceToSegment(c, Spawn, BigHill) - r < LaneHalfWidth) return false;
        foreach (Vector2 patch in BushPatches)
            if (Vector2.Distance(c, patch) - r < RockPatchClearance) return false;
        foreach (Prop rock in rocks)
            if (Vector2.Distance(c, rock.Footprint.Center) < RockSpacing * (r + rock.Footprint.HalfExtents.Length())) return false;
        return true;
    }

    /// <summary>A <paramref name="spec"/> target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
    private static TargetSpot Facing(Vector2 position, float turnDegrees, TankSpec spec) =>
        new(position, Angles.Wrap(Angles.YawOf(Spawn - position) + Angles.Deg(turnDegrees)), spec);

    internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
