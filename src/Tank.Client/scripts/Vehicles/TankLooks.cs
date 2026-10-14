using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>Look table, one entry per <c>TankCatalog</c> spec. The medium tank is an orange toy: soap-bar hull, big dome, stubby gun.</summary>
public static class TankLooks
{
    private const int CornerSegments = 4;

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

    public static IReadOnlyList<TankLook> All { get; } = new[] { Medium };

    public static TankLook For(string specId) =>
        All.FirstOrDefault(look => look.SpecId == specId)
        ?? throw new KeyNotFoundException($"No TankLook for spec '{specId}'. Add one to TankLooks.");

    /// <summary>A convex profile with each corner rounded to its own radius.</summary>
    private static SN.Vector2[] Rounded(params (SN.Vector2 Corner, float Radius)[] corners) =>
        Polygon2.RoundCorners(corners.Select(c => c.Corner).ToArray(), corners.Select(c => c.Radius).ToArray(), CornerSegments);

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
