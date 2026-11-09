using System.Numerics;
using Tank.Sim.Maps.Building;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps.Riverland;

/// <summary>An axis-aligned rectangle on the ground (x east, z south).</summary>
internal readonly record struct GroundRect(Vector2 Min, Vector2 Max)
{
    public Vector2 Centre => (Min + Max) * 0.5f;

    public bool Contains(Vector2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    /// <summary>Distance to the rectangle's outline: negative inside, positive outside.</summary>
    public float SignedDistance(Vector2 p)
    {
        Vector2 half = (Max - Min) * 0.5f;
        Vector2 q = Vector2.Abs(p - Centre) - half;
        return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0f);
    }

    /// <summary>How far inside the rectangle <paramref name="p"/> is (to the nearest side); negative outside.</summary>
    public float Inside(Vector2 p) => MathF.Min(MathF.Min(p.X - Min.X, Max.X - p.X), MathF.Min(p.Y - Min.Y, Max.Y - p.Y));
}

/// <summary>A ramp up the plateau: a straight lane from the top end (on the plateau's rim) down to the bottom end.</summary>
internal readonly record struct Ramp(Vector2 Top, Vector2 Bottom)
{
    public float Length => Vector2.Distance(Top, Bottom);

    /// <summary>Unit direction from the top end down to the bottom end.</summary>
    public Vector2 Down => Vector2.Normalize(Bottom - Top);

    /// <summary>How far along the ramp from its top end (negative behind it), and how far off its centre line, of <paramref name="p"/>.</summary>
    public (float Along, float Across) Locate(Vector2 p)
    {
        Vector2 d = p - Top, down = Down;
        return (Vector2.Dot(d, down), MathF.Abs(d.X * down.Y - d.Y * down.X));
    }
}

/// <summary>A road or a dirt track: its centre line, which kind of ground it is, and its id in its distance grid.</summary>
internal sealed record RoadLine(string Name, Polyline Line, bool IsTrack, int Id);

/// <summary>A ford: where a road crosses the river; <see cref="Centre"/> is the road's centre point nearest the river's centre line.</summary>
internal sealed record Ford(RoadLine Road, Vector2 Centre);

/// <summary>
/// A village (riverland design §3.6): its houses stand within <see cref="Radius"/> of <see cref="Centre"/>, along both
/// sides of its <see cref="Streets"/>; it gets <see cref="Houses"/> of them. The town has mostly two-storey houses and
/// farmhouses, a village mostly cottages and farmhouses.
/// </summary>
internal sealed record Village(string Name, Vector2 Centre, float Radius, RoadLine[] Streets, int Houses, bool IsTown);

/// <summary>A side road or track that starts or ends on a main one.</summary>
internal sealed record Junction(RoadLine Branch, bool AtStart, RoadLine Main);

/// <summary>
/// Where everything on the riverland lies (riverland design §3; x east, z south, metres): the river, the lake and the
/// fords, the plateau and its ramps, the hills and the dunes, the roads and tracks, the forests, the villages. The
/// generator and the tests read the same constants and shape functions.
/// </summary>
internal static class RiverlandLayout
{
    public const float Size = 1200f;

    // ---- noise seeds: RiverlandMap.Seed plus a fixed offset per use ----
    private const uint Seed = RiverlandMap.Seed;
    public const uint BroadNoiseSeed = Seed + 1, FineNoiseSeed = Seed + 2, PlateauEdgeSeed = Seed + 3,
                      BankSeed = Seed + 4, LakeShoreSeed = Seed + 5, ForestEdgeSeed = Seed + 6, VillageSeed = Seed + 7,
                      RockSeed = Seed + 8, BushSeed = Seed + 9, ForestSeed = Seed + 10, LoneTreeSeed = Seed + 11;

    // ---- base ground (§3.3) ----
    public const float PlainHeight = 4f, BroadAmplitude = 4f, BroadScale = 150f, FineAmplitude = 1f, FineScale = 30f;
    public const int BroadOctaves = 3, FineOctaves = 2;

    // ---- the river (§3.4) ----
    /// <summary>The centre line, starting and ending a little outside the map so the river runs off both edges.</summary>
    public static readonly Vector2[] RiverControl =
    {
        new(640, -20), new(640, 0), new(600, 150), new(615, 300), new(670, 440), new(700, 560),
        new(665, 700), new(720, 850), new(840, 980), new(930, 1100), new(980, 1200), new(990, 1220),
    };
    public const float RiverHalfWidth = 18f, RiverDepth = 1.3f;
    /// <summary>The sand bank runs from the water's edge to BankOuter = RiverHalfWidth + BankWidth ± BankJitter (6–10 m wide), rising to BankHeight.</summary>
    public const float BankWidth = 8f, BankJitter = 2f, BankHeight = 1f, BankNoiseScale = 25f;
    /// <summary>Beyond the bank the ground eases to the valley floor over this many metres.</summary>
    public const float BankBlend = 14f;
    /// <summary>Within ValleyFlat m of the centre line the land is lowered right down to ValleyHeight, easing out by ValleyEdge.</summary>
    public const float ValleyFlat = 60f, ValleyEdge = 120f, ValleyHeight = 1.5f;
    /// <summary>The river grid is stamped this far, past the valley's edge.</summary>
    public const float RiverReach = ValleyEdge + 10f;

    // ---- the fords ----
    /// <summary>Within FordHalfWidth m of a ford road's centre the river is FordDepth deep, easing back to the bed over FordBlend m.</summary>
    public const float FordHalfWidth = 12f, FordDepth = 0.5f, FordBlend = 6f;
    /// <summary>The ford's area reaches this far from the river's centre line (the water plus the road's dip into it).</summary>
    public const float FordRiverReach = 30f;

    // ---- the lake ----
    public static readonly Vector2 LakeCentre = new(330, 800);
    public const float LakeRadius = 85f, LakeJitter = 12f, LakeDepth = 1.6f, BeachWidth = 10f;
    /// <summary>The shore's noise is read on a circle this big in noise space, so it closes on itself.</summary>
    private const float LakeNoiseCircle = 2.5f;

    // ---- the plateau and its ramps ----
    public static readonly GroundRect Plateau = new(new Vector2(60, 80), new Vector2(420, 320));
    /// <summary>The rim wanders up to PlateauJitter m in or out: noise PlateauNoiseGain × [−1, 1], clamped (fractal noise seldom nears ±1).</summary>
    public const float PlateauRise = 22f, PlateauJitter = 10f, PlateauNoiseGain = 16f, PlateauNoiseScale = 90f, CliffWidth = 7f;
    public static readonly Ramp SouthRamp = new(new Vector2(232, 320), new Vector2(240, 420));
    public static readonly Ramp EastRamp = new(new Vector2(420, 220), new Vector2(525, 232));
    public static readonly Ramp[] Ramps = { SouthRamp, EastRamp };
    /// <summary>A ramp's lane is 2 × RampHalfWidth wide; its sides drop to the ground around over RampSide m.</summary>
    public const float RampHalfWidth = 7f, RampSide = 6f;
    /// <summary>Behind its top end a ramp continues level this far, so it always meets the plateau's top whatever its rim's noise.</summary>
    public const float RampLanding = 20f;
    /// <summary>Past its bottom end a ramp eases into the ground over this many metres.</summary>
    public const float RampFoot = 10f;

    // ---- hills ----
    public static readonly TerrainGenerator.Hill SouthWestHill = new(180, 1060, 140, 18);
    public static readonly TerrainGenerator.Hill EastHill = new(1000, 350, 120, 15);
    public static readonly TerrainGenerator.Hill[] Hills =
    {
        SouthWestHill, EastHill,
        new(470, 180, 60, 6), new(340, 640, 50, 5), new(560, 1110, 55, 7), new(840, 290, 60, 8), new(1100, 560, 45, 6),
    };

    // ---- dunes ----
    public static readonly GroundRect Dunes = new(new Vector2(860, 880), new Vector2(1170, 1170));
    /// <summary>Ridges run north-east to south-west, DuneSpacing m apart and 2 × DuneAmplitude high, fading in over DuneFade m from the edge.</summary>
    public const float DuneSpacing = 40f, DuneAmplitude = 2f, DuneFade = 30f;

    // ---- roads and tracks (§3.5) ----
    public const float RoadHalfWidth = 4f, TrackHalfWidth = 3f, Shoulder = 5f;
    public const float RoadSmoothing = 60f;
    public static readonly float MaxRoadGrade = MathF.Tan(8f * MathF.PI / 180f);
    /// <summary>The road and track grids are stamped this far (shoulders, and what villages and scattering need).</summary>
    public const float RoadReach = 40f;
    /// <summary>A side road's last and the main road's nearest JunctionPad m are held level where they meet.</summary>
    public const float JunctionPad = 10f;

    public const int EastWestRoadId = 0, NorthRoadId = 1, SouthRoadId = 2;
    public const int PlateauTrackId = 0, DuneTrackId = 1;

    public static readonly RoadLine EastWestRoad = new("east-west road", new Polyline(new Vector2[]
    {
        new(30, 560), new(250, 520), new(450, 545), new(700, 560), new(860, 600), new(1000, 640), new(1170, 620),
    }), IsTrack: false, EastWestRoadId);

    public static readonly RoadLine NorthRoad = new("north road", new Polyline(new Vector2[]
    {
        new(900, 30), new(900, 180), new(800, 400), new(760, 575),
    }), IsTrack: false, NorthRoadId);

    public static readonly RoadLine SouthRoad = new("south road", new Polyline(new Vector2[]
    {
        new(640, 555), new(590, 700), new(560, 800), new(500, 1000), new(520, 1170),
    }), IsTrack: false, SouthRoadId);

    public static readonly RoadLine PlateauTrack = new("plateau track", new Polyline(new Vector2[]
    {
        new(250, 520), new(240, 420), new(232, 320), new(200, 220),
    }), IsTrack: true, PlateauTrackId);

    public static readonly RoadLine DuneTrack = new("dune track", new Polyline(new Vector2[]
    {
        new(500, 1000), new(700, 1010), new(850, 1000), new(1000, 1050),
    }), IsTrack: true, DuneTrackId);

    /// <summary>The roads, indexed by their ids.</summary>
    public static readonly RoadLine[] Roads = { EastWestRoad, NorthRoad, SouthRoad };

    /// <summary>The tracks, indexed by their ids.</summary>
    public static readonly RoadLine[] Tracks = { PlateauTrack, DuneTrack };

    /// <summary>Every road and track, main roads before the ones that branch off them.</summary>
    public static readonly RoadLine[] AllRoads = { EastWestRoad, NorthRoad, SouthRoad, PlateauTrack, DuneTrack };

    public static readonly Junction[] Junctions =
    {
        new(NorthRoad, AtStart: false, EastWestRoad),
        new(SouthRoad, AtStart: true, EastWestRoad),
        new(PlateauTrack, AtStart: true, EastWestRoad),
        new(DuneTrack, AtStart: true, SouthRoad),
    };

    public static readonly Polyline River = new(RiverControl);

    /// <summary>The east-west road's crossing by the town and the dune track's in the south.</summary>
    public static readonly Ford[] Fords = { FordOf(EastWestRoad), FordOf(DuneTrack) };

    // ---- forests (§3.7; the ground under them is dirt) ----
    public static readonly GroundRect[] Forests =
    {
        new(new Vector2(860, 420), new Vector2(1160, 800)),   // east
        new(new Vector2(50, 620), new Vector2(240, 880)),     // west
        new(new Vector2(680, 40), new Vector2(840, 200)),     // north
        new(new Vector2(1040, 120), new Vector2(1160, 260)),  // north-east
        new(new Vector2(300, 300), new Vector2(420, 420)),    // centre
    };
    public const float ForestJitter = 25f, ForestNoiseScale = 90f;

    // ---- villages (§3.6) ----
    public static readonly Vector2 Town = new(700, 560), NorthVillage = new(900, 180), WestVillage = new(250, 520), SouthVillage = new(500, 1000);
    public static readonly Vector2[] Farms = { new(440, 680), new(545, 330), new(790, 780), new(650, 880), new(115, 470) };
    /// <summary>A village's houses stand within this many metres of its centre; a farm's within FarmRadius of the farm's.</summary>
    public const float TownRadius = 160f, NorthVillageRadius = 110f, WestVillageRadius = 90f, SouthVillageRadius = 110f, FarmRadius = 40f;

    /// <summary>The town and the three villages, in the order they are built: where, how far out, along which roads, how many houses.</summary>
    public static readonly Village[] Villages =
    {
        new("town", Town, TownRadius, new[] { EastWestRoad, NorthRoad, SouthRoad }, Houses: 22, IsTown: true),
        new("north village", NorthVillage, NorthVillageRadius, new[] { NorthRoad }, Houses: 10, IsTown: false),
        new("west village", WestVillage, WestVillageRadius, new[] { EastWestRoad, PlateauTrack }, Houses: 8, IsTown: false),
        new("south village", SouthVillage, SouthVillageRadius, new[] { SouthRoad, DuneTrack }, Houses: 10, IsTown: false),
    };

    // ---- shape functions ----

    /// <summary>The base ground before anything is sculpted: plain height plus broad and fine noise.</summary>
    public static float BaseHeight(float x, float z) =>
        PlainHeight
        + BroadAmplitude * ValueNoise.Signed(BroadNoiseSeed, x / BroadScale, z / BroadScale, BroadOctaves)
        + FineAmplitude * ValueNoise.Signed(FineNoiseSeed, x / FineScale, z / FineScale, FineOctaves);

    /// <summary>The sum of the hills' bumps at (x, z).</summary>
    public static float HillsHeight(float x, float z)
    {
        float h = 0;
        foreach (TerrainGenerator.Hill hill in Hills)
        {
            float dx = x - hill.X, dz = z - hill.Z;
            h += hill.Height * HeightSculpt.Bump(MathF.Sqrt(dx * dx + dz * dz), hill.Radius);
        }
        return h;
    }

    /// <summary>The plateau's outline with its rim's noise: negative inside, positive outside, metres.</summary>
    public static float PlateauEdgeDistance(Vector2 p) =>
        Plateau.SignedDistance(p) + Math.Clamp(
            PlateauNoiseGain * ValueNoise.Signed(PlateauEdgeSeed, p.X / PlateauNoiseScale, p.Y / PlateauNoiseScale, 2), -PlateauJitter, PlateauJitter);

    /// <summary>Where the river's sand bank ends, m from its centre line (RiverHalfWidth + 6–10 m).</summary>
    public static float BankOuter(Vector2 p) =>
        RiverHalfWidth + BankWidth + BankJitter * ValueNoise.Signed(BankSeed, p.X / BankNoiseScale, p.Y / BankNoiseScale, 2);

    /// <summary>The lake shore's distance from the lake centre in the direction of <paramref name="p"/>: 85 ± 12 m.</summary>
    public static float LakeShoreRadius(Vector2 p)
    {
        Vector2 d = p - LakeCentre;
        float r = d.Length();
        Vector2 dir = r > 1e-4f ? d / r : Vector2.UnitX;
        return LakeRadius + LakeJitter * ValueNoise.Signed(LakeShoreSeed,
            LakeNoiseCircle * (1 + dir.X), LakeNoiseCircle * (1 + dir.Y), 2);
    }

    /// <summary>How far <paramref name="p"/> is outside the lake shore, measured from the centre outwards (negative in the lake).</summary>
    public static float LakeShoreDistance(Vector2 p) => Vector2.Distance(p, LakeCentre) - LakeShoreRadius(p);

    /// <summary>Within a forest's outline, whose edges the noise moves in and out by up to ForestJitter m.</summary>
    public static bool InForest(Vector2 p)
    {
        foreach (GroundRect forest in Forests)
            if (ForestDepth(forest, p) > 0) return true;
        return false;
    }

    /// <summary>
    /// Roughly how far inside <paramref name="forest"/>'s noisy outline <paramref name="p"/> is, m: positive inside,
    /// negative outside (more than ForestJitter m out of the rectangle, simply how far out beyond that).
    /// </summary>
    public static float ForestDepth(in GroundRect forest, Vector2 p)
    {
        float d = forest.SignedDistance(p);
        if (d > ForestJitter) return ForestJitter - d;   // the noise can't bring the outline out this far
        return -(d + ForestJitter * ValueNoise.Signed(ForestEdgeSeed, p.X / ForestNoiseScale, p.Y / ForestNoiseScale, 2));
    }

    /// <summary>The deepest <see cref="ForestDepth(in GroundRect, Vector2)"/> of <paramref name="p"/> in any forest.</summary>
    public static float ForestDepth(Vector2 p)
    {
        float depth = float.NegativeInfinity;
        foreach (GroundRect forest in Forests) depth = MathF.Max(depth, ForestDepth(forest, p));
        return depth;
    }

    /// <summary>The dunes' ridges at <paramref name="p"/>: 0 in the troughs, 2 × DuneAmplitude on the crests, faded in from the area's edge.</summary>
    public static float DuneHeight(Vector2 p)
    {
        float inside = Dunes.Inside(p);
        if (inside <= 0) return 0f;
        float u = (p.X + p.Y) / MathF.Sqrt(2f);
        return HeightSculpt.SmoothStep(0, DuneFade, inside) * DuneAmplitude * (1 + MathF.Sin(2 * MathF.PI * u / DuneSpacing));
    }

    /// <summary>The road's centre point nearest the river's centre line.</summary>
    private static Ford FordOf(RoadLine road)
    {
        Vector2 best = road.Line.Points[0];
        float bestDistance = float.PositiveInfinity;
        foreach (Vector2 p in road.Line.Points)
        {
            float d = River.DistanceTo(p, out _);
            if (d < bestDistance) (best, bestDistance) = (p, d);
        }
        return new Ford(road, best);
    }
}
