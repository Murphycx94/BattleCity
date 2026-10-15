using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks, twelve wide patches of tall bushes and six
/// practice targets. The spawn faces the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
/// </summary>
public static class SandboxMap
{
    public const int Seed = 20261013;
    public const int Samples = 201;
    public const float CellSize = 2f;
    public const float Border = 10f;
    private const int RockTarget = 88, MaxRockAttempts = 200;   // place until the target, giving up after this many tries
    private const float MinRockHalfX = 2f, MaxRockHalfX = 4.5f, MinRockHalfZ = 2f, MaxRockHalfZ = 5.5f;
    private const float MinRockHeight = 2.5f, MaxRockHeight = 6f;   // above the ground; the base sinks a further RockSink
    private const float RockSink = 1f;
    private const float RockMargin = 30f, RockSpan = (Samples - 1) * CellSize - 2 * RockMargin;   // rock centres lie this far inside the map edge
    private const float SpawnClearance = 30f;
    private const float RockPatchClearance = 8f;   // a rock's bounding circle stays this far from every bush patch centre
    private const float RockSpacing = 0.5f;        // rock centres at least this × the sum of bounding radii apart: piles, not stacks

    public static readonly Vector2 Spawn = new(200, 330);

    /// <summary>Faces the big hill to the north.</summary>
    public const float SpawnYaw = 0f;

    /// <summary>Rocks, bushes and targets keep this far from the straight lane between the spawn and the big hill.</summary>
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
    private const float BushSpread = 9f;          // bush centres lie within this radius, so a patch is at most ~27 m across
    private const int MinBushes = 16, MaxBushes = 22;
    private const float MinBushRadius = 3.2f, MaxBushRadius = 4.6f;
    private const int SpawnPatch = 0;
    private const float MinCentreBushRadius = 3.8f;   // the first bush sits exactly on the patch centre: a guaranteed hiding spot
    private const float MinSpawnPatchCentreBushRadius = 4.0f;   // the patch beside the spawn gets an extra-roomy centre bush
    private const float MinBushHeight = 4.2f, MaxBushHeight = 5.2f;
    private const float BushSink = 0.3f;          // footprint starts this far below the ground, like the rocks
    private const float BushMaxSlope = 10f * MathF.PI / 180f;
    private const float BushSpawnClearance = 20f;
    private const float BushOverlap = 0.35f;      // neighbours may overlap: centres at least this × the sum of radii apart
    private const int BushAttemptsPerPatch = 200;

    /// <summary>
    /// Six practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes and the
    /// spawn-to-hill lane. Seen from the spawn they show, in order: the front, the side, a 45° angle, the rear, a front
    /// half hidden behind a rock, and a front far away (180–200 m).
    /// </summary>
    public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
    {
        Facing(new Vector2(160, 304), turnDegrees: 0),
        Facing(new Vector2(248, 300), turnDegrees: 90),
        Facing(new Vector2(216, 244), turnDegrees: 45),
        Facing(new Vector2(140, 256), turnDegrees: 180),
        Facing(new Vector2(280, 328), turnDegrees: 0),
        Facing(new Vector2(380, 272), turnDegrees: 0),
    };

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

    /// <summary>A target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
    private static TargetSpot Facing(Vector2 position, float turnDegrees) =>
        new(position, Angles.Wrap(Angles.YawOf(Spawn - position) + Angles.Deg(turnDegrees)));

    internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
