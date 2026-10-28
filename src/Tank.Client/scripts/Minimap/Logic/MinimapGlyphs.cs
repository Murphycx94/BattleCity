using System.Numerics;

namespace Tank.Client.Minimap;

/// <summary>
/// Icon geometry in map-view pixels (y down), written into caller buffers (stage 5 design §5); each method returns the
/// number of points it wrote, never more than its constant. A <c>size</c> is the icon's extent in pixels: the diameter
/// of the circle a shape lies in, an arrow's length tip to tail. Nothing here allocates.
/// </summary>
public static class MinimapGlyphs
{
    public const int ArrowPoints = 4, FanPoints = 12, ArcMaxPoints = 33;

    /// <summary>The points of each tank shape's outline: Light's diamond, Medium's octagon, Heavy's square, Td's triangle.</summary>
    public const int DiamondPoints = 4, OctagonPoints = 8, SquarePoints = 4, TrianglePoints = 3;

    /// <summary>The most points a tank shape has: the octagon's.</summary>
    public const int ShapeMaxPoints = OctagonPoints;

    /// <summary>The fan's arc points between its two edges, both included.</summary>
    private const int FanArcPoints = FanPoints - 1;

    /// <summary>The progress arc's segments for a whole turn.</summary>
    private const int ArcSegments = ArcMaxPoints - 1;

    /// <summary>The arrow's rear corners stand this share of its length out to each side; its notch this share of the length behind the centre.</summary>
    private const float ArrowHalfWidth = 0.38f, ArrowNotch = 0.2f;

    /// <summary>The octagon is turned half a side's angle so it has a flat top and bottom.</summary>
    private const float OctagonTurn = MathF.PI / OctagonPoints;

    /// <summary>A heading on the map: yaw 0 is up (−y), yaw grows anticlockwise, as <c>Angles.Forward</c> on the ground.</summary>
    public static Vector2 Direction(float yaw) => new(-MathF.Sin(yaw), -MathF.Cos(yaw));

    /// <summary>
    /// A dart <paramref name="size"/> long centred on <paramref name="centre"/>, its tip along <see cref="Direction"/>
    /// (<paramref name="yaw"/>): tip, right rear corner, the notch in the tail, left rear corner.
    /// </summary>
    public static int Arrow(Vector2 centre, float yaw, float size, Span<Vector2> into)
    {
        Vector2 forward = Direction(yaw);
        var right = new Vector2(-forward.Y, forward.X);   // the right hand of a tank facing forward, with y down
        float half = size / 2;
        Vector2 rear = centre - forward * half;
        into[0] = centre + forward * half;
        into[1] = rear + right * (size * ArrowHalfWidth);
        into[2] = centre - forward * (size * ArrowNotch);
        into[3] = rear - right * (size * ArrowHalfWidth);
        return ArrowPoints;
    }

    /// <summary>
    /// <paramref name="shape"/>'s outline in the circle of diameter <paramref name="size"/> round <paramref name="centre"/>,
    /// upright: Light a diamond (4 points), Medium an octagon (8), Heavy a square (4), Td a triangle pointing down (3).
    /// </summary>
    public static int Shape(MinimapShape shape, Vector2 centre, float size, Span<Vector2> into)
    {
        float r = size / 2;
        switch (shape)
        {
            case MinimapShape.Light:
                into[0] = new Vector2(centre.X, centre.Y - r);
                into[1] = new Vector2(centre.X + r, centre.Y);
                into[2] = new Vector2(centre.X, centre.Y + r);
                into[3] = new Vector2(centre.X - r, centre.Y);
                return DiamondPoints;
            case MinimapShape.Heavy:
                float s = r / MathF.Sqrt(2);
                into[0] = centre + new Vector2(-s, -s);
                into[1] = centre + new Vector2(s, -s);
                into[2] = centre + new Vector2(s, s);
                into[3] = centre + new Vector2(-s, s);
                return SquarePoints;
            case MinimapShape.Td:
                float sin60 = MathF.Sqrt(3) / 2;
                into[0] = centre + new Vector2(-r * sin60, -r / 2);
                into[1] = centre + new Vector2(r * sin60, -r / 2);
                into[2] = new Vector2(centre.X, centre.Y + r);
                return TrianglePoints;
            default:
                for (int i = 0; i < OctagonPoints; i++)
                {
                    float a = OctagonTurn + i * (2 * MathF.PI / OctagonPoints);
                    into[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                }
                return OctagonPoints;
        }
    }

    /// <summary>A wreck's cross: two strokes, A–B and C–D, along the diagonals, each end <paramref name="size"/>/2 from the centre.</summary>
    public static (Vector2 A, Vector2 B, Vector2 C, Vector2 D) Cross(Vector2 centre, float size)
    {
        float d = size / 2 / MathF.Sqrt(2);
        return (centre + new Vector2(-d, -d), centre + new Vector2(d, d), centre + new Vector2(d, -d), centre + new Vector2(-d, d));
    }

    /// <summary>
    /// The view fan: <paramref name="apex"/>, then <c>FanPoints − 1</c> points on the arc of radius
    /// <paramref name="length"/> from heading <paramref name="cameraYaw"/> − <paramref name="halfFov"/> to + it.
    /// </summary>
    public static int Fan(Vector2 apex, float cameraYaw, float halfFov, float length, Span<Vector2> into)
    {
        into[0] = apex;
        float from = cameraYaw - halfFov, step = 2 * halfFov / (FanArcPoints - 1);
        for (int i = 0; i < FanArcPoints; i++) into[1 + i] = apex + Direction(from + i * step) * length;
        return FanPoints;
    }

    /// <summary>The progress arc: from 12 o'clock clockwise through points/100 of a turn; 0 points gives 0 points written.</summary>
    /// <remarks>1 + ⌈32 · points/100⌉ points; more than 100 points is a whole turn.</remarks>
    public static int Arc(Vector2 centre, float radius, byte points, Span<Vector2> into)
    {
        if (points == 0) return 0;
        int p = Math.Min((int)points, 100);
        int segments = (ArcSegments * p + 99) / 100;
        // y is down, so turning from −π/2 towards +x is clockwise on the screen.
        float sweep = 2 * MathF.PI * p / 100, start = -MathF.PI / 2;
        for (int i = 0; i <= segments; i++)
        {
            float a = start + sweep * i / segments;
            into[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return segments + 1;
    }
}
