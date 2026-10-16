using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>
/// Look table, one entry per <c>TankCatalog</c> spec, all orange toys told apart by shape. The light tank is small and
/// low with a little dome and a long thin gun on three road wheels; the medium tank a soap-bar hull, big dome and stubby
/// gun; the heavy tank wide and tall, a big rounded-block turret, a fat gun, five road wheels and wide tracks; the tank
/// destroyer a low wedge with no turret, its long gun in a ball mount on the sloped front plate.
/// </summary>
public static class TankLooks
{
    private const int CornerSegments = 4;
    private const int BlockRoofSteps = 3;   // profile steps across a rounded block's crowned roof, from its top edge in to the axis

    public static readonly TankLook Light = new()
    {
        SpecId = "light",
        LowerHull = Rounded(
            (new(-1.95f, 0.4f), 0.3f), (new(1.9f, 0.4f), 0.3f), (new(2.2f, 0.8f), 0.28f),
            (new(2.2f, 1.3f), 0.28f), (new(-2.2f, 1.3f), 0.28f), (new(-2.2f, 0.9f), 0.28f)),
        LowerHullWidth = 1.56f,
        LowerHullRound = 0.26f,
        // A low soap bar; the long glacis in front of the dome keeps the long gun clear at full depression.
        UpperHull = Rounded(
            (new(-2.45f, 0.92f), 0.22f), (new(2.5f, 0.92f), 0.28f), (new(2.5f, 1.55f), 0.32f),
            (new(-1.6f, 1.55f), 0.45f), (new(-2.45f, 1.34f), 0.18f)),
        UpperHullWidth = 2.8f,
        UpperHullRound = 0.15f,
        TurretBase = 1.49f,
        TurretProfile = Dome(radius: 1.1f, height: 0.85f, lip: 0.04f, steps: 12),
        GunPivotZ = -1.0f,
        MantletRadius = 0.2f,
        MantletWidth = 0.7f,
        BarrelRadius = 0.13f,
        MuzzleRadius = 0.2f,
        MuzzleLength = 0.34f,
        TrackX = 1.1f,
        TrackWidth = 0.56f,
        TrackThickness = 0.08f,
        LinkPitch = 0.26f,
        Sprocket = new WheelSpot(-2.28f, 0.66f, 0.42f),
        Idler = new WheelSpot(2.28f, 0.66f, 0.42f),
        RoadWheelCount = 3,
        RoadWheelRadius = 0.5f,
        RoadWheelY = 0.58f,
        RoadWheelSpacing = 1.12f,
        RoadWheelWidth = 0.46f,
        Details = new DetailPart[]
        {
            new(DetailKind.Headlight, new(-0.7f, 1.42f, -2.3f), new(0.34f, 0.34f, 0.18f)),
            new(DetailKind.Headlight, new(0.7f, 1.42f, -2.3f), new(0.34f, 0.34f, 0.18f)),
            new(DetailKind.Cupola, new(0.2f, 2.22f, 0.25f), new(0.46f, 0.16f, 0f), OnTurret: true),
        },
        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
    };

    public static readonly TankLook Medium = new()
    {
        SpecId = "medium",
        // A soap bar between the tracks, recessed a little under the deck's nose and tail.
        LowerHull = Rounded(
            (new(-2.3f, 0.42f), 0.35f), (new(2.25f, 0.42f), 0.35f), (new(2.55f, 0.85f), 0.32f),
            (new(2.55f, 1.48f), 0.32f), (new(-2.55f, 1.48f), 0.32f), (new(-2.55f, 0.95f), 0.32f)),
        LowerHullWidth = 1.84f,
        LowerHullRound = 0.3f,
        // One fat soap-bar body over the tracks: its rounded side skirts swallow the upper track run and stop near the
        // road-wheel tops, so the wheels show below. It stops short of the track ends, so the round end wheels show like
        // a toy's. The roof dips into a rounded glacis in front of the dome, so the fully depressed gun clears it even
        // while a shot rocks the hull nose-up.
        UpperHull = Rounded(
            (new(-2.85f, 0.94f), 0.3f), (new(2.9f, 0.94f), 0.36f), (new(2.9f, 1.74f), 0.4f),
            (new(-1.9f, 1.74f), 0.5f), (new(-2.85f, 1.5f), 0.22f)),
        UpperHullWidth = 3.2f,
        UpperHullRound = 0.16f,
        TurretBase = 1.68f,
        TurretProfile = Dome(radius: 1.52f, height: 1.18f, lip: 0.04f, steps: 12),
        GunPivotZ = -1.42f,
        MantletRadius = 0.28f,
        MantletWidth = 1.0f,
        BarrelRadius = 0.2f,
        MuzzleRadius = 0.28f,
        MuzzleLength = 0.42f,
        TrackX = 1.27f,
        TrackWidth = 0.62f,
        TrackThickness = 0.08f,
        LinkPitch = 0.28f,
        Sprocket = new WheelSpot(-2.7f, 0.66f, 0.46f),
        Idler = new WheelSpot(2.7f, 0.66f, 0.46f),
        RoadWheelCount = 4,
        RoadWheelRadius = 0.52f,
        RoadWheelY = 0.60f,
        RoadWheelSpacing = 1.08f,
        RoadWheelWidth = 0.5f,
        Details = new DetailPart[]
        {
            new(DetailKind.Headlight, new(-0.85f, 1.6f, -2.55f), new(0.42f, 0.42f, 0.2f)),   // two round "eyes" on the glacis
            new(DetailKind.Headlight, new(0.85f, 1.6f, -2.55f), new(0.42f, 0.42f, 0.2f)),
            new(DetailKind.Cupola, new(0.25f, 2.71f, 0.35f), new(0.6f, 0.2f, 0f), OnTurret: true),  // one low round hatch
        },
        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
    };

    public static readonly TankLook Heavy = new()
    {
        SpecId = "heavy",
        LowerHull = Rounded(
            (new(-3.1f, 0.45f), 0.35f), (new(3.05f, 0.45f), 0.35f), (new(3.35f, 0.95f), 0.32f),
            (new(3.35f, 1.8f), 0.32f), (new(-3.35f, 1.8f), 0.32f), (new(-3.35f, 1.05f), 0.32f)),
        LowerHullWidth = 1.86f,
        LowerHullRound = 0.3f,
        // A tall, wide slab; the glacis dips in front of the turret like the medium's.
        UpperHull = Rounded(
            (new(-3.7f, 0.95f), 0.3f), (new(3.7f, 0.95f), 0.3f), (new(3.7f, 2.0f), 0.4f),
            (new(-2.4f, 2.0f), 0.55f), (new(-3.7f, 1.72f), 0.24f)),
        UpperHullWidth = 3.6f,
        UpperHullRound = 0.18f,
        TurretBase = 1.94f,
        TurretProfile = Block(radius: 1.55f, height: 1.3f, edge: 0.45f, crown: 0.08f, lip: 0.04f, steps: 6),
        TurretSquareness = 4f,
        GunPivotZ = -1.57f,
        MantletRadius = 0.34f,
        MantletWidth = 1.2f,
        BarrelRadius = 0.26f,
        MuzzleRadius = 0.36f,
        MuzzleLength = 0.5f,
        TrackX = 1.38f,
        TrackWidth = 0.8f,
        TrackThickness = 0.1f,
        LinkPitch = 0.32f,
        Sprocket = new WheelSpot(-3.24f, 0.8f, 0.42f),
        Idler = new WheelSpot(3.24f, 0.8f, 0.42f),
        RoadWheelCount = 5,
        RoadWheelRadius = 0.55f,
        RoadWheelY = 0.65f,
        RoadWheelSpacing = 1.12f,
        RoadWheelWidth = 0.66f,
        Details = new DetailPart[]
        {
            new(DetailKind.Headlight, new(-0.95f, 1.82f, -3.3f), new(0.46f, 0.46f, 0.22f)),
            new(DetailKind.Headlight, new(0.95f, 1.82f, -3.3f), new(0.46f, 0.46f, 0.22f)),
            new(DetailKind.Cupola, new(0.5f, 3.22f, 0.55f), new(0.66f, 0.24f, 0f), OnTurret: true),
        },
        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
    };

    public static readonly TankLook Td = new()
    {
        SpecId = "td",
        LowerHull = Rounded(
            (new(-2.9f, 0.42f), 0.3f), (new(2.85f, 0.42f), 0.3f), (new(3.15f, 0.85f), 0.3f),
            (new(3.15f, 1.3f), 0.3f), (new(-3.15f, 1.3f), 0.2f), (new(-3.15f, 0.9f), 0.2f)),
        LowerHullWidth = 1.84f,
        LowerHullRound = 0.18f,
        // The casemate: a wedge whose sloped front plate holds the ball mount, sloping away fast enough in front of it
        // that the fully depressed barrel clears it.
        UpperHull = Rounded(
            (new(-3.45f, 0.92f), 0.22f), (new(3.45f, 0.92f), 0.28f), (new(3.45f, 1.55f), 0.3f),
            (new(2.4f, 2.15f), 0.45f), (new(-2.4f, 2.15f), 0.4f), (new(-3.45f, 1.35f), 0.2f)),
        UpperHullWidth = 3.3f,
        UpperHullRound = 0.16f,
        TurretBase = 1.85f,
        TurretProfile = Array.Empty<SN.Vector2>(),
        GunPivotZ = 0f,              // the ball turns about its own centre
        MantletRadius = 0.36f,
        MantletWidth = 0.76f,        // all but a ball
        BarrelRadius = 0.18f,
        MuzzleRadius = 0.3f,
        MuzzleLength = 0.44f,
        TrackX = 1.3f,
        TrackWidth = 0.66f,
        TrackThickness = 0.08f,
        LinkPitch = 0.28f,
        Sprocket = new WheelSpot(-2.95f, 0.68f, 0.44f),
        Idler = new WheelSpot(2.95f, 0.68f, 0.44f),
        RoadWheelCount = 4,
        RoadWheelRadius = 0.52f,
        RoadWheelY = 0.6f,
        RoadWheelSpacing = 1.16f,
        RoadWheelWidth = 0.5f,
        Details = new DetailPart[]
        {
            new(DetailKind.Headlight, new(-1.0f, 1.18f, -3.38f), new(0.36f, 0.36f, 0.2f)),
            new(DetailKind.Headlight, new(1.0f, 1.18f, -3.38f), new(0.36f, 0.36f, 0.2f)),
            new(DetailKind.Cupola, new(-0.5f, 2.1f, 0.8f), new(0.56f, 0.2f, 0f)),   // on the casemate roof
        },
        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
    };

    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium, Heavy, Td };

    public static TankLook For(string specId) =>
        All.FirstOrDefault(look => look.SpecId == specId)
        ?? throw new KeyNotFoundException($"No TankLook for spec '{specId}'. Add one to TankLooks.");

    /// <summary>A convex profile with each corner rounded to its own radius.</summary>
    private static SN.Vector2[] Rounded(params (SN.Vector2 Corner, float Radius)[] corners) =>
        Polygon2.RoundCorners(corners.Select(c => c.Corner).ToArray(), corners.Select(c => c.Radius).ToArray(), CornerSegments);

    /// <summary>
    /// Lathe profile of a rounded block's side, for a squared-off sweep: a short flared lip, straight sides up to a top
    /// edge rounded over <paramref name="edge"/>, then a roof crowned <paramref name="crown"/> higher in the middle.
    /// </summary>
    private static SN.Vector2[] Block(float radius, float height, float edge, float crown, float lip, int steps)
    {
        var points = new List<SN.Vector2> { new(radius * 0.95f, 0f), new(radius, lip) };
        for (int k = 0; k <= steps; k++)
        {
            float theta = MathF.PI / 2 * k / steps;
            points.Add(new SN.Vector2(radius - edge + edge * MathF.Cos(theta), height - edge + edge * MathF.Sin(theta)));
        }
        float roof = radius - edge;
        for (int k = 1; k <= BlockRoofSteps; k++)
        {
            float r = roof * (1f - (float)k / BlockRoofSteps);
            points.Add(new SN.Vector2(r, height + crown * (1f - r * r / (roof * roof))));
        }
        return points.ToArray();
    }

    /// <summary>
    /// Lathe profile of a full, toy-like dome: a short flared lip from 95% of <paramref name="radius"/> up to
    /// <paramref name="lip"/>, then a quarter ellipse closing at the axis.
    /// </summary>
    private static SN.Vector2[] Dome(float radius, float height, float lip, int steps)
    {
        var points = new List<SN.Vector2> { new(radius * 0.95f, 0f) };
        for (int k = 0; k <= steps; k++)
        {
            float theta = MathF.PI / 2 * k / steps;
            float r = k == steps ? 0f : radius * MathF.Cos(theta);
            points.Add(new SN.Vector2(r, lip + (height - lip) * MathF.Sin(theta)));
        }
        return points.ToArray();
    }
}
